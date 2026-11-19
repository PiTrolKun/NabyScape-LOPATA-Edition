using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Island;
using NAudio.Wave;

namespace NabyScape;

internal static class IslandVoiceAdvancedDiagnostics
{
    internal static async Task RunAsync(App app, OverlayWindow island, string folder, CancellationToken token, Action<bool, string> check)
    {
        var first = AddTrack(folder, "Du Hast Studio");
        var live = AddTrack(folder, "Du Hast Live");
        var other = AddTrack(folder, "Forest Ballad");
        File.WriteAllText(Path.ChangeExtension(other.Path, ".lrc"), "[00:01.00] Мужик в лес пошёл, а вечер был тихий.");
        var catalog = app.Media.MusicCatalog;
        catalog.SaveCorrection(first, new("Du Hast", "Rammstein", "Mutter", "Studio", "Industrial metal"));
        catalog.SaveCorrection(live, new("Du Hast", "Rammstein", "Live in Berlin", "Live", "Industrial metal"));
        catalog.SaveCorrection(other, new("Forest Ballad", "Король и Шут", "", "", "Folk", "спокойное, женский вокал"));
        app.Settings.LocalMusicFolder = folder;
        app.Settings.PlaybackOrder = LocalPlaybackOrder.Sequential;
        app.ChangeSettings();
        var player = (ILocalMusicTransport)app.Media;

        Task<IslandVoiceOutcome> Voice(string phrase, bool russian = true) => app.ExecuteIslandVoiceRequestAsync(
            IslandVoiceCommands.Parse(phrase, russian) ?? throw new InvalidOperationException("Did not parse: " + phrase), token);
        bool Active(LocalMusicTrack track) => player.CurrentMusic.Track == track && player.CurrentMusic.Playing;
        bool Accepted(IslandVoiceOutcome result) => result.Disposition == IslandVoiceDisposition.Applied;
        bool Rejected(IslandVoiceOutcome result) => result.Disposition == IslandVoiceDisposition.Rejected;

        var before = player.CurrentMusic.Track;
        var choices = await Voice("Включи Дю Хаст");
        check(choices.Disposition == IslandVoiceDisposition.Clarification && player.CurrentMusic.Track == before
            && choices.Answer!.Contains("Studio") && choices.Answer.Contains("Live"), "Ambiguous foreign title read in Russian offers both versions without playing");
        check(Accepted(await Voice("Вторую")) && (Active(first) || Active(live)), "Spoken choice starts one of the previously offered files");
        check(Rejected(await Voice("Первую")), "The same choice cannot be executed twice");
        check(Accepted(await Voice("Поставь альбом Муттер")) && Active(first), "Album search starts the matching album");
        check(Accepted(await Voice("Включи Рамштайн")) && (Active(first) || Active(live)) && app.Media.CanNext,
            "A Russian transcription of a foreign artist starts both tracks in one queue");
        check(Accepted(await Voice("Включи песню КиШа, где мужик в лес пошёл")) && Active(other), "Local lyrics and artist abbreviation support descriptive search");
        check(Accepted(await Voice("Поставь что-нибудь спокойное с женским вокалом")) && Active(other), "Description search can use explicit mood and vocal tags");
        check(Accepted(await Voice("Play album Mutter", false)) && Active(first), "English command uses the same local catalog");
        check(Accepted(await Voice("Включи похожее по звучанию")) && Active(live), "Similar search uses stored genre and description");
        check(Rejected(await Voice("Включи абсолютно несуществующую запись зета")), "No-result search leaves the current source alone");

        var cacheFile = Path.Combine(app.Store.DirectoryPath, "music-cards.cache.json");
        var priorStamp = File.GetLastWriteTimeUtc(cacheFile);
        var inputs = await player.TracksAsync(LocalMusicSource.Folder, folder, token);
        var reloaded = new LocalMusicCatalog(app.Store.DirectoryPath, _ => { });
        var cards = await Task.Run(() => reloaded.RefreshAsync(inputs, token), token);
        check(cards.Any(item => item.Track == first && item.Album == "Mutter") && File.GetLastWriteTimeUtc(cacheFile) == priorStamp,
            "Restarted index reuses cached tracks and persisted corrections without rewriting the index");
        File.WriteAllText(Path.ChangeExtension(other.Path, ".lrc"), "[00:01.00] При свете луны дрожал серебряный маяк.");
        check(Accepted(await Voice("Включи песню, где дрожал серебряный маяк")) && Active(other), "Edited sidecar is re-indexed while the player is active");
        var newTrack = AddTrack(folder, "Starlight Beacon");
        check(Accepted(await Voice("Включи Starlight Beacon")) && Active(newTrack), "A newly added file is available without restarting the island");
        string renamed = Path.Combine(folder, "Midnight Beacon.wav");
        // WPF can keep the file open during playback; switch away before simulating a rename.
        await Voice("Включи Forest Ballad");
        File.Move(newTrack.Path, renamed);
        check(Accepted(await Voice("Включи Midnight Beacon")) && player.CurrentMusic.Track?.Path == renamed,
            "Renamed file replaces the old path in the refreshed index");
        await Voice("Включи Forest Ballad");
        File.Delete(renamed);
        check(Rejected(await Voice("Включи Midnight Beacon")) && Active(other), "Removed file is no longer a playable match");

        check(Rejected(await Voice("Включи что играло сегодня")), "Listening history is off by default");
        app.Settings.MusicListeningHistory = true;
        await Voice("Включи Forest Ballad");
        await Voice("Поставь альбом Mutter");
        check(Accepted(await Voice("Включи то, что играло сегодня перед Rammstein")) && Active(other),
            "Playback history finds the entry immediately before the requested artist");
        check(app.ClearMusicListeningHistory() && Rejected(await Voice("Включи что играло сегодня")), "Clearing listening history removes results");
        app.Settings.MusicListeningHistory = false;

        check(Accepted(await Voice("Поставь таймер на семь минут")) && app.Session.Timer.Running
            && app.Session.Timer.Remaining(DateTimeOffset.UtcNow) is > 412 and <= 420, "Voice starts the actual island countdown");
        check(Accepted(await Voice("Сколько осталось?")), "Voice reads the active countdown");
        string previousNote = app.Productivity.Note;
        var prompt = await Voice("Запиши в заметку");
        check(prompt.Disposition == IslandVoiceDisposition.Clarification && prompt.FollowUp == IslandVoiceFollowUp.Note
            && prompt.Answer == Localization.Text("What should I write?") && app.Productivity.Note == previousNote,
            "Voice asks for separate note text before making any change");
        var cancelNote = IslandVoiceCommands.ParseNoteReply("Отмена.")!;
        check(Accepted(await app.ExecuteIslandVoiceRequestAsync(cancelNote, token)) && app.Productivity.Note == previousNote,
            "Cancelling the follow-up preserves the existing note");
        await Voice("Запиши в заметку");
        var dictated = IslandVoiceCommands.ParseNoteReply("Не забыть обложку!")!;
        check(Accepted(await app.ExecuteIslandVoiceRequestAsync(dictated, token)) && app.Productivity.Note.EndsWith("Не забыть обложку!")
            && app.Store.Load<ProductivityData>("productivity").Note == app.Productivity.Note, "Voice appends and persists the note without changing case or punctuation");
        check(Accepted(await Voice("Покажи заметки")) && island.ActivePage == "notes", "Voice opens the native Notes panel");
        check(Accepted(await Voice("Покажи погоду")) && island.ActivePage == "weather", "Voice opens the native Weather panel");
        check(Accepted(await Voice("Покажи состояние ПК")) && island.ActivePage == "status", "Voice opens the native System panel");
        check(Accepted(await Voice("Сколько памяти занято?")), "Voice reads native available and total memory");
        check(Rejected(await Voice("Будет ли дождь?")), "Voice reports when no forecast is saved");
        var moment = DateTimeOffset.UtcNow;
        var cachedWeather = new WeatherSnapshot
        {
            Updated = moment.AddMinutes(-25), Hours =
            [
                new() { Stamp = moment.UtcDateTime.AddHours(-2).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), RainChance = 95 },
                new() { Stamp = moment.UtcDateTime.AddHours(1).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), RainChance = 35 },
                new() { Stamp = moment.UtcDateTime.AddHours(2).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), RainChance = 60 }
            ]
        };
        string forecast = App.DescribeRainForecast(cachedWeather, moment);
        check(forecast.Contains("60", StringComparison.Ordinal) && !forecast.Contains("95", StringComparison.Ordinal)
            && forecast.Contains(cachedWeather.Updated.ToLocalTime().ToString("g", Localization.Culture), StringComparison.Ordinal),
            "Voice forecast uses upcoming saved hours and includes the update timestamp");
        check(Accepted(await Voice("Открой список треков")) && island.ActivePage == "media", "Voice opens the normal track chooser from the music panel");
        check(Accepted(await Voice("Открой список задач")), "Voice opens the LOPATA task panel");
        check((await Voice("Следующее видео")).DiagnosticReason == "NoExternalSource",
            "External Next leaves the local player alone when no external source is displayed");
        var switched = await app.ExecuteIslandVoiceRequestAsync(IslandVoiceCommands.Parse("Пауза", true)!, token, "previous-source");
        check(switched.DiagnosticReason == "PlaybackSourceChanged" && player.CurrentMusic.Playing,
            "A transport command leaves playback alone when the source changed after the wake phrase");

        string? executable = LopataEdition.HostExecutable();
        if (executable is not null && Path.GetPathRoot(executable) == Path.GetPathRoot(folder)
            && Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))) == "bridge-fixture")
            await CheckWorkAsync(executable, Voice, token, check);

        app.MusicLists.ToggleFavorite(other);
        check(Accepted(await Voice("Включи избранное и выключи музыку через полчаса")) && player.CurrentMusic.Playing,
            "A linked command starts a real favorites queue and schedules its stop");
        app.CheckVoiceMusicStop(Stopwatch.GetTimestamp() + 1801L * Stopwatch.Frequency);
        check(!player.CurrentMusic.Playing, "The scheduled time pauses the same playback queue");
        await Voice("Продолжи");
        check(Accepted(await Voice("Выключи музыку через минуту")) && Accepted(await Voice("Отмени остановку музыки")),
            "A pending music stop can be cancelled");
        app.CheckVoiceMusicStop(Stopwatch.GetTimestamp() + 61L * Stopwatch.Frequency);
        check(player.CurrentMusic.Playing, "Cancelled stop does not pause the player");
        await Voice("Выключи музыку через минуту");
        await Voice("Включи Forest Ballad");
        app.CheckVoiceMusicStop(Stopwatch.GetTimestamp() + 61L * Stopwatch.Frequency);
        check(Active(other), "Replacing the queue prevents an old stop from pausing the new playback");

        if (Environment.GetEnvironmentVariable("LOPATA_ISLAND_EXTERNAL_MEDIA_TEST") == "1")
            await IslandVoiceExternalDiagnostics.RunAsync(app, token, check);
    }

    private static async Task CheckWorkAsync(string executable, Func<string, bool, Task<IslandVoiceOutcome>> voice,
        CancellationToken token, Action<bool, string> check)
    {
        string parent = Path.GetDirectoryName(executable)!;
        using var marker = new Mutex(false, @"Local\" + IslandLink.InstallationName(parent), out bool created);
        if (!created) throw new InvalidOperationException("Fixture host is already running");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var jobs = new List<IslandJob>
        {
            new("image", "Апскейл фото", "Running", true, false, .4),
            new("video", "Апскейл видео", "Running", true, false, .7)
        };
        var bridge = ServeAsync();
        try
        {
            var status = await voice("Как идёт генерация?", true);
            check(status.Disposition == IslandVoiceDisposition.Applied && status.Answer!.Contains("40")
                && status.Answer.Contains("70"), "Voice reads confirmed task progress over the normal host pipe");
            var prompt = await voice("Поставь апскейл на паузу", true);
            check(prompt.Disposition == IslandVoiceDisposition.Clarification && jobs.All(job => job.CanPause),
                "Several eligible tasks require a choice before pausing");
            check((await voice("Вторую", true)).Disposition == IslandVoiceDisposition.Applied && jobs[1].Phase == "Paused",
                "Only the selected job is paused, after the host confirms it");
            check((await voice("Продолжи обработку", true)).Disposition == IslandVoiceDisposition.Applied && jobs[1].Phase == "Running",
                "Voice resumes the one paused job after host confirmation");
        }
        finally
        {
            cancel.Cancel();
            await bridge;
        }

        async Task ServeAsync()
        {
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    await using var pipe = IslandLink.ListenHost(parent);
                    await pipe.WaitForConnectionAsync(cancel.Token).ConfigureAwait(false);
                    var request = await IslandLink.ReadAsync<IslandRequest>(pipe, cancel.Token).ConfigureAwait(false);
                    string result = "ok";
                    if (request.Action != "status")
                    {
                        int index = jobs.FindIndex(job => job.Id == request.Id);
                        if (index < 0) result = "changed";
                        else if (request.Action == "pause" && jobs[index].CanPause)
                            jobs[index] = jobs[index] with { Phase = "Paused", CanPause = false, CanResume = true };
                        else if (request.Action == "resume" && jobs[index].CanResume)
                            jobs[index] = jobs[index] with { Phase = "Running", CanPause = true, CanResume = false };
                        else result = "changed";
                    }
                    await IslandLink.WriteAsync(pipe, new IslandResponse(result, jobs.ToArray()), cancel.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }
    }

    private static LocalMusicTrack AddTrack(string folder, string name)
    {
        var track = new LocalMusicTrack(Path.Combine(folder, name + ".wav"), LocalMusicSource.Folder);
        using var writer = new WaveFileWriter(track.Path, new WaveFormat(16000, 16, 1));
        writer.Write(new byte[28 * 32000]);
        return track;
    }
}
