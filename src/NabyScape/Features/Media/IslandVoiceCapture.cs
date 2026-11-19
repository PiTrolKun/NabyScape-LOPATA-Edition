using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.Wave;

namespace NabyScape;

internal sealed class IslandVoiceCapture : IDisposable
{
    private const int BytesPerSecond = 32000;
    private const int WakeCadenceBytes = BytesPerSecond * 3 / 4;
    private const int CommandPauseBytes = BytesPerSecond * 3 / 4;
    private const int NotePauseBytes = BytesPerSecond * 3 / 2;
    private const int CommandLimitBytes = BytesPerSecond * 10;
    private const int CommandStartBytes = BytesPerSecond * 5;
    private const int CommandOnsetBytes = BytesPerSecond / 10;

    private enum CaptureMode { Idle, Wake, Command }
    private WaveInEvent? input;
    private readonly Channel<byte[]> completed = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly MemoryStream buffer = new();
    private readonly object gate = new();
    private CaptureMode mode;
    private int wakeWindowBytes;
    private int endSilenceBytes;
    private int waiting;
    private int silence;
    private int windowProgress;
    private int onset;
    private bool heardSpeech;

    internal void StartMicrophone()
    {
        var microphone = new WaveInEvent
        {
            DeviceNumber = -1, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50
        };
        microphone.DataAvailable += (_, e) => Push(e.Buffer.AsSpan(0, e.BytesRecorded));
        microphone.RecordingStopped += (_, e) => completed.Writer.TryComplete(e.Exception);
        try { microphone.StartRecording(); }
        catch { microphone.Dispose(); throw; }
        input = microphone;
    }

    internal void ListenForWake(int wakeWords) => Reset(CaptureMode.Wake, Math.Clamp(wakeWords + 1, 3, 12) * BytesPerSecond);
    internal void ListenForCommand(bool noteText = false) =>
        Reset(CaptureMode.Command, endSilence: noteText ? NotePauseBytes : CommandPauseBytes);
    internal void IgnoreInput() => Reset(CaptureMode.Idle);

    private void Reset(CaptureMode next, int wakeWindow = 0, int endSilence = CommandPauseBytes)
    {
        lock (gate)
        {
            mode = next;
            wakeWindowBytes = wakeWindow;
            endSilenceBytes = endSilence;
            waiting = silence = windowProgress = onset = 0;
            heardSpeech = false;
            buffer.SetLength(0);
            completed.Reader.TryRead(out _);
        }
    }

    internal async Task<byte[]> ReadAsync(CancellationToken token) => await completed.Reader.ReadAsync(token);

    internal void Push(ReadOnlySpan<byte> source)
    {
        lock (gate)
        {
            if (mode != CaptureMode.Idle) AcceptAudio(source);
        }
    }

    private void AcceptAudio(ReadOnlySpan<byte> source)
    {
        double sum = 0;
        for (int i = 0; i + 1 < source.Length; i += 2)
        {
            double sample = BitConverter.ToInt16(source[i..]) / 32768d;
            sum += sample * sample;
        }
        bool loud = Math.Sqrt(sum / Math.Max(1, source.Length / 2)) >= (mode == CaptureMode.Command ? .008 : .012);
        silence = loud ? 0 : Math.Min(endSilenceBytes, silence + source.Length);
        if (mode == CaptureMode.Wake)
        {
            Append(source, wakeWindowBytes);
            windowProgress += source.Length;
            heardSpeech |= loud;
            if (heardSpeech && windowProgress >= WakeCadenceBytes && buffer.Length >= BytesPerSecond * .7)
            {
                completed.Writer.TryWrite(buffer.ToArray());
                windowProgress = 0;
                heardSpeech = false;
            }
            // Start the next wake phrase without including unrelated earlier speech.
            if (silence >= CommandPauseBytes && buffer.Length > WakeCadenceBytes)
            {
                if (heardSpeech) completed.Writer.TryWrite(buffer.ToArray());
                buffer.SetLength(0);
                heardSpeech = false;
                windowProgress = 0;
            }
            return;
        }

        if (!heardSpeech)
        {
            waiting += source.Length;
            onset = loud ? onset + source.Length : 0;
            heardSpeech = onset >= CommandOnsetBytes;
            if (!heardSpeech)
            {
                Append(source, BytesPerSecond / 5);
                if (waiting >= CommandStartBytes) Finish([]);
                return;
            }
        }
        buffer.Write(source);
        if (silence >= endSilenceBytes || buffer.Length >= CommandLimitBytes)
            Finish(buffer.ToArray());
    }

    private void Append(ReadOnlySpan<byte> source, int limit)
    {
        var size = (int)buffer.Length;
        int remove = size + source.Length - limit;
        if (remove > 0)
        {
            var bytes = buffer.GetBuffer().AsSpan(0, size);
            bytes[remove..].CopyTo(bytes);
            buffer.SetLength(size - remove);
        }
        buffer.Position = buffer.Length;
        buffer.Write(source);
    }

    private void Finish(byte[] pcm)
    {
        mode = CaptureMode.Idle;
        completed.Writer.TryWrite(pcm);
    }

    public void Dispose()
    {
        lock (gate)
        {
            mode = CaptureMode.Idle;
            buffer.Dispose();
        }
        input?.Dispose();
        completed.Writer.TryComplete();
    }
}
