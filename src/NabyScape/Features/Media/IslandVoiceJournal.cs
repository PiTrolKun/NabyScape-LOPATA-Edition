using System;
using System.IO;
using System.Text.Json;
using Lopata.Island;

namespace NabyScape;

internal enum IslandVoiceEvent
{
    MicrophoneReady, Activation, Listening, NoSpeech, NoText, Unrecognized,
    CommandRecognized, CommandApplied, CommandUnchanged, CommandRejected, CommandClarification, Failure
}

internal sealed class IslandVoiceJournal(string profileDirectory)
{
    internal const string FileName = "island-voice.jsonl";
    private readonly string path = Path.Combine(profileDirectory, FileName);

    internal void Write(IslandVoiceEvent stage, IslandVoiceAction? action = null, int? audioMs = null,
        int? playerVolume = null, string? errorType = null, int? similarity = null,
        int? previousPlayerVolume = null, bool? localTrackActive = null, string? reason = null)
    {
        // Log application states only. Never record PCM, phrases, transcripts, or track metadata.
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length >= 64 * 1024)
                File.Move(path, path + ".previous", overwrite: true);
            File.AppendAllText(path, JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.Now, stage = stage.ToString(), action = action?.ToString(),
                audioMs, playerVolume, errorType, similarity, previousPlayerVolume, localTrackActive, reason
            }) + "\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
