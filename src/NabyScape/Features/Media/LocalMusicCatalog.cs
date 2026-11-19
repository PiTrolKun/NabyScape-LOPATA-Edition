using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Collection;
using Windows.Storage;

namespace NabyScape;

internal sealed record LocalMusicCorrection(string Title = "", string Artist = "", string Album = "",
    string Version = "", string Genre = "", string Description = "");

internal sealed record LocalMusicCard(LocalMusicTrack Track, string Title, string Artist, string Album,
    string Version, double Seconds, string Genre, int Number, string Description, string Lyrics)
{
    internal string Label => string.Join(" · ", new[] { Title, Artist, Version }.Where(value => value.Length > 0));

    internal LocalMusicCard Corrected(LocalMusicCorrection value) => this with
    {
        Title = Keep(value.Title, Title), Artist = Keep(value.Artist, Artist), Album = Keep(value.Album, Album),
        Version = Keep(value.Version, Version), Genre = Keep(value.Genre, Genre),
        Description = Keep(value.Description, Description)
    };

    private static string Keep(string? replacement, string original) =>
        string.IsNullOrWhiteSpace(replacement) ? original : replacement.Trim();
}

internal sealed class LocalMusicCatalog(string profile, Action<Exception> log)
{
    private const int MetadataLimit = 112 * 1024 * 1024;
    private const int TextLimit = 6144;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly SemaphoreSlim scanGate = new(1);
    private readonly object editGate = new();
    private readonly string cache = Path.Combine(profile, "music-cards.cache.json");
    private readonly string edits = Path.Combine(profile, "music-card-corrections.json");
    private Dictionary<string, CachedCard>? saved;
    private Dictionary<string, LocalMusicCorrection>? corrections;

    private sealed record FileStamp(long Length, long Modified, long Created);
    private sealed record CachedCard(FileStamp Audio, FileStamp? Lrc, FileStamp? Text, LocalMusicCard Card);

    internal async Task<IReadOnlyList<LocalMusicCard>> RefreshAsync(IReadOnlyList<LocalMusicTrack> source, CancellationToken token)
    {
        await scanGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            saved ??= Read<CachedCard>(cache);
            var next = new Dictionary<string, CachedCard>(StringComparer.OrdinalIgnoreCase);
            var pending = new List<(LocalMusicTrack Track, FileStamp Audio, FileStamp? Lrc, FileStamp? Text)>();
            foreach (var track in source.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Take(CollectionSnapshot.MaxEntries))
            {
                token.ThrowIfCancellationRequested();
                if (Stamp(track.Path) is not { } audio) continue;
                string lrcPath = Path.ChangeExtension(track.Path, ".lrc"), textPath = Path.ChangeExtension(track.Path, ".txt");
                var lrc = Stamp(lrcPath);
                var text = Stamp(textPath);
                if (saved.TryGetValue(track.Path, out var current) && current.Audio == audio
                    && current.Lrc == lrc && current.Text == text && current.Card.Track.Source == track.Source)
                {
                    next.Add(track.Path, current);
                    continue;
                }

                pending.Add((track, audio, lrc, text));
            }

            var refreshed = new CachedCard?[pending.Count];
            await Parallel.ForEachAsync(Enumerable.Range(0, pending.Count),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (index, limit) =>
                {
                    var item = pending[index];
                    var card = await ReadCardAsync(item.Track, Path.ChangeExtension(item.Track.Path, ".lrc"), item.Lrc,
                        Path.ChangeExtension(item.Track.Path, ".txt"), item.Text, limit).ConfigureAwait(false);
                    if (Stamp(item.Track.Path) == item.Audio)
                        refreshed[index] = new(item.Audio, item.Lrc, item.Text, card);
                }).ConfigureAwait(false);
            foreach (var item in refreshed.OfType<CachedCard>()) next.Add(item.Card.Track.Path, item);

            token.ThrowIfCancellationRequested();
            if (pending.Count > 0 || next.Count != saved.Count)
            {
                try { Write(cache, next); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { log(error); }
            }
            saved = next;
            lock (editGate)
            {
                corrections ??= Read<LocalMusicCorrection>(edits);
                return next.Values.Select(item => corrections.TryGetValue(item.Card.Track.Path, out var correction)
                    ? item.Card.Corrected(correction) : item.Card).ToArray();
            }
        }
        finally { scanGate.Release(); }
    }

    internal LocalMusicCorrection CorrectionFor(LocalMusicTrack track)
    {
        lock (editGate)
        {
            corrections ??= Read<LocalMusicCorrection>(edits);
            return corrections.GetValueOrDefault(track.Path) ?? new();
        }
    }

    internal void SaveCorrection(LocalMusicTrack track, LocalMusicCorrection correction)
    {
        lock (editGate)
        {
            corrections ??= Read<LocalMusicCorrection>(edits);
            var next = new Dictionary<string, LocalMusicCorrection>(corrections, StringComparer.OrdinalIgnoreCase);
            if (correction == new LocalMusicCorrection()) next.Remove(track.Path);
            else next[track.Path] = correction;
            if (next.Count > CollectionSnapshot.MaxEntries) throw new IOException("Too many music card corrections.");
            Write(edits, next);
            corrections = next;
        }
    }

    private static async Task<LocalMusicCard> ReadCardAsync(LocalMusicTrack track,
        string lrcPath, FileStamp? lrc, string textPath, FileStamp? text, CancellationToken token)
    {
        IReadOnlyDictionary<string, string> tags;
        try { tags = CollectionMusicMetadata.ReadTags(track.Path); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { tags = new Dictionary<string, string>(); }
        string Tag(params string[] names) => names.Select(name => tags.GetValueOrDefault(name))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

        string title = Tag("TITLE"), artist = Tag("ARTIST", "ALBUMARTIST"), album = Tag("ALBUM"), genre = Tag("GENRE");
        int.TryParse(Tag("TRACKNUMBER").Split('/')[0], out int number);
        double seconds = 0;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMilliseconds(850));
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(track.Path).AsTask(budget.Token).ConfigureAwait(false);
            var props = await file.Properties.GetMusicPropertiesAsync().AsTask(budget.Token).ConfigureAwait(false);
            if (title.Length == 0) title = props.Title ?? "";
            if (artist.Length == 0) artist = props.Artist ?? "";
            if (album.Length == 0) album = props.Album ?? "";
            if (genre.Length == 0) genre = string.Join(", ", props.Genre.Take(4));
            if (number == 0 && props.TrackNumber <= int.MaxValue) number = (int)props.TrackNumber;
            seconds = props.Duration.TotalSeconds;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or COMException) { }
        token.ThrowIfCancellationRequested();

        string version = Tag("VERSION", "LOPATA_VERSION");
        if (version.Length == 0)
        {
            var name = LocalMusicSearch.Normalize(track.Name);
            version = new[] { "live", "acoustic", "remix", "remaster", "demo", "акустика", "концерт", "ремикс" }
                .FirstOrDefault(word => name.Split(' ').Contains(word)) ?? "";
        }
        string features = string.Join(' ', new[] { Tag("LOPATA_DESCRIPTION", "DESCRIPTION", "COMMENT"), Tag("MOOD"),
            Tag("VOCAL"), Tag("LOPATA_STYLE"), Tag("BPM") }.Where(value => value.Length > 0));
        string lyrics = Tag("LYRICS", "UNSYNCEDLYRICS", "SYNCEDLYRICS", "LOPATA_LRC");
        if (lyrics.Length == 0 && Tag("LOPATA_TIMED_LYRICS") is { Length: > 0 and <= 524288 } structured)
        {
            try
            {
                using var document = JsonDocument.Parse(structured);
                if (document.RootElement.TryGetProperty("SourceLyrics", out var property)) lyrics = property.GetString() ?? "";
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException) { }
        }
        if (lyrics.Length == 0) lyrics = ReadSidecar(lrcPath, lrc);
        if (lyrics.Length == 0) lyrics = ReadSidecar(textPath, text);
        return new(track, Cut(title.Length == 0 ? track.Name : title, 240), Cut(artist, 240), Cut(album, 240),
            Cut(version, 96), seconds, Cut(genre, 240), number, Cut(features, 768), Cut(lyrics, TextLimit));
    }

    private static string ReadSidecar(string path, FileStamp? stamp)
    {
        if (stamp is not { Length: > 0 and <= 524288 }) return "";
        try
        {
            using var reader = File.OpenText(path);
            var buffer = new char[TextLimit];
            return new string(buffer, 0, reader.ReadBlock(buffer));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static string Cut(string value, int limit) => value.Length <= limit ? value : value[..limit];

    private static FileStamp? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && CollectionFilePolicy.CanRead(info)
                ? new(info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static Dictionary<string, T> Read<T>(string path)
    {
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (input.Length <= MetadataLimit && JsonSerializer.Deserialize<Dictionary<string, T>>(input, Json) is { } value
                && value.Count <= CollectionSnapshot.MaxEntries && value.All(pair => pair.Value is not null && Path.IsPathFullyQualified(pair.Key)
                    && (pair.Value is not LocalMusicCorrection correction || correction is
                        { Title: not null, Artist: not null, Album: not null, Version: not null, Genre: not null, Description: not null })
                    && (pair.Value is not CachedCard cached || cached.Card is
                        { Title: not null, Artist: not null, Album: not null, Description: not null, Lyrics: not null,
                            Version: not null, Genre: not null, Track.Path: { Length: > 0 } storedPath }
                        && StringComparer.OrdinalIgnoreCase.Equals(pair.Key, storedPath))))
                return new(value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static void Write<T>(string path, Dictionary<string, T> value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > MetadataLimit) throw new IOException("Music cards exceed the cache limit.");
        CollectionFiles.WriteAtomic(path, bytes);
    }
}
