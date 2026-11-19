using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Collection;
using Lopata.Island;

namespace NabyScape;

internal enum IslandVoiceDisposition { Applied, Rejected, Clarification }
internal enum IslandVoiceFollowUp { None, Choice, Note }

internal sealed record IslandVoiceOutcome(IslandVoiceDisposition Disposition, string? Answer = null,
    IslandVoiceFollowUp FollowUp = IslandVoiceFollowUp.None, string? DiagnosticReason = null);

public sealed partial class App
{
    private sealed record VoiceChoice(string Label, Func<CancellationToken, Task<IslandVoiceOutcome>> Execute);
    private sealed record OfferedChoices(DateTimeOffset Until, VoiceChoice[] Choices);
    private OfferedChoices? offeredChoices;
    private LocalMusicHistory history = null!;
    private (Guid Queue, long Started, TimeSpan Delay)? musicStop;

    private static IslandVoiceOutcome Applied(string? answer = null) => new(IslandVoiceDisposition.Applied, answer);
    private static IslandVoiceOutcome Rejected(string caption) => new(IslandVoiceDisposition.Rejected, Localization.Text(caption));

    internal async Task<IslandVoiceOutcome> ExecuteIslandVoiceRequestAsync(IslandVoiceCommand command, CancellationToken token,
        string? playbackSource = null)
    {
        token.ThrowIfCancellationRequested();
        var choices = offeredChoices;
        offeredChoices = null;
        if (command.Action is IslandVoiceAction.Pause or IslandVoiceAction.Resume or IslandVoiceAction.Next
            or IslandVoiceAction.Previous or IslandVoiceAction.ExternalPause or IslandVoiceAction.ExternalNext
            && playbackSource is not null && playbackSource != (Media.AudioMeterSource ?? ""))
            return PlaybackSourceChanged();
        switch (command.Action)
        {
            case IslandVoiceAction.Pause or IslandVoiceAction.Resume or IslandVoiceAction.Next or IslandVoiceAction.Previous
                when Media.VoiceExternalSource is not null:
                return await ExternalMediaAsync(command.Action, command.Query, token);
            case IslandVoiceAction.Current when Media.VoiceExternalSource is not null:
                string name = string.IsNullOrWhiteSpace(Media.Artist) ? Media.Title : $"{Media.Title}, {Media.Artist}";
                return Applied(Localization.Format(Media.Playing ? "Playing {0}." : "Paused: {0}.", name));
            case IslandVoiceAction.ChooseResult:
                if (choices is null || choices.Until < DateTimeOffset.UtcNow)
                    return Rejected("No voice choices are waiting.");
                if (command.Value < 1 || command.Value > choices.Choices.Length)
                {
                    offeredChoices = choices;
                    return new(IslandVoiceDisposition.Clarification, ChoicePrompt(choices.Choices), IslandVoiceFollowUp.Choice);
                }
                return await choices.Choices[command.Value - 1].Execute(token);
            case IslandVoiceAction.CancelChoice:
                return Applied();
            case IslandVoiceAction.FindTrack:
            case IslandVoiceAction.FindArtist:
            case IslandVoiceAction.FindAlbum:
            case IslandVoiceAction.FindDescription:
            case IslandVoiceAction.FindSimilar:
            case IslandVoiceAction.FindHistory:
                return await SearchMusicAsync(command, token);
            case IslandVoiceAction.ScheduleStop:
                return ScheduleMusicStop(command.Value);
            case IslandVoiceAction.CancelStop:
                if (musicStop is null) return Rejected("No music stop is scheduled.");
                musicStop = null;
                return Applied(Localization.Text("The music stop was cancelled."));
            case IslandVoiceAction.StartTimer:
            case IslandVoiceAction.TimerRemaining:
            case IslandVoiceAction.StartNote:
            case IslandVoiceAction.AddNote:
            case IslandVoiceAction.CancelNote:
            case IslandVoiceAction.ShowNotes:
            case IslandVoiceAction.ShowWeather:
            case IslandVoiceAction.RainForecast:
            case IslandVoiceAction.ShowStatus:
            case IslandVoiceAction.MemoryUsed:
            case IslandVoiceAction.ShowTracks:
                return await ExecuteVoiceWidgetAsync(command, token);
            case IslandVoiceAction.OpenLopata:
            case IslandVoiceAction.ShowTasks:
            case IslandVoiceAction.TaskStatus:
            case IslandVoiceAction.PauseTask:
            case IslandVoiceAction.ResumeTask:
            case IslandVoiceAction.WindowsVolume:
            case IslandVoiceAction.ExternalPause:
            case IslandVoiceAction.ExternalNext:
                return await ExecuteVoiceSystemAsync(command, token);
            default:
                string? answer = await ExecuteIslandVoiceCommandAsync(command, token);
                if (answer is not null)
                    return command.Action == IslandVoiceAction.Current && Media.LocalTrack is not null
                        ? Applied(answer) : new(IslandVoiceDisposition.Rejected, answer);
                return command.StopAfterSeconds > 0 ? ScheduleMusicStop(command.StopAfterSeconds) : Applied();
        }
    }

    private IslandVoiceOutcome OfferChoices(IEnumerable<VoiceChoice> choices)
    {
        var options = choices.Take(3).ToArray();
        offeredChoices = new(DateTimeOffset.UtcNow.AddSeconds(45), options);
        return new(IslandVoiceDisposition.Clarification, ChoicePrompt(options), IslandVoiceFollowUp.Choice);
    }

    private static string ChoicePrompt(VoiceChoice[] options) => Localization.Format("Choose an option: {0}.",
        string.Join(". ", options.Select((option, index) => $"{index + 1}. {option.Label[..Math.Min(100, option.Label.Length)]}")));

    private async Task<IslandVoiceOutcome> SearchMusicAsync(IslandVoiceCommand command, CancellationToken token)
    {
        if (command.Action == IslandVoiceAction.FindHistory && !Settings.MusicListeningHistory)
            return Rejected("Listening history is turned off in Music and sound settings.");
        IReadOnlyList<LocalMusicCard> cards;
        try
        {
            cards = await RefreshVoiceLibraryAsync(token);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Store.Log(error);
            return Rejected("Could not read the local music library.");
        }
        var current = Media.LocalTrack;
        if (command.Action == IslandVoiceAction.FindSimilar && current is null) return Rejected(NoLocalTrack);
        var offers = command.Action == IslandVoiceAction.FindHistory
            ? history.Find(DateTimeOffset.Now, command.Value, command.Query, cards) is { } track
                ? new[] { new MusicOffer(track.Name, [track]) } : []
            : await Task.Run(() => LocalMusicSearch.Find(cards, command.Action, command.Query, current, token), token);
        return offers.Count switch
        {
            0 => Rejected("No matching songs were found in the available local sources."),
            1 => await PlayVoiceOfferAsync(offers[0], command.StopAfterSeconds, token),
            _ => OfferChoices(offers.Select(offer => new VoiceChoice(offer.Label,
                choiceToken => PlayVoiceOfferAsync(offer, command.StopAfterSeconds, choiceToken))))
        };
    }

    private async Task<IslandVoiceOutcome> PlayVoiceOfferAsync(MusicOffer offer, int stopAfterSeconds, CancellationToken token)
    {
        var queue = offer.Queue.Where(track => File.Exists(track.Path) && CollectionFilePolicy.CanRead(new FileInfo(track.Path))).ToArray();
        if (queue.Length == 0) return Rejected("The audio file is unavailable.");
        if (!await Media.PlayVoiceQueueAsync(queue[0], queue, token))
            return Rejected(Media.LocalOpenError ?? "The track could not be opened.");
        if (stopAfterSeconds > 0) return ScheduleMusicStop(stopAfterSeconds);
        return Applied();
    }

    private async Task<IReadOnlyList<LocalMusicCard>> RefreshVoiceLibraryAsync(CancellationToken token)
    {
        var transport = (ILocalMusicTransport)Media;
        var folder = Settings.LocalMusicFolder;
        var favorites = MusicLists.Favorites.ToArray();
        var inputs = await Task.WhenAll(
            transport.TracksAsync(LocalMusicSource.Collection, "", token, requestCollectionRefresh: false),
            transport.TracksAsync(LocalMusicSource.Folder, folder, token));
        var tracks = inputs.SelectMany(value => value).Concat(favorites).ToArray();
        return await Task.Run(() => Media.MusicCatalog.RefreshAsync(tracks, token), token);
    }

    private async Task WarmVoiceLibraryAsync(CancellationToken token)
    {
        try { await RefreshVoiceLibraryAsync(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Store.Log(error); }
    }

    private IslandVoiceOutcome ScheduleMusicStop(int seconds)
    {
        if (Media.LocalTrack is null || Media.LocalQueueId == Guid.Empty) return Rejected(NoLocalTrack);
        musicStop = (Media.LocalQueueId, Stopwatch.GetTimestamp(), TimeSpan.FromSeconds(seconds));
        return Applied(Localization.Format("Music will stop in {0}.", Countdown.FormatSeconds(seconds)));
    }

    internal void CheckVoiceMusicStop(long now)
    {
        if (musicStop is not { } scheduled) return;
        if (Media.LocalQueueId != scheduled.Queue) { musicStop = null; return; }
        if (Stopwatch.GetElapsedTime(scheduled.Started, now) < scheduled.Delay) return;
        musicStop = null;
        Media.SetLocalPlaying(false);
    }

    internal bool ClearMusicListeningHistory() => history.Clear();
}
