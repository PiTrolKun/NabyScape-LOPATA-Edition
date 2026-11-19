using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lopata.Collection;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace NabyScape;

internal sealed record LocalMusicDetails(string Title, string Artist, LyricResult? Lyrics, bool IsLopata, ImageSource? Artwork)
{
    private const int MaximumLyricLength = 512 * 1024;

    internal static async Task<LocalMusicDetails> ReadAsync(LocalMusicTrack track, LocalMusicLibrary library, CancellationToken token)
    {
        var details = await Task.Run(() => ReadEmbedded(track, library), token);
        using var metadataBudget = CancellationTokenSource.CreateLinkedTokenSource(token);
        metadataBudget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(track.Path).AsTask(metadataBudget.Token);
            var metadata = await file.Properties.GetMusicPropertiesAsync().AsTask(metadataBudget.Token);
            using var thumbnail = details.Artwork is null ? await file.GetThumbnailAsync(ThumbnailMode.MusicView, 192).AsTask(metadataBudget.Token) : null;
            ImageSource? artwork = null;
            if (thumbnail is { Type: ThumbnailType.Image, Size: > 0 and <= 2 * 1024 * 1024 })
            {
                using var stream = thumbnail.AsStreamForRead();
                artwork = DecodeArtwork(stream);
            }
            return details with
            {
                Title = string.IsNullOrWhiteSpace(metadata.Title) || details.Title != track.Name ? details.Title : metadata.Title,
                Artist = string.IsNullOrWhiteSpace(metadata.Artist) || details.Artist.Length != 0 ? details.Artist : metadata.Artist,
                Artwork = details.Artwork ?? artwork
            };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return details; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or System.Runtime.InteropServices.COMException)
        { return details; }
    }

    private static LocalMusicDetails ReadEmbedded(LocalMusicTrack track, LocalMusicLibrary library)
    {
        var manufactured = track.Source == LocalMusicSource.Collection || library.IsLopata(track.Path);
        IReadOnlyDictionary<string,string> tags;
        try { tags = CollectionMusicMetadata.ReadTags(track.Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        {
            // Do not send a network lookup for a file whose embedded origin could not be read.
            return new(track.Name, "", null, true, null);
        }

        string Tag(string field) => tags.TryGetValue(field, out var value) && value.Length <= MaximumLyricLength ? value : "";
        string title = Tag("TITLE").Trim(), artist = Tag("ARTIST").Trim();
        string lrc = Tag("LOPATA_LRC");
        if (lrc.Length == 0) lrc = Tag("SYNCEDLYRICS");
        string plain = Tag("LYRICS");
        if (plain.Length == 0) plain = Tag("UNSYNCEDLYRICS");
        var lines = LyricTiming.Parse(lrc);
        if (lines.Length == 0 && plain.StartsWith('['))
        {
            lines = LyricTiming.Parse(plain);
            if (lines.Length > 0) plain = "";
        }
        var json = Tag("LOPATA_TIMED_LYRICS");
        if (json.Length > 0 && (lines.Length == 0 || plain.Length == 0))
        {
            try
            {
                using var structured = JsonDocument.Parse(json);
                var root = structured.RootElement;
                string text = root.GetProperty("SourceLyrics").GetString() ?? "";
                if (lines.Length == 0) lines = ReadTimingLines(root, text);
                if (plain.Length == 0) plain = text;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { }
        }
        if (lines.Length == 0)
        {
            string sidecar = System.IO.Path.ChangeExtension(track.Path, ".lrc");
            try
            {
                var file = new FileInfo(sidecar);
                if (file.Exists && file.Length <= MaximumLyricLength && CollectionFilePolicy.CanRead(file))
                    lines = LyricTiming.Parse(File.ReadAllText(sidecar));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        LyricResult? lyrics = lines.Length > 0 ? new(lines, Localization.Text("Local synced lyrics")) :
            string.IsNullOrWhiteSpace(plain) ? null : new([], Localization.Text("Local track lyrics"), plain);
        return new(title.Length == 0 ? track.Name : title, artist, lyrics, manufactured, EmbeddedArtwork(tags));
    }

    private static LyricLine[] ReadTimingLines(JsonElement root, string text)
    {
        var result = new List<LyricLine>();
        foreach (var line in root.GetProperty("Lines").EnumerateArray().Take(10000))
        {
            int start = line.GetProperty("SourceStart").GetInt32(), length = line.GetProperty("SourceLength").GetInt32();
            double seconds = line.GetProperty("StartSeconds").GetDouble();
            if (start >= 0 && length >= 0 && start <= text.Length && length <= text.Length - start && double.IsFinite(seconds) && seconds >= 0)
                result.Add(new(seconds, text.Substring(start, length)));
        }
        return result.OrderBy(line => line.At).ToArray();
    }

    private static ImageSource? EmbeddedArtwork(IReadOnlyDictionary<string, string> tags)
    {
        try
        {
            if (tags.TryGetValue("COVER_BYTES", out var id3))
            {
                using var source = new MemoryStream(Convert.FromBase64String(id3), false);
                return DecodeArtwork(source);
            }
            if (!tags.TryGetValue("METADATA_BLOCK_PICTURE", out var field)) return null;
            using var input = new MemoryStream(Convert.FromBase64String(field), false);
            using var reader = new BinaryReader(input);
            int ReadNumber()
            {
                var bytes = reader.ReadBytes(4);
                if (bytes.Length != 4) throw new EndOfStreamException();
                return System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes);
            }
            void SkipSizedField()
            {
                int length = ReadNumber();
                if (length < 0 || length > input.Length - input.Position) throw new InvalidDataException();
                input.Position += length;
            }
            _ = ReadNumber();
            SkipSizedField();
            SkipSizedField();
            input.Position += 16;
            int pictureLength = ReadNumber();
            if (pictureLength <= 0 || pictureLength > input.Length - input.Position) return null;
            using var picture = new MemoryStream(reader.ReadBytes(pictureLength), false);
            return DecodeArtwork(picture);
        }
        catch (Exception error) when (error is FormatException or IOException or InvalidDataException or
            ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { return null; }
    }

    private static BitmapImage DecodeArtwork(Stream stream)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 192;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>One decoded file and its real playback position. All commands run on the island dispatcher.</summary>
internal sealed class LocalMusicPlayback : IDisposable
{
    private readonly MediaPlayer player = new();
    private readonly double defaultVolume;
    internal LocalMusicTrack Track { get; }
    internal LocalMusicDetails Details { get; private set; }
    internal bool Playing { get; private set; }
    internal event Action? Ended;
    internal event Action<Exception>? Failed;

    private LocalMusicPlayback(LocalMusicTrack track)
    {
        defaultVolume = player.Volume;
        Track = track;
        Details = new(track.Name, "", null, track.Source == LocalMusicSource.Collection, null);
        player.MediaEnded += (_, _) => { Playing = false; Ended?.Invoke(); };
    }

    internal static async Task<LocalMusicPlayback> OpenAsync(LocalMusicTrack track, LocalMusicLibrary library,
        LocalMusicCorrection correction, CancellationToken token)
    {
        var result = new LocalMusicPlayback(track);
        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Opened(object? _, EventArgs args) => opened.TrySetResult(true);
        void Failed(object? _, ExceptionEventArgs args) => opened.TrySetException(args.ErrorException);
        result.player.MediaOpened += Opened;
        result.player.MediaFailed += Failed;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        Task<LocalMusicDetails> metadata = LocalMusicDetails.ReadAsync(track, library, budget.Token);
        try
        {
            result.player.Open(new Uri(track.Path));
            await opened.Task.WaitAsync(budget.Token);
            result.Details = await metadata;
            if (correction.Title.Length > 0 || correction.Artist.Length > 0)
                result.Details = result.Details with
                {
                    Title = correction.Title.Length > 0 ? correction.Title : result.Details.Title,
                    Artist = correction.Artist.Length > 0 ? correction.Artist : result.Details.Artist
                };
            result.player.MediaFailed += (_, args) =>
            {
                result.Playing = false;
                result.Failed?.Invoke(args.ErrorException);
            };
            return result;
        }
        catch
        {
            budget.Cancel();
            try { await metadata; } catch (Exception) { }
            result.Dispose();
            throw;
        }
        finally
        {
            result.player.MediaOpened -= Opened;
            result.player.MediaFailed -= Failed;
        }
    }

    internal (double elapsed, double duration) Timeline() =>
        (player.Position.TotalSeconds, player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0);

    internal void Play() { player.Play(); Playing = true; }
    internal void Pause() { player.Pause(); Playing = false; }
    internal void SetVolume(double volume) => player.Volume = defaultVolume * volume;
    internal void Restart()
    {
        player.Stop();
        player.Position = TimeSpan.Zero;
        Play();
    }
    internal void Seek(double fraction)
    {
        var duration = Timeline().duration;
        if (duration > 0) player.Position = TimeSpan.FromSeconds(duration * Math.Clamp(fraction, 0, 1));
    }
    public void Dispose() { Playing = false; player.Close(); }
}
