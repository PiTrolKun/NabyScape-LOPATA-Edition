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
    Action<Exception> log) : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);
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
            var target = readTarget();
            if (target != Target)
            {
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
            }
            if (target?.Channel is null || Checking ||
                (!manual && (!backgroundEnabled || !target.Automatic || DateTimeOffset.UtcNow < nextCheck))) return;
            Checking = true;
            nextCheck = DateTimeOffset.UtcNow + CheckInterval;
            Status = "Checking LOPATA updates";
            Changed?.Invoke();
            try
            {
                var offer = await check(target, stop.Token);
                if (disposed || target != Target) return;
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
    private (DateTime DllWritten, DateTime JournalWritten, long DllLength, long JournalLength) lastStamp;
    private string? lastVersion;

    internal LopataUpdateTarget? Read()
    {
        var registered = InstalledUpdateState.Read();
        var executable = LopataEdition.HostExecutable(registered);
        if (executable is null) return null;
        var directory = Path.GetDirectoryName(executable)!;
        var dll = new FileInfo(Path.Combine(directory, "AIHub.dll"));
        var state = string.Equals(directory, registered?.AppDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            ? registered : null;
        var journal = state is null ? null : new FileInfo(Path.Combine(state.StateDirectory, "transaction.json"));
        var stamp = (dll.LastWriteTimeUtc, journal?.LastWriteTimeUtc ?? default, dll.Exists ? dll.Length : 0,
            journal?.Exists == true ? journal.Length : 0);
        if (executable != lastExecutable || stamp != lastStamp)
        {
            var version = dll.Exists ? FileVersionInfo.GetVersionInfo(dll.FullName).ProductVersion : null;
            if (version is not null && version.EndsWith("-dev", StringComparison.Ordinal)) version = version[..^4];
            if (state is not null && journal?.Exists == true &&
                new UpdateTransaction(state.Roots(), UpdateReleaseKeys.Trusted, state.StateDirectory).ReadJournal() is
                    { Phase: not (UpdatePhase.Healthy or UpdatePhase.RolledBack) } pending)
                version = pending.Previous?.Verify(UpdateReleaseKeys.Trusted).Version;
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
}
