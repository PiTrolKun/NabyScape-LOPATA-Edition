using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Lopata.Island;
using Lopata.Updates;

namespace NabyScape;

internal sealed record LopataUpdateTarget(string Executable, string Version, UpdateDelivery? Channel, bool Automatic);
internal sealed record LopataUpdateCache(string Executable, PublishedUpdateVersion[] Versions);

internal sealed class LopataUpdateMonitor(
    Func<LopataUpdateTarget?> readTarget,
    Func<LopataUpdateTarget, CancellationToken, Task<UpdateOffer?>> check,
    Action<Exception> log, TimeProvider? clock = null) : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly CancellationTokenSource stop = new();
    private DateTimeOffset nextCheck;
    private bool disposed;
    private bool backgroundEnabled;

    internal LopataUpdateTarget? Target { get; private set; }
    internal PublishedUpdateVersion[] Versions { get; private set; } = [];
    internal bool Checking { get; private set; }
    internal string Status { get; private set; } = "LOPATA installation could not be found";
    internal event Action? Changed;

    internal void Start()
    {
        if (disposed || backgroundEnabled) return;
        backgroundEnabled = true;
        timer.Tick += Poll;
        timer.Start();
        _ = RefreshAsync();
    }

    private async void Poll(object? sender, EventArgs e) => await RefreshAsync();

    internal async Task RefreshAsync(bool manual = false)
    {
        if (disposed) return;
        try
        {
            var target = RefreshTarget();
            if (target?.Channel is null || Checking ||
                (!manual && (!backgroundEnabled || !target.Automatic || Now < nextCheck))) return;
            Checking = true;
            nextCheck = Now + CheckInterval;
            Status = "Checking LOPATA updates";
            Changed?.Invoke();
            try
            {
                var offer = await check(target, stop.Token);
                if (disposed || target != RefreshTarget()) return;
                SetVersions(target, Versions.Concat(offer?.PublishedVersions ?? []).ToArray());
                Status = "LOPATA update check complete";
            }
            finally
            {
                Checking = false;
                if (!disposed) Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or FormatException or InvalidOperationException or HttpRequestException or OperationCanceledException or
            KeyNotFoundException or CryptographicException)
        {
            if (disposed) return;
            if (Status == "Could not check LOPATA updates" && !manual) return;
            log(error);
            Status = "Could not check LOPATA updates";
            Changed?.Invoke();
        }
    }

    private DateTimeOffset Now => (clock ?? TimeProvider.System).GetUtcNow();

    private LopataUpdateTarget? RefreshTarget()
    {
        var target = readTarget();
        if (target == Target) return target;
        var before = Target;
        Target = target;
        if (before is null || target is null || !SameChannel(before, target))
            Versions = target is null ? [] : Load(target);
        if (target?.Channel is not null) SetVersions(target, Versions);
        nextCheck = default;
        Status = target is null ? "LOPATA installation could not be found"
            : target.Channel is null ? "Choose an update channel in LOPATA"
            : "LOPATA update check pending";
        Changed?.Invoke();
        return target;
    }

    private static bool SameChannel(LopataUpdateTarget left, LopataUpdateTarget right) =>
        left.Channel == right.Channel && left.Executable.Equals(right.Executable, StringComparison.OrdinalIgnoreCase);

    private static string CacheFile(UpdateDelivery channel) => $"lopata-update-status-{channel}.json";

    private static PublishedUpdateVersion[] Load(LopataUpdateTarget target)
    {
        if (target.Channel is not { } channel) return [];
        var cache = IslandPreferences.ReadJson<LopataUpdateCache>(CacheFile(channel));
        return cache is not null && string.Equals(cache.Executable, target.Executable, StringComparison.OrdinalIgnoreCase)
            ? cache.Versions ?? [] : [];
    }

    private void SetVersions(LopataUpdateTarget target, PublishedUpdateVersion[] candidates)
    {
        var installed = UpdateManifest.NumericVersion(target.Version);
        var next = candidates.Where(item => item is not null && item.Version is not null && item.Tag is not null &&
                string.Equals(PublishedUpdateCatalog.VersionFromTag(item.Tag), item.Version, StringComparison.Ordinal))
            .Select(item => (Item: item, Number: UpdateManifest.NumericVersion(item.Version)))
            .Where(item => item.Number > installed)
            .DistinctBy(item => item.Number)
            .OrderByDescending(item => item.Number)
            .Take(1000)
            .Select(item => item.Item).ToArray();
        if (Versions.SequenceEqual(next)) return;
        Versions = next;
        try { IslandPreferences.SaveJson(CacheFile(target.Channel!.Value), new LopataUpdateCache(target.Executable, next)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log(error); }
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        stop.Cancel();
        stop.Dispose();
    }
}

internal sealed class LopataInstalledUpdateProbe
{
    private string? lastExecutable;
    private ((DateTime Written, long Size) Assembly, (DateTime Written, long Size) Journal,
        (DateTime Written, long Size) Manifest) lastStamp;
    private string? lastVersion;

    internal LopataUpdateTarget? Read()
    {
        var registration = InstalledUpdateState.Read();
        var executable = registration is null ? LopataEdition.HostExecutable()
            : Path.Combine(registration.AppDirectory, "AIHub.exe");
        if (executable is null) return null;
        var assembly = new FileInfo(Path.Combine(Path.GetDirectoryName(executable)!, "AIHub.dll"));
        var directory = registration?.StateDirectory;
        var journal = directory is null ? null : new FileInfo(Path.Combine(directory, "transaction.json"));
        var installed = directory is null ? null : new FileInfo(Path.Combine(directory, "installed.signed.json"));
        var stamp = (Stamp(assembly), Stamp(journal), Stamp(installed));
        if (executable != lastExecutable || stamp != lastStamp)
        {
            string? version = assembly.Exists ? FileVersionInfo.GetVersionInfo(assembly.FullName).ProductVersion : null;
            if (version?.EndsWith("-dev", StringComparison.Ordinal) == true) version = version[..^4];
            if (registration is not null)
            {
                if (journal?.Exists == true &&
                    new UpdateTransaction(registration.Roots(), UpdateReleaseKeys.Trusted, directory!).ReadJournal() is
                        { Phase: not (UpdatePhase.Healthy or UpdatePhase.RolledBack) } pending)
                    version = pending.Previous?.Verify(UpdateReleaseKeys.Trusted).Version;
                else if (version is not null && installed?.Exists == true)
                {
                    SafeUpdatePath.RejectLinks(installed.FullName);
                    if (installed.Length > SignedManifest.MaximumEnvelopeBytes)
                        throw new InvalidDataException("Oversized installed update manifest.");
                    version = SignedManifest.Read(File.ReadAllBytes(installed.FullName)).Verify(UpdateReleaseKeys.Trusted).Version;
                }
            }
            lastVersion = PublishedUpdateCatalog.VersionFromTag(version);
            lastExecutable = executable;
            lastStamp = stamp;
        }
        if (lastVersion is null) return null;
        var config = InstalledUpdateState.UserDataDirectory;
        var channel = UpdateChannelStore.Read(Path.Combine(config, "Updates"));
        var automatic = true;
        var settings = Path.Combine(config, "settings.json");
        if (File.Exists(settings))
        {
            using var file = new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var data = JsonDocument.Parse(file);
            automatic = !data.RootElement.TryGetProperty("updates", out var updates) ||
                updates.ValueKind != JsonValueKind.Object ||
                !updates.TryGetProperty("checkOnStartup", out var enabled) || enabled.ValueKind != JsonValueKind.False;
        }
        return new(executable, lastVersion, channel, automatic);
    }

    private static (DateTime Written, long Size) Stamp(FileInfo? file) =>
        file?.Exists == true ? (file.LastWriteTimeUtc, file.Length) : default;
}
