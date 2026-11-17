using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace NabyScape;

internal sealed record MusicPlaybackState(LocalMusicTrack? Track, string Title, string Artist, bool Playing, double Position, double Duration);

/// <summary>The library and transport shared by the island and a future local music assistant.</summary>
internal interface ILocalMusicTransport
{
    Task<IReadOnlyList<LocalMusicTrack>> TracksAsync(LocalMusicSource source, string folder, CancellationToken token);
    Task<bool> PlayLocalAsync(LocalMusicTrack track, IReadOnlyList<LocalMusicTrack> queue, CancellationToken token = default);
    MusicPlaybackState CurrentMusic { get; }
    Task Command(string command);
    Task Seek(double fraction);
}

public sealed partial class MediaService
{
    private readonly LocalMusicLibrary library;
    private LocalMusicPlayback? localPlayback;
    private LocalMusicTrack[] localQueue = [];
    private int localIndex;
    private CancellationTokenSource? openingLocal;

    internal static string LocalSourceId(LocalMusicSource source) =>
        source == LocalMusicSource.Collection ? "@lopata-library" : "@music-folder";
    internal static bool IsLocalSource(string id) => id is "@lopata-library" or "@music-folder";
    internal const string LocalMeterSource = "@island-audio";
    internal string? AudioMeterSource => localPlayback is null ? Selected?.SourceAppUserModelId : LocalMeterSource;
    internal LocalMusicDetails? LocalDetails => localPlayback?.Details;
    internal LocalMusicTrack? LocalTrack => localPlayback?.Track;
    internal bool NetworkLyricsAllowed => localPlayback?.Details.IsLopata != true;
    internal string? LocalOpenError { get; private set; }

    MusicPlaybackState ILocalMusicTransport.CurrentMusic
    {
        get
        {
            var (elapsed, duration) = Timeline();
            return new(LocalTrack, Title, Artist, Playing, elapsed, duration);
        }
    }

    Task<IReadOnlyList<LocalMusicTrack>> ILocalMusicTransport.TracksAsync(LocalMusicSource source, string folder, CancellationToken token) =>
        Task.Run(() => library.Read(source, folder, token), token);

    Task<bool> ILocalMusicTransport.PlayLocalAsync(LocalMusicTrack track, IReadOnlyList<LocalMusicTrack> queue, CancellationToken token) => PlayLocalAsync(track, queue, token);

    private async Task<bool> PlayLocalAsync(LocalMusicTrack track, IReadOnlyList<LocalMusicTrack> queue, CancellationToken token = default)
    {
        if (disposed || DiagnosticFixture) return false;
        openingLocal?.Cancel();
        using var opening = CancellationTokenSource.CreateLinkedTokenSource(token);
        openingLocal = opening;
        LocalOpenError = null;
        LocalMusicPlayback? candidate = null;
        try
        {
            candidate = await LocalMusicPlayback.OpenAsync(track, library, opening.Token);
            opening.Token.ThrowIfCancellationRequested();
            if (selected is { } external && Playing && localPlayback is null)
            {
                var control = external.GetPlaybackInfo()?.Controls;
                if (control?.IsPauseEnabled != true || !await external.TryPauseAsync().AsTask(opening.Token))
                {
                    LocalOpenError = "Pause the other player before starting a local track.";
                    return false;
                }
            }
            opening.Token.ThrowIfCancellationRequested();
            localPlayback?.Dispose();
            localPlayback = candidate;
            candidate = null;
            localQueue = queue.ToArray();
            localIndex = Array.IndexOf(localQueue,track);
            if (localIndex < 0) { localQueue = [track]; localIndex = 0; }
            sourceSelection.Select(LocalSourceId(track.Source));
            var active = localPlayback;
            active.Ended += () => FinishLocalTrack(active);
            active.Failed += error =>
            {
                if (localPlayback != active) return;
                log(error);
                LocalOpenError = "Local playback failed.";
                PublishLocal();
            };
            active.Play();
            Select(null, manual:false);
            return true;
        }
        catch (OperationCanceledException) when (opening.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            log(error);
            LocalOpenError = "Windows could not play this audio file.";
            return false;
        }
        finally
        {
            candidate?.Dispose();
            if (ReferenceEquals(openingLocal, opening)) openingLocal = null;
        }
    }

    private async void FinishLocalTrack(LocalMusicPlayback completed)
    {
        if (localPlayback != completed) return;
        PublishLocal();
        if (localIndex + 1 < localQueue.Length)
            await PlayLocalAsync(localQueue[localIndex + 1], localQueue);
    }

    private void PublishLocal()
    {
        if (localPlayback is not { } active) return;
        Title = active.Details.Title;
        Artist = active.Details.Artist;
        Artwork = active.Details.Artwork;
        Playing = active.Playing;
        CanToggle = true;
        CanSeek = active.Timeline().duration > 0;
        CanPrevious = localIndex > 0;
        CanNext = localIndex + 1 < localQueue.Length;
        Changed?.Invoke();
    }

    private async Task LocalCommand(string command)
    {
        if (localPlayback is not { } active) return;
        if (command == "next" && localIndex + 1 < localQueue.Length)
            await PlayLocalAsync(localQueue[localIndex + 1], localQueue);
        else if (command == "previous" && localIndex > 0)
            await PlayLocalAsync(localQueue[localIndex - 1], localQueue);
        else if (command == "toggle")
        {
            if (active.Playing) active.Pause();
            else
            {
                var (elapsed, duration) = active.Timeline();
                if (duration > 0 && elapsed >= duration - 0.05) active.Seek(0);
                active.Play();
            }
            PublishLocal();
        }
    }

    private void StopLocalSource()
    {
        openingLocal?.Cancel();
        localPlayback?.Dispose();
        localPlayback = null;
        localQueue = [];
    }
}