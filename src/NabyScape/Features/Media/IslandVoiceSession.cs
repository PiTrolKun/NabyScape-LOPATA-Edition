using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Lopata.Island;
using NAudio;
using NAudio.Wave;

namespace NabyScape;

public sealed partial class App
{
    private CancellationTokenSource? islandVoice;
    private bool islandVoiceReady;
    private IDisposable? islandVoiceDucking;
    internal bool IslandVoiceEnabled => islandVoiceReady && islandVoice is { IsCancellationRequested: false };
    internal bool IslandVoiceStarting => !islandVoiceReady && islandVoice is { IsCancellationRequested: false };
    internal bool IslandVoiceStopping => islandVoice is { IsCancellationRequested: true };
    internal event Action? IslandVoiceChanged;

    internal void ToggleIslandVoice()
    {
        if (islandVoice is { } active)
        {
            active.Cancel();
            SetIslandVoiceDucking(false);
            IslandVoiceChanged?.Invoke();
            return;
        }
        var session = new CancellationTokenSource();
        islandVoice = session;
        IslandVoiceChanged?.Invoke();
        _ = RunIslandVoiceAsync(session);
    }

    private async Task RunIslandVoiceAsync(CancellationTokenSource session)
    {
        using var lifetime = session;
        var token = session.Token;
        var journal = new IslandVoiceJournal(Store.DirectoryPath);
        try
        {
            var russianAtStartup = IslandVoicePreferences.IsRussian(Localization.LopataLanguage ?? Localization.Language);
            using var transcriptWindow = IslandVoiceTranscriptWindow.OpenForTest(russianAtStartup);
            using var microphone = IslandMicrophoneLease.TryAcquire();
            if (microphone is null)
            {
                NotifyVoice("Stop dictation in LOPATA before turning on the island microphone.");
                return;
            }
            var host = LopataEdition.HostExecutable() ?? throw new FileNotFoundException();
            await using var worker = new IslandVoiceWorkerClient(host, russianAtStartup);
            using var stop = token.Register(worker.Stop);
            var ready = await worker.ReadyAsync(token);
            if (ready.Type == "unavailable")
            {
                NotifyVoice("Open LOPATA to finish preparing voice control.");
                return;
            }
            if (ready.Type != "ready") throw new IOException("The Island voice worker could not initialize.");
            using var input = new IslandVoiceCapture();
            input.StartMicrophone();
            islandVoiceReady = true;
            IslandVoiceChanged?.Invoke();
            journal.Write(IslandVoiceEvent.MicrophoneReady);
            _ = WarmVoiceLibraryAsync(token);

            while (!token.IsCancellationRequested)
            {
                var settings = IslandVoicePreferences.Read();
                bool russian = IslandVoicePreferences.IsRussian(Localization.LopataLanguage ?? Localization.Language);
                string phrase = settings.Wake(russian);
                input.ListenForWake(phrase.Split(' ').Length);
                while (true)
                {
                    var transcript = await DecodeAsync(worker, await input.ReadAsync(token), token);
                    transcriptWindow?.ShowWake(transcript);
                    settings = IslandVoicePreferences.Read();
                    russian = IslandVoicePreferences.IsRussian(Localization.LopataLanguage ?? Localization.Language);
                    phrase = settings.Wake(russian);
                    if (IslandVoiceCommands.IsWake(transcript, phrase)) break;
                }

                input.IgnoreInput();
                token.ThrowIfCancellationRequested();
                journal.Write(IslandVoiceEvent.Activation);
                string playbackSource = Media.AudioMeterSource ?? "";
                try
                {
                    SetIslandVoiceDucking(true);
                    await PlayReadyReplyAsync(settings.Reply(russian), russian, token);
                    var followUp = await ReadVoiceRequestAsync(input, worker, journal, transcriptWindow, russian, IslandVoiceFollowUp.None, playbackSource, token);
                    if (followUp != IslandVoiceFollowUp.None)
                        await ReadVoiceRequestAsync(input, worker, journal, transcriptWindow, russian, followUp, playbackSource, token);
                }
                finally { SetIslandVoiceDucking(false); }
            }
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            var errorType = error is ChannelClosedException
                ? error.InnerException?.GetType().Name ?? error.GetType().Name : error.GetType().Name;
            journal.Write(IslandVoiceEvent.Failure, errorType: errorType);
            Store.Log(new IOException("Island voice: " + errorType));
            NotifyVoice(error is MmException or ChannelClosedException
                ? "Could not use the default microphone." : "Island voice control stopped. Try turning the microphone on again.");
        }
        finally
        {
            islandVoiceReady = false;
            islandVoice = null;
            offeredChoices = null;
            IslandVoiceChanged?.Invoke();
        }
    }

    private async Task<IslandVoiceFollowUp> ReadVoiceRequestAsync(IslandVoiceCapture input, IslandVoiceWorkerClient worker,
        IslandVoiceJournal journal, IslandVoiceTranscriptWindow? transcriptWindow, bool russian, IslandVoiceFollowUp followUp,
        string playbackSource, CancellationToken token)
    {
        input.ListenForCommand(noteText: followUp == IslandVoiceFollowUp.Note);
        journal.Write(IslandVoiceEvent.Listening, followUp == IslandVoiceFollowUp.Note ? IslandVoiceAction.AddNote : null);
        byte[] pcm;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            limit.CancelAfter(TimeSpan.FromSeconds(16));
            try { pcm = await input.ReadAsync(limit.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { pcm = []; }
        }
        input.IgnoreInput();
        if (pcm.Length == 0)
        {
            transcriptWindow?.ShowNoSpeech();
            journal.Write(IslandVoiceEvent.NoSpeech);
            return IslandVoiceFollowUp.None;
        }
        var text = await DecodeAsync(worker, pcm, token);
        var command = followUp == IslandVoiceFollowUp.Note
            ? IslandVoiceCommands.ParseNoteReply(text) : IslandVoiceCommands.Parse(text, russian);
        if (followUp == IslandVoiceFollowUp.Choice && command?.Action is not (IslandVoiceAction.ChooseResult or IslandVoiceAction.CancelChoice))
            command = null;
        int audioMs = pcm.Length / 32;
        string? answer;
        var next = IslandVoiceFollowUp.None;
        if (command is null)
        {
            transcriptWindow?.ShowCommand(text, command);
            journal.Write(string.IsNullOrWhiteSpace(text) ? IslandVoiceEvent.NoText : IslandVoiceEvent.Unrecognized, audioMs: audioMs);
            answer = Localization.Text(followUp switch
            {
                IslandVoiceFollowUp.Choice => "Say the number of an offered option.",
                IslandVoiceFollowUp.Note => "I did not catch the note text.",
                _ => "Voice command not recognized."
            });
        }
        else
        {
            journal.Write(IslandVoiceEvent.CommandRecognized, command.Action, audioMs, similarity: command.Similarity);
            bool volumeCommand = command.Action is IslandVoiceAction.Volume or IslandVoiceAction.Louder or IslandVoiceAction.Quieter;
            int? previousLevel = volumeCommand ? Settings.LocalMusicVolume : null;
            var outcome = await ExecuteIslandVoiceRequestAsync(command, token, playbackSource);
            answer = outcome.Answer;
            next = outcome.FollowUp;
            int? level = volumeCommand ? Settings.LocalMusicVolume : null;
            var result = outcome.Disposition switch
            {
                IslandVoiceDisposition.Clarification => IslandVoiceEvent.CommandClarification,
                IslandVoiceDisposition.Rejected => IslandVoiceEvent.CommandRejected,
                _ => volumeCommand && previousLevel == level ? IslandVoiceEvent.CommandUnchanged : IslandVoiceEvent.CommandApplied
            };
            journal.Write(result, command.Action, playerVolume: level, previousPlayerVolume: previousLevel,
                localTrackActive: volumeCommand ? Media.LocalTrack is not null : null, reason: outcome.DiagnosticReason);
            transcriptWindow?.ShowCommand(text, command, previousLevel, level);
        }
        if (answer is { Length: > 0 })
        {
            var spoken = await worker.SendAsync(new("speak", Text: answer[..Math.Min(answer.Length, 500)], Russian: russian), token);
            if (spoken.Type == "error") throw new IOException("The Island voice model stopped.");
            if (spoken.Type != "spoken")
            {
                if (next == IslandVoiceFollowUp.Note)
                {
                    journal.Write(IslandVoiceEvent.CommandRejected, IslandVoiceAction.StartNote, errorType: "NotePromptUnavailable");
                    NotifyVoice("Could not play the note prompt. Try again.");
                    return IslandVoiceFollowUp.None;
                }
                NotifyVoice(answer, alreadyLocalized: true);
            }
        }
        return next;
    }

    private static async Task<string?> DecodeAsync(IslandVoiceWorkerClient worker, byte[] bytes, CancellationToken token)
    {
        var response = await worker.SendAsync(new("decode", Pcm: Convert.ToBase64String(bytes)), token);
        return response.Type == "transcript" ? response.Text : throw new IOException("The voice worker could not decode audio.");
    }

    private static async Task PlayReadyReplyAsync(string? path, bool russian, CancellationToken token)
    {
        if (path is not null)
            try
            {
                using var file = File.OpenRead(path);
                await PlayWaveAsync(file, token);
                return;
            }
            catch (Exception error) when (error is IOException or MmException or UnauthorizedAccessException
                or ArgumentException or FormatException or NotSupportedException)
            {
                // A missing or invalid custom clip falls back to the bundled reply.
            }
        using var bundled = typeof(App).Assembly.GetManifestResourceStream(russian
            ? "NabyScape.IslandReady.ru.wav" : "NabyScape.IslandReady.en.wav") ?? throw new InvalidDataException();
        await PlayWaveAsync(bundled, token);
    }

    private static async Task PlayWaveAsync(Stream source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var audio = new WaveFileReader(source);
        if (audio.TotalTime.TotalSeconds is <= 0 or > 12) throw new InvalidDataException();
        using var player = new WaveOutEvent();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is { } error) completed.TrySetException(error);
            else completed.TrySetResult();
        };
        player.Init(audio);
        player.Play();
        try { await completed.Task.WaitAsync(token); }
        finally { player.Stop(); }
    }

    private void NotifyVoice(string answer, bool alreadyLocalized = false) =>
        Notify(Localization.Text("Voice commands"), alreadyLocalized ? answer : Localization.Text(answer));

    private void StopIslandVoice()
    {
        islandVoice?.Cancel();
        if (islandVoice is not null) SetIslandVoiceDucking(false);
    }

    internal void SetIslandVoiceDucking(bool enabled)
    {
        islandVoiceDucking?.Dispose();
        islandVoiceDucking = null;
        Media.SetVoiceDucking(enabled);
        if (enabled)
            islandVoiceDucking = Audio.DuckMediaForVoice(Media.VoiceExternalSource?.SourceAppUserModelId);
    }
}
