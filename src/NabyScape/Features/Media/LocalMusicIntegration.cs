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
    Task<IReadOnlyList<LocalMusicTrack>> TracksAsync(LocalMusicSource source, string folder, CancellationToken token,
        bool requestCollectionRefresh = true);
    Task<IReadOnlyList<LocalMusicTrack>> SavedCollectionAsync(CancellationToken token);
    Task<bool> PlayLocalAsync(LocalMusicTrack track, IReadOnlyList<LocalMusicTrack> queue, CancellationToken token = default);
    MusicPlaybackState CurrentMusic { get; }
    Task Command(string command);
    Task Seek(double fraction);
}

public sealed partial class MediaService
{
    private readonly LocalMusicLibrary library;
    private LocalMusicPlayback? localPlayback;
    private LocalMusicQueue localQueue = new();
    private LocalPlaybackOrder playbackOrder;
    private int localMusicVolume = 100;
    private bool voiceDucking;
    internal event Action<string>? LocalNavigationFailed;
    internal event Action<LocalMusicTrack, string>? LocalPlaybackStarted;
    internal LocalMusicCatalog MusicCatalog { get; }
    internal Guid LocalQueueId { get; private set; }

    private void ConfigurePlaybackOrder(LocalPlaybackOrder order)
    {
        if (playbackOrder == order) return;
        playbackOrder = order;
        if (localPlayback is not { } active) return;
        localQueue.Reorder(active.Track, order);
        PublishLocal();
    }

    private void ConfigureLocalVolume(int volume)
    {
        localMusicVolume = Math.Clamp(volume, 0, 100);
        ApplyLocalVolume();
    }

    internal void SetVoiceDucking(bool enabled)
    {
        voiceDucking = enabled;
        ApplyLocalVolume();
    }

    private void ApplyLocalVolume() => localPlayback?.SetVolume(localMusicVolume / 100d * (voiceDucking ? .1 : 1));
    private CancellationTokenSource? openingLocal;

    internal static string LocalSourceId(LocalMusicSource source) =>
        source == LocalMusicSource.Collection ? "@lopata-library" : "@music-folder";
    internal const string FavoritesSourceId = "@favorite-tracks";
    internal static bool IsLocalSource(string id) => id is "@lopata-library" or "@music-folder" or FavoritesSourceId;
    internal const string LocalMeterSource = "@island-audio";
    internal string? AudioMeterSource => localPlayback is null ? Selected?.SourceAppUserModelId : LocalMeterSource;
    internal GlobalSystemMediaTransportControlsSession? VoiceExternalSource =>
        localPlayback is null && !SourceDisabled ? Selected : null;
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

    Task<IReadOnlyList<LocalMusicTrack>> ILocalMusicTransport.TracksAsync(LocalMusicSource source, string folder,
        CancellationToken token, bool requestCollectionRefresh) =>
        Task.Run(() => library.Read(source, folder, token, requestCollectionRefresh), token);

    Task<IReadOnlyList<LocalMusicTrack>> ILocalMusicTransport.SavedCollectionAsync(CancellationToken token) =>
        Task.Run(library.ReadSavedCollection, token);

    Task<bool> ILocalMusicTransport.PlayLocalAsync(LocalMusicTrack track, IReadOnlyList<LocalMusicTrack> queue, CancellationToken token) => PlayLocalAsync(track, queue, token);

    private async Task<bool> PlayLocalAsync(LocalMusicTrack track, IReadOnlyList<LocalMusicTrack>? queue,
        CancellationToken token = default, bool allowExternalPause = true)
    {
        if (disposed || DiagnosticFixture) return false;
        if (!System.IO.File.Exists(track.Path))
        {
            LocalOpenError = "The audio file is unavailable.";
            return false;
        }
        openingLocal?.Cancel();
        using var opening = CancellationTokenSource.CreateLinkedTokenSource(token);
        openingLocal = opening;
        LocalOpenError = null;
        LocalMusicPlayback? candidate = null;
        try
        {
            candidate = await LocalMusicPlayback.OpenAsync(track, library, MusicCatalog.CorrectionFor(track), opening.Token);
            opening.Token.ThrowIfCancellationRequested();
            if (selected is { } external && Playing && localPlayback is null)
            {
                var control = external.GetPlaybackInfo()?.Controls;
                if (!allowExternalPause || control?.IsPauseEnabled != true || !await external.TryPauseAsync().AsTask(opening.Token))
                {
                    LocalOpenError = "Pause the other player before starting a local track.";
                    return false;
                }
            }
            opening.Token.ThrowIfCancellationRequested();
            localPlayback?.Dispose();
            localPlayback = candidate;
            candidate = null;
            if (queue is not null)
            {
                localQueue.Start(queue, track, playbackOrder);
                LocalQueueId = Guid.NewGuid();
            }
            else localQueue.MoveTo(track);
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
            ApplyLocalVolume();
            active.Play();
            Select(null, manual:false);
            LocalPlaybackStarted?.Invoke(track, active.Details.Artist);
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
        if (localPlayback != completed || openingLocal is not null) return;
        if (playbackOrder == LocalPlaybackOrder.RepeatTrack)
        {
            completed.Restart();
            PublishLocal();
            return;
        }

        PublishLocal();
        await NavigateLocalAsync(localQueue.Next);
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
        CanPrevious = localQueue.Previous is not null;
        CanNext = localQueue.Next is not null;
        Changed?.Invoke();
    }

    private async Task LocalCommand(string command)
    {
        if (localPlayback is not { } active) return;
        if (command == "next")
            await NavigateLocalAsync(localQueue.Next);
        else if (command == "previous")
            await NavigateLocalAsync(localQueue.Previous);
        else if (command == "toggle")
            SetLocalPlaying(!active.Playing);
    }

    internal bool SetLocalPlaying(bool playing)
    {
        if (localPlayback is not { } active) return false;
        if (playing != active.Playing)
        {
            if (!playing) active.Pause();
            else
            {
                var (elapsed, duration) = active.Timeline();
                if (duration > 0 && elapsed >= duration - 0.05) active.Seek(0);
                active.Play();
            }
            PublishLocal();
        }
        return true;
    }

    internal async Task<bool> NavigateLocalFromVoiceAsync(bool forward, CancellationToken token) =>
        localPlayback is not null && openingLocal is null && (forward ? localQueue.Next : localQueue.Previous) is { } track
        && await PlayLocalAsync(track, null, token, allowExternalPause: false);

    internal Task<bool> PlayVoiceQueueAsync(LocalMusicTrack track, IReadOnlyList<LocalMusicTrack> queue, CancellationToken token) =>
        PlayLocalAsync(track, queue, token, allowExternalPause: false);

    internal bool SeekLocalSeconds(int seconds, bool relative)
    {
        if (localPlayback is not { } active) return false;
        var (elapsed, duration) = active.Timeline();
        if (duration <= 0) return false;
        active.Seek(((relative ? elapsed : 0) + seconds) / duration);
        PublishLocal();
        return true;
    }

    private async Task NavigateLocalAsync(LocalMusicTrack? track)
    {
        if (track is null || openingLocal is not null) return;
        if (!await PlayLocalAsync(track, null) && LocalOpenError is { } message)
            LocalNavigationFailed?.Invoke(message);
    }

    private void StopLocalSource()
    {
        openingLocal?.Cancel();
        localPlayback?.Dispose();
        localPlayback = null;
        localQueue = new();
        LocalQueueId = Guid.Empty;
    }
}
