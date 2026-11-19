using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Island;
using Windows.Media.Control;

namespace NabyScape;

internal static class IslandVoiceExternalDiagnostics
{
    internal static async Task RunAsync(App app, CancellationToken token, Action<bool, string> check)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token);
        GlobalSystemMediaTransportControlsSession? browser = null;
        foreach (var candidate in manager.GetSessions())
        {
            if (!candidate.SourceAppUserModelId.Equals("MSEdge", StringComparison.OrdinalIgnoreCase)) continue;
            var properties = await candidate.TryGetMediaPropertiesAsync().AsTask(token);
            if (properties.Title == "LOPATA Browser Fixture 1") { browser = candidate; break; }
        }
        if (browser is null) throw new InvalidOperationException("Start the isolated browser playlist fixture first.");

        app.Media.SelectAutomatic();
        app.Media.Select(browser, manual: false);
        check(app.Media.SourceSelectionId == MediaSourceSelection.AutoId && app.Media.VoiceExternalSource == browser,
            "Voice uses the same browser source that the player shows in Auto");

        Task<IslandVoiceOutcome> Speak(string phrase) => app.ExecuteIslandVoiceRequestAsync(
            IslandVoiceCommands.Parse(phrase, russian: true)!, token, browser.SourceAppUserModelId);

        var mixer = new AudioMixer();
        var before = mixer.Snapshot();
        var browserVolumes = before.Apps.Where(item => Path.GetFileNameWithoutExtension(item.ProcessPath) == "msedge"
            && !item.Muted && item.Volume > 0).ToArray();
        check(browserVolumes.Length > 0, "The isolated browser publishes a controllable audio session");
        try
        {
            app.SetIslandVoiceDucking(true);
            var reduced = mixer.Snapshot();
            check(browserVolumes.Any(expected => reduced.Apps.Any(item => item.Target == expected.Target
                && Math.Abs(item.Volume - expected.Volume * .1) < .001)), "Voice reduces actual browser mixer gain to ten percent");
            check(before.Outputs.All(expected => reduced.Outputs.Any(item => item.Target == expected.Target
                && Math.Abs(item.Volume - expected.Volume) < .001)), "Ducking preserves the Windows output volume");

            check((await Speak("Следующее видео")).Disposition == IslandVoiceDisposition.Applied,
                "Windows accepted voice Next from the Auto browser source");
            for (int attempt = 0; attempt < 20 && (await browser.TryGetMediaPropertiesAsync().AsTask(token)).Title != "LOPATA Browser Fixture 2"; attempt++)
                await Task.Delay(80, token);
            check((await browser.TryGetMediaPropertiesAsync().AsTask(token)).Title == "LOPATA Browser Fixture 2",
                "The real browser handled Next and selected the second item");
            check((await Speak("Следующее видео")).DiagnosticReason == "ExternalActionUnavailable",
                "The browser without Next reports the unavailable action");
        }
        finally { app.SetIslandVoiceDucking(false); }
        var restored = mixer.Snapshot();
        check(browserVolumes.All(expected => restored.Apps.Any(item => item.Target == expected.Target
            && Math.Abs(item.Volume - expected.Volume) < .001)), "Voice restores the actual browser mixer gain after the dialogue");

        check((await Speak("Пауза")).Disposition == IslandVoiceDisposition.Applied, "The plain Pause command goes to the displayed browser");
        for (int attempt = 0; attempt < 20 && browser.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused; attempt++)
            await Task.Delay(80, token);
        check(browser.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
            "The browser actually pauses");
    }
}
