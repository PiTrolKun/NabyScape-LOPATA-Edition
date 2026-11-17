using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Lopata.Collection;

namespace NabyScape;

internal enum LocalMusicSource { Collection, Folder }

internal sealed record LocalMusicTrack(string Path, LocalMusicSource Source)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

internal sealed class LocalMusicLibrary
{
    private const int MaximumTracks = CollectionSnapshot.MaxEntries;
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3", ".flac", ".opus", ".ogg", ".m4a", ".aac", ".wma", ".aif", ".aiff" };
    private readonly string collectionRoot;

    internal LocalMusicLibrary(string? collectionRoot = null)
    {
        this.collectionRoot = collectionRoot ?? CollectionPaths.DefaultDirectory;
    }

    internal IReadOnlyList<LocalMusicTrack> Read(LocalMusicSource source, string folder, CancellationToken token)
    {
        if (source == LocalMusicSource.Folder) return ReadFolder(folder, token);
        var configuration = CollectionConfig.Load(collectionRoot);
        var snapshot = configuration.Enabled
            ? CollectionPublication.ReadCurrent(collectionRoot, CollectionPublication.Revision(configuration)) : [];
        if (configuration.Enabled)
        {
            try
            {
                CollectionFiles.WriteAtomic(Path.Combine(collectionRoot, "refresh.request"),
                    System.Text.Encoding.ASCII.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }

        // Known locations and completed exports are available even on a machine without the Explorer worker.
        var roots = configuration.Roots.Concat([Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)])
            .Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var local = configuration with { Roots = roots, SearchEnabled = false };
        using var scanner = new CollectionScanner();
        var checkedPaths = scanner.Scan(collectionRoot, local, token).Items.Select(item => item.Path);
        return MakeList(snapshot.Select(item => item.Path).Concat(checkedPaths)
            .Where(path => CollectionFilePolicy.IsAllowed(path, configuration)), source, token);
    }

    internal bool IsLopata(string path) => CollectionOriginReader.Read(path) is not null ||
        CollectionJournal.TryResolveOrigin(path, collectionRoot) is not null;

    private static IReadOnlyList<LocalMusicTrack> ReadFolder(string folder, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) return [];
        if (!CollectionFilePolicy.CanRead(new DirectoryInfo(folder))) return [];
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 32,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline
        };
        return MakeList(Directory.EnumerateFiles(folder, "*", options), LocalMusicSource.Folder, token);
    }

    private static IReadOnlyList<LocalMusicTrack> MakeList(IEnumerable<string> candidates, LocalMusicSource source, CancellationToken token)
    {
        var result = new List<LocalMusicTrack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates)
        {
            token.ThrowIfCancellationRequested();
            if (!Path.IsPathFullyQualified(path) || !AudioExtensions.Contains(Path.GetExtension(path)) || !seen.Add(path)) continue;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || !CollectionFilePolicy.CanRead(info)) continue;
                result.Add(new(info.FullName, source));
                if (result.Count == MaximumTracks) break;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return result.OrderBy(track => track.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}