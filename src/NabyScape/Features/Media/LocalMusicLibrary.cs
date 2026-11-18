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

internal sealed class LocalMusicLibrary : IDisposable
{
    private const int MaximumTracks = CollectionSnapshot.MaxEntries;
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3", ".flac", ".opus", ".ogg", ".m4a", ".aac", ".wma", ".aif", ".aiff" };
    private readonly string collectionRoot;
    private readonly LopataMusicIndex index;

    internal LocalMusicLibrary(string? collectionRoot = null, string? profile = null)
    {
        this.collectionRoot = collectionRoot ?? CollectionPaths.DefaultDirectory;
        index = new(this.collectionRoot, Path.Combine(profile ?? collectionRoot ?? LopataEdition.ProfileDirectory,
            "lopata-music-index.json"), collectionRoot is null);
    }

    internal IReadOnlyList<LocalMusicTrack> Read(LocalMusicSource source, string folder, CancellationToken token,
        bool requestCollectionRefresh = true) =>
        source == LocalMusicSource.Folder ? ReadFolder(folder, token) : index.Refresh(requestCollectionRefresh, token);

    internal IReadOnlyList<LocalMusicTrack> ReadSavedCollection() => index.ReadSaved();
    internal static bool IsAudioPath(string path) => AudioExtensions.Contains(Path.GetExtension(path));
    public void Dispose() => index.Dispose();

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
            if (!Path.IsPathFullyQualified(path) || !IsAudioPath(path) || !seen.Add(path)) continue;
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