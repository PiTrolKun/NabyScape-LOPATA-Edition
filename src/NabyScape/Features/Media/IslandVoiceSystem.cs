using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Island;
using Windows.Media.Control;

namespace NabyScape;

public sealed partial class App
{
    private async Task<IslandVoiceOutcome> ExecuteVoiceSystemAsync(IslandVoiceCommand command, CancellationToken token)
    {
        switch (command.Action)
        {
            case IslandVoiceAction.OpenLopata:
                if (!LopataEdition.TryOpenHost(null, out _)) return Rejected("Could not open LOPATA.");
                return Applied();
            case IslandVoiceAction.ShowTasks:
                overlay?.OpenVoiceWorkPanel();
                return Applied();
            case IslandVoiceAction.TaskStatus:
            case IslandVoiceAction.PauseTask:
            case IslandVoiceAction.ResumeTask:
                return await VoiceWorkAsync(command, token);
            case IslandVoiceAction.WindowsVolume:
                Audio.Read();
                Audio.SetVolume(command.Value / 100d);
                Audio.Read();
                return Audio.Available && Math.Abs(Audio.Volume * 100 - command.Value) <= 2
                    ? Applied(Localization.Format("Windows volume is {0:0} percent.", Audio.Volume * 100))
                    : Rejected("Windows did not confirm the volume change.");
            case IslandVoiceAction.ExternalPause:
            case IslandVoiceAction.ExternalNext:
                return await ExternalMediaAsync(command.Action, command.Query, token);
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private async Task<IslandVoiceOutcome> VoiceWorkAsync(IslandVoiceCommand command, CancellationToken token)
    {
        var state = await LopataLink.ReadVoiceStateAsync(token);
        if (state.Result == "offline") return Rejected("LOPATA is closed");
        if (state.Result != "ok") return Rejected("LOPATA task status is unavailable");
        var jobs = state.Jobs ?? [];
        if (command.Action == IslandVoiceAction.TaskStatus)
        {
            if (jobs.Length == 0) return Applied(Localization.Text(state.Busy ? "LOPATA is busy" : "No active LOPATA tasks"));
            return Applied(string.Join(". ", jobs.Take(3).Select(job =>
            {
                string phase = Localization.Text("LOPATA task " + job.Phase);
                return job.Progress is { } progress && double.IsFinite(progress)
                    ? Localization.Format("{0}: {1}, {2:0} percent.", job.Title, phase, Math.Clamp(progress, 0, 1) * 100)
                    : $"{job.Title}: {phase}";
            })));
        }

        bool pause = command.Action == IslandVoiceAction.PauseTask;
        string query = command.Query;
        var eligible = jobs.Where(job => pause ? job.CanPause : job.CanResume).ToArray();
        if (query is not ("" or "задачу" or "обработку" or "генерацию" or "processing" or "generation"))
            eligible = eligible.Where(job => TaskMatches(query, job.Title)).ToArray();
        if (eligible.Length == 0) return Rejected("No matching LOPATA task supports this action.");
        return eligible.Length == 1
            ? await ControlVoiceJobAsync(eligible[0], pause, token)
            : OfferChoices(eligible.Select(job => new VoiceChoice(job.Title, choice => ControlVoiceJobAsync(job, pause, choice))));
    }

    private static bool TaskMatches(string query, string title)
    {
        if (LocalMusicSearch.MatchText(query, title) >= 80) return true;
        return query is "апскейл" or "upscale" && LocalMusicSearch.Normalize(title).Split(' ')
            .Any(word => word.StartsWith("апскейл", StringComparison.Ordinal) || word.StartsWith("upscal", StringComparison.Ordinal)
                || word.StartsWith("масштабирован", StringComparison.Ordinal) || word.StartsWith("увеличен", StringComparison.Ordinal));
    }

    private async Task<IslandVoiceOutcome> ControlVoiceJobAsync(IslandJob job, bool pause, CancellationToken token)
    {
        var result = await LopataLink.ControlAsync(pause ? "pause" : "resume", job.Id, token);
        if (result.Result != "ok") return Rejected("LOPATA did not confirm the task command.");
        var updated = result.Jobs?.FirstOrDefault(candidate => candidate.Id == job.Id);
        return Applied(Localization.Format(pause ? "LOPATA paused {0}." : "LOPATA resumed {0}.", updated?.Title ?? job.Title));
    }

    private static IslandVoiceOutcome PlaybackSourceChanged() =>
        new(IslandVoiceDisposition.Rejected, Localization.Text("The selected player changed."), DiagnosticReason: "PlaybackSourceChanged");

    private async Task<IslandVoiceOutcome> ExternalMediaAsync(IslandVoiceAction action, string name, CancellationToken token)
    {
        var selected = Media.VoiceExternalSource;
        if (selected is null)
            return new(IslandVoiceDisposition.Rejected,
                Localization.Text("Choose an external player in the island music source menu first."), DiagnosticReason: "NoExternalSource");
        string source = selected.SourceAppUserModelId;
        string display = MediaService.SessionName(selected);
        string requested = name switch
        {
            "ютуб" or "ютубе" => "youtube",
            "спотифай" => "spotify",
            "влс" => "vlc",
            _ => name
        };
        var unidentified = new IslandVoiceOutcome(IslandVoiceDisposition.Rejected,
            Localization.Format("Windows could not confirm {0} in {1}. Say Pause to pause the current player.", requested, display),
            DiagnosticReason: "WrongExternalSource");
        if (requested.Length > 0 && !(source.Contains(requested, StringComparison.OrdinalIgnoreCase)
            || display.Contains(requested, StringComparison.OrdinalIgnoreCase)))
        {
            // A browser is not proof that its active tab belongs to the requested site.
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var media = await selected.TryGetMediaPropertiesAsync().AsTask(timeout.Token);
                if (media is null || !new[] { media.Subtitle, media.AlbumTitle }.Any(value => string.Equals(value, requested,
                    StringComparison.OrdinalIgnoreCase))) return unidentified;
            }
            catch (Exception error) when (!token.IsCancellationRequested && (error is COMException or IOException or OperationCanceledException))
            { return unidentified; }
        }
        if (Media.VoiceExternalSource is not { } current || current.SourceAppUserModelId != source)
            return PlaybackSourceChanged();

        string commandLabel = Localization.Text(action switch
        {
            IslandVoiceAction.Next or IslandVoiceAction.ExternalNext => "Next",
            IslandVoiceAction.Previous => "Previous",
            IslandVoiceAction.Resume => "Play",
            _ => "Pause"
        });
        var refused = new IslandVoiceOutcome(IslandVoiceDisposition.Rejected,
            Localization.Format("{0} did not confirm {1}.", display, commandLabel), DiagnosticReason: "ExternalActionRefused");
        try
        {
            var playback = current.GetPlaybackInfo();
            var controls = playback?.Controls;
            var state = playback?.PlaybackStatus;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var deadline = timeout.Token;
            bool? accepted = action switch
            {
                IslandVoiceAction.Next or IslandVoiceAction.ExternalNext when controls?.IsNextEnabled == true
                    => await current.TrySkipNextAsync().AsTask(deadline),
                IslandVoiceAction.Previous when controls?.IsPreviousEnabled == true
                    => await current.TrySkipPreviousAsync().AsTask(deadline),
                IslandVoiceAction.Pause or IslandVoiceAction.ExternalPause when state == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused
                    => true,
                IslandVoiceAction.Pause or IslandVoiceAction.ExternalPause when controls?.IsPauseEnabled == true
                    => await current.TryPauseAsync().AsTask(deadline),
                IslandVoiceAction.Pause or IslandVoiceAction.ExternalPause when controls?.IsPlayPauseToggleEnabled == true
                    && state == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    => await current.TryTogglePlayPauseAsync().AsTask(deadline),
                IslandVoiceAction.Resume when state == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    => true,
                IslandVoiceAction.Resume when controls?.IsPlayEnabled == true
                    => await current.TryPlayAsync().AsTask(deadline),
                IslandVoiceAction.Resume when controls?.IsPlayPauseToggleEnabled == true
                    && state is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped
                    => await current.TryTogglePlayPauseAsync().AsTask(deadline),
                _ => null
            };
            if (accepted is null)
                return new(IslandVoiceDisposition.Rejected, Localization.Format("{0} does not offer {1} for this item.",
                    display, commandLabel), DiagnosticReason: "ExternalActionUnavailable");
            return accepted.Value ? Applied() : refused;
        }
        catch (Exception error) when (!token.IsCancellationRequested && (error is COMException or IOException or OperationCanceledException))
        { return refused; }
    }
}
