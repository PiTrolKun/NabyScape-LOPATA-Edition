using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lopata.Island;
using NAudio.Wave;

namespace NabyScape;

internal static class IslandVoicePlaybackDiagnostics
{
    internal static async Task RunAsync(App app, OverlayWindow island, string directory)
    {
        var results = new List<string>();
        void Check(bool condition, string scenario)
        {
            if (!condition) throw new InvalidOperationException(scenario);
            results.Add("PASS " + scenario);
        }
        int exitCode = 0;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        try
        {
            await CheckCaptureAsync(budget.Token, Check);
            CheckJournal(app.Store.DirectoryPath, Check);
            CheckTranscriptWindow(Check);
            Check(IslandVoiceCommands.Parse("Промотай на пятнадцать секунд.", russian: true) is null,
                "An incomplete relative seek cannot turn into an absolute seek");
            island.DiagnosticRun = true;
            island.Reveal(false);
            island.SelectPage("media", false);
            await Task.Delay(300, budget.Token);
            var microphone = Diagnostics.Descendants(island).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "Lopata.IslandVoice.Toggle");
            Check(microphone.IsVisible && !app.IslandVoiceEnabled, "Player microphone button is visible and starts off");
            foreach (string lang in new[] { "ru", "en" })
            {
                using var stream = typeof(App).Assembly.GetManifestResourceStream($"NabyScape.IslandReady.{lang}.wav");
                using var audio = new WaveFileReader(stream ?? throw new InvalidDataException(lang));
                Check(audio.TotalTime.TotalSeconds is > 0 and < 12, $"Bundled {lang} reply is playable");
            }

            var folder = Path.Combine(directory, "voice-fixture");
            Directory.CreateDirectory(folder);
            var tracks = new[] { "First", "Second" }.Select(name => new LocalMusicTrack(
                Path.Combine(folder, name + ".wav"), LocalMusicSource.Folder)).ToArray();
            foreach (var track in tracks)
            {
                using var writer = new WaveFileWriter(track.Path, new WaveFormat(16000, 16, 1));
                writer.Write(new byte[8 * 32000]);
            }
            var player = (ILocalMusicTransport)app.Media;
            Check(await player.PlayLocalAsync(tracks[0], tracks, budget.Token), "Opened the same local transport used by the music widget");

            async Task<string?> Voice(string phrase) => await app.ExecuteIslandVoiceCommandAsync(
                IslandVoiceCommands.Parse(phrase, russian: true) ?? throw new InvalidOperationException(phrase), budget.Token);
            Check(await Voice("Pause.") is null && !player.CurrentMusic.Playing, "An English transcription of pause stops local playback in Russian mode");
            Check(await Voice("Продолжай.") is null && player.CurrentMusic.Playing, "The observed alternate Russian wording resumes the local track");
            Check(await Voice("Дальше") is null && player.CurrentMusic.Track == tracks[1], "The alternate next command follows the local queue");
            Check(await Voice("Предыдущая") is null && player.CurrentMusic.Track == tracks[0], "Voice previous uses the local queue");
            await Voice("Пауза");
            Check(await Voice("Перемотай на 0:03") is null && Math.Abs(player.CurrentMusic.Position - 3) < .5,
                "Voice seek updates the playback position");
            var systemVolume = app.Audio.Volume;
            var systemMute = app.Audio.Muted;
            var output = LocalPlayer(app.Media);
            double originalVolume = output.Volume;
            Check(await Voice("Погромче") is null && app.Settings.LocalMusicVolume == 100
                && Math.Abs(output.Volume - originalVolume) < .0001, "Louder at 100% leaves the actual audio gain at its limit");
            Check(await Voice("Потише") is null && app.Settings.LocalMusicVolume == 90
                && Math.Abs(output.Volume - originalVolume * .9) < .0001, "Quieter at 100% changes the actual local audio gain to 90%");
            Check(await Voice("Громксть плеера 30%.") is null
                && await Voice("Убавь громкость") is null && app.Settings.LocalMusicVolume == 20
                && Math.Abs(app.Audio.Volume - systemVolume) < .001 && app.Audio.Muted == systemMute,
                "Voice volume is local and leaves the Windows output untouched");
            CheckVolumeDisplay(island, app.Settings.LocalMusicVolume, Check);
            CheckDucking(app.Media, Check);
            Check((await Voice("Что за песня?"))?.Contains("First", StringComparison.Ordinal) == true,
                "The spoken reply reflects the actual current track");
            await Voice("Повтори эту песню");
            Check(app.Settings.PlaybackOrder == LocalPlaybackOrder.RepeatTrack, "Voice repeat uses the player's playback setting");
            await Voice("Играй подряд");
            Check(app.Settings.PlaybackOrder == LocalPlaybackOrder.Sequential, "Voice in-order uses the player's playback setting");
            await Voice("Добавь эту песню в избранное");
            await Voice("Добавь эту песню в избранное");
            Check(app.MusicLists.Favorites.SequenceEqual([tracks[0]]), "Repeated voice add is idempotent");
            await Voice("Убери из избранного");
            Check(app.MusicLists.Favorites.Count == 0, "Voice removes the same favorite the player shows");
            app.MusicLists.ToggleFavorite(tracks[1]);
            Check(await Voice("Включи избранное вперемешку") is null && player.CurrentMusic.Track == tracks[1]
                && app.MusicLists.Selected == LocalMusicList.Favorites && app.Settings.PlaybackOrder == LocalPlaybackOrder.Shuffle,
                "Voice favorites starts the saved local queue in shuffle mode");
            await IslandVoiceAdvancedDiagnostics.RunAsync(app, island, folder, budget.Token, Check);
        }
        catch (Exception error)
        {
            results.Add("FAIL " + error);
            exitCode = 1;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(directory, "island-voice-report.txt"), results);
            app.Shutdown(exitCode);
        }
    }

    private static void CheckDucking(MediaService media, Action<bool, string> check)
    {
        var player = LocalPlayer(media);
        double original = player.Volume;
        media.SetVoiceDucking(true);
        check(original > 0 && Math.Abs(player.Volume - original * .1) < .0001, "The dialogue ducks local music to 10% of its current level");
        media.SetVoiceDucking(false);
        check(Math.Abs(player.Volume - original) < .0001, "The local player restores its volume after the dialogue");
    }

    private static System.Windows.Media.MediaPlayer LocalPlayer(MediaService media)
    {
        const System.Reflection.BindingFlags fields =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var playback = typeof(MediaService).GetField("localPlayback", fields)?.GetValue(media)
            ?? throw new InvalidOperationException("Missing diagnostic playback.");
        return (System.Windows.Media.MediaPlayer)(typeof(LocalMusicPlayback).GetField("player", fields)?.GetValue(playback)
            ?? throw new InvalidOperationException("Missing diagnostic media player."));
    }

    private static void CheckVolumeDisplay(OverlayWindow island, int level, Action<bool, string> check)
    {
        island.UpdateLayout();
        var widgets = Diagnostics.Descendants(island).OfType<MediaControls>().Single(candidate => candidate.IsVisible);
        var button = widgets.VolumeButton;
        check(AutomationProperties.GetName(button) == Localization.Text("Local player volume")
            && ((StackPanel)button.Content).Children.OfType<TextBlock>().Any(text => text.Text == $"{level}%"),
            "The music widget shows the local volume after a voice command");
        widgets.Toggle("volume");
        island.UpdateLayout();
        var captions = Diagnostics.Descendants(widgets.Popover).OfType<TextBlock>().Select(text => text.Text).ToArray();
        check(captions.Contains($"{Localization.Text("Local player volume")} · {level}%")
            && captions.Contains(Localization.Text("Windows volume")), "The volume popup distinguishes player and Windows levels");
        widgets.Close(false);
    }

    private static async Task CheckCaptureAsync(CancellationToken token, Action<bool, string> check)
    {
        using var capture = new IslandVoiceCapture();
        var speech = Enumerable.Repeat((short)1200, 800).ToArray();
        byte[] frame = new byte[1600];
        Buffer.BlockCopy(speech, 0, frame, 0, frame.Length);
        var quiet = new byte[1600];
        capture.ListenForWake(2);
        for (int i = 0; i < 16; i++) capture.Push(frame);
        capture.IgnoreInput();
        for (int i = 0; i < 16; i++) capture.Push(frame);
        capture.ListenForCommand();
        var command = capture.ReadAsync(token);
        capture.Push(frame);
        for (int i = 0; i < 17; i++) capture.Push(quiet);
        check(!command.IsCompleted, "Wake audio, the readiness reply and a brief noise do not fill the command window");
        for (int i = 0; i < 4; i++) capture.Push(frame);
        for (int i = 0; i < 15; i++) capture.Push(quiet);
        var pcm = await command;
        check(pcm.Length is > 0 and < 32000 * 2, "The command window receives a new phrase immediately and ends at a pause");
        capture.ListenForCommand();
        var timeout = capture.ReadAsync(token);
        for (int i = 0; i < 100; i++) capture.Push(quiet);
        check((await timeout).Length == 0, "A silent command window expires after five seconds");
        capture.IgnoreInput();
        for (int i = 0; i < 20; i++) capture.Push(frame);
        capture.ListenForCommand(noteText: true);
        var note = capture.ReadAsync(token);
        check(!note.IsCompleted, "The spoken note prompt is excluded before note capture starts");
        for (int i = 0; i < 4; i++) capture.Push(frame);
        for (int i = 0; i < 20; i++) capture.Push(quiet);
        check(!note.IsCompleted, "A one-second pause does not truncate a dictated note");
        for (int i = 0; i < 4; i++) capture.Push(frame);
        for (int i = 0; i < 30; i++) capture.Push(quiet);
        check((await note).Length > 32000 * 2, "The second part of the dictated note survives the pause");
    }

    private static void CheckJournal(string profile, Action<bool, string> check)
    {
        var journal = new IslandVoiceJournal(profile);
        journal.Write(IslandVoiceEvent.CommandUnchanged, IslandVoiceAction.Louder, playerVolume: 100,
            previousPlayerVolume: 100, localTrackActive: true);
        using var recorded = System.Text.Json.JsonDocument.Parse(File.ReadLines(Path.Combine(profile, IslandVoiceJournal.FileName)).Last());
        var value = recorded.RootElement;
        check(value.GetProperty("stage").GetString() == "CommandUnchanged"
            && value.GetProperty("action").GetString() == "Louder" && value.GetProperty("playerVolume").GetInt32() == 100
            && value.GetProperty("previousPlayerVolume").GetInt32() == 100 && value.GetProperty("localTrackActive").GetBoolean(),
            "The voice journal records commands that hit the current volume limit");
    }

    private static void CheckTranscriptWindow(Action<bool, string> check)
    {
        using var preview = new IslandVoiceTranscriptWindow(russian: true);
        preview.Show();
        preview.ShowWake("Лопата, приём.");
        preview.ShowCommand("Пауса.", new IslandVoiceCommand(IslandVoiceAction.Pause) { Similarity = 80 });
        preview.ShowCommand("Громкость плеера 37%.", new IslandVoiceCommand(IslandVoiceAction.Volume, 37));
        preview.ShowCommand("Потише.", new IslandVoiceCommand(IslandVoiceAction.Quieter), 100, 90);
        preview.ShowCommand("Погромче.", new IslandVoiceCommand(IslandVoiceAction.Louder), 100, 100);
        preview.ShowCommand("Сторонняя фраза.", null);
        preview.UpdateLayout();
        var controls = Diagnostics.Descendants(preview).ToArray();
        var wake = controls.OfType<TextBlock>().Single(text =>
            AutomationProperties.GetAutomationId(text) == "Lopata.IslandVoice.WakeTranscript");
        var commands = controls.OfType<TextBox>().Single(text =>
            AutomationProperties.GetAutomationId(text) == "Lopata.IslandVoice.CommandTranscripts");
        check(wake.Text == "Лопата, приём." && commands.Text.Contains("«Пауса.»", StringComparison.Ordinal)
            && commands.Text.Contains("Пауза · 80%", StringComparison.Ordinal)
            && commands.Text.Contains("Громкость плеера 37% · 100%", StringComparison.Ordinal)
            && commands.Text.Contains("Громкость плеера 100% → 90%", StringComparison.Ordinal)
            && commands.Text.Contains("Громкость плеера уже 100%", StringComparison.Ordinal)
            && commands.Text.Contains("«Сторонняя фраза.»", StringComparison.Ordinal)
            && commands.Text.Contains("Команда не выбрана", StringComparison.Ordinal),
            "Temporary recognition window displays the exact transcript even for an unmatched command");
    }
}
