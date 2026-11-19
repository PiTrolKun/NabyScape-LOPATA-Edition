using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace NabyScape;

public sealed partial class AudioService
{
    internal IDisposable? DuckMediaForVoice(string? source)
    {
        if (disposed || string.IsNullOrWhiteSpace(source)) return null;
        var endpoints = DiscoverMediaSessions(source).ToArray();
        var volumes = new List<VoiceSessionVolume>();
        foreach (var endpoint in endpoints)
        foreach (var session in endpoint.Sessions)
        {
            try
            {
                if (session.State != AudioSessionState.AudioSessionStateActive) continue;
                var level = session.SimpleAudioVolume;
                float original = level.Volume;
                if (!level.Mute && original > 0)
                    volumes.Add(new(level, original, original * .1f));
            }
            catch (COMException) { /* The application may release an audio session during discovery. */ }
        }

        foreach (var volume in volumes)
        {
            try { volume.Control.Volume = volume.Reduced; }
            catch (COMException) { }
        }
        return new VoiceVolumeLease(endpoints, volumes);
    }

    private sealed record VoiceSessionVolume(SimpleAudioVolume Control, float Original, float Reduced);

    private sealed class VoiceVolumeLease(MediaEndpoint[] endpoints, List<VoiceSessionVolume> volumes) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (released) return;
            released = true;
            try
            {
                foreach (var volume in volumes)
                {
                    try
                    {
                        // Preserve adjustments the user made in the Windows mixer during the dialogue.
                        if (Math.Abs(volume.Control.Volume - volume.Reduced) < .0001f)
                            volume.Control.Volume = volume.Original;
                    }
                    catch (COMException) { /* The output or application can close while the microphone is active. */ }
                }
            }
            finally
            {
                foreach (var endpoint in endpoints) endpoint.Dispose();
            }
        }
    }
}
