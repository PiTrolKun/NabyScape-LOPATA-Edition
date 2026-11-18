using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using Lopata.Collection;

namespace NabyScape;

internal sealed class LopataMusicIndex(string collectionRoot, string cachePath, bool includeDefaultMusic) : IDisposable
{
    private sealed record Entry(string Path, long Length, long Modified, long Created, string? Scenario)
    {
        internal static Entry Capture(string path, CollectionScanner scanner)
        {
            var file = new FileInfo(path);
            var origin = scanner.GetOriginStamp(path);
            return new(path, file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks,
                origin is not null && origin.Length == file.Length && origin.Modified == file.LastWriteTimeUtc.Ticks
                    && origin.Created == file.CreationTimeUtc.Ticks ? origin.Scenario : null);
        }
    }

    private sealed record Snapshot(int Schema, string Revision, Entry[] Entries);
    private readonly object gate = new();
    private readonly CollectionScanner scanner = new();
    private string? restoredRevision;
    private volatile bool disposed;
    internal int LastOriginReads { get; private set; }

    internal IReadOnlyList<LocalMusicTrack> ReadSaved()
    {
        var config = CollectionConfig.Load(collectionRoot);
        return Tracks(ReadSnapshot(Revision(config))?.Entries ?? [], config);
    }

    internal IReadOnlyList<LocalMusicTrack> Refresh(bool requestCollectionRefresh, CancellationToken token)
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            if (disposed) return [];
            var configuration = CollectionConfig.Load(collectionRoot);
            var revision = Revision(configuration);
            var saved = ReadSnapshot(revision);
            var before = saved?.Entries ?? [];
            if (restoredRevision != revision)
            {
                scanner.RestoreOriginCache(before.Where(entry => entry.Scenario is not null)
                    .Select(entry => KeyValuePair.Create(entry.Path,
                        new CollectionOriginStamp(entry.Length, entry.Modified, entry.Created, entry.Scenario!))));
                restoredRevision = revision;
            }

            if (configuration.Enabled && requestCollectionRefresh)
            {
                try
                {
                    var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        .ToString(System.Globalization.CultureInfo.InvariantCulture);
                    CollectionFiles.WriteAtomic(Path.Combine(collectionRoot, "refresh.request"), Encoding.ASCII.GetBytes(stamp));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }

            var knownRoots = Roots(configuration);
            var scanConfig = configuration with { Roots = knownRoots, SearchEnabled = false };
            var indexed = configuration.Enabled
                ? CollectionPublication.ReadCurrent(collectionRoot, CollectionPublication.Revision(configuration)) : [];
            var candidates = before.Select(entry => entry.Path).Concat(indexed.Select(item => item.Path));
            var scan = scanner.ScanFiltered(collectionRoot, scanConfig, token, fileFilter: LocalMusicLibrary.IsAudioPath,
                additionalPaths: candidates);
            LastOriginReads = scan.OriginReads;
            var confirmed = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in scan.Items)
            {
                token.ThrowIfCancellationRequested();
                try { confirmed[item.Path] = Entry.Capture(item.Path, scanner); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            foreach (var entry in before)
            {
                if (confirmed.Count >= CollectionSnapshot.MaxEntries) break;
                if (!confirmed.ContainsKey(entry.Path) && TemporarilyUnavailable(entry.Path, knownRoots, configuration))
                    confirmed.Add(entry.Path, entry);
            }

            token.ThrowIfCancellationRequested();
            if (revision != Revision(CollectionConfig.Load(collectionRoot)))
                return ReadSaved();

            var next = confirmed.Values.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (saved is null || !before.SequenceEqual(next))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new Snapshot(1, revision, next), CollectionFiles.Json);
                if (bytes.Length <= CollectionSnapshot.MaxBytes)
                    CollectionFiles.WriteAtomic(cachePath, bytes);
            }
            return Tracks(next, configuration);
        }
    }

    private string[] Roots(CollectionConfig config) => config.Roots.Concat(includeDefaultMusic
            ? [Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)] : Array.Empty<string>())
        .Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private string Revision(CollectionConfig config) => CollectionPublication.Revision(config with { Roots = Roots(config) });

    private Snapshot? ReadSnapshot(string revision)
    {
        try
        {
            using var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > CollectionSnapshot.MaxBytes) return null;
            var value = JsonSerializer.Deserialize<Snapshot>(stream, CollectionFiles.Json);
            return value is { Schema: 1, Entries.Length: <= CollectionSnapshot.MaxEntries } && value.Revision == revision
                && value.Entries.All(entry => entry is not null && entry.Path is { Length: > 0 and <= 32700 }
                    && Path.IsPathFullyQualified(entry.Path) && !entry.Path.Any(char.IsControl)
                    && LocalMusicLibrary.IsAudioPath(entry.Path) && entry.Length >= 0
                    && entry.Scenario is null or { Length: <= 256 })
                && value.Entries.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == value.Entries.Length
                ? value : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static LocalMusicTrack[] Tracks(IEnumerable<Entry> entries, CollectionConfig config) =>
        entries.Where(entry => CollectionFilePolicy.IsAllowed(entry.Path, config))
            .Select(entry => new LocalMusicTrack(entry.Path, LocalMusicSource.Collection))
            .OrderBy(track => track.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.Path, StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool TemporarilyUnavailable(string path, string[] knownRoots, CollectionConfig config)
    {
        if (!CollectionFilePolicy.IsAllowed(path, config)) return false;
        try
        {
            _ = File.GetAttributes(path);
            return !CollectionFilePolicy.CanRead(new FileInfo(path));
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException)
        {
            var root = knownRoots.Concat(config.SearchRoots).Where(directory => CollectionFilePolicy.IsWithin(path, directory))
                .OrderByDescending(directory => directory.Length).FirstOrDefault() ?? Path.GetPathRoot(path);
            return root is not null && !Directory.Exists(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException) { return true; }
    }

    public void Dispose()
    {
        disposed = true;
        if (Monitor.TryEnter(gate))
        {
            try { scanner.Dispose(); }
            finally { Monitor.Exit(gate); }
        }
        else ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (gate) scanner.Dispose();
        });
    }
}
