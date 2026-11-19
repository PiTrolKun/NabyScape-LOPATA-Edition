using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Island;

namespace NabyScape;

internal sealed class IslandVoiceWorkerClient : IAsyncDisposable
{
    private readonly Process process;

    internal IslandVoiceWorkerClient(string host, bool russian)
    {
        var info = new ProcessStartInfo(host, "--island-music-voice")
        {
            WorkingDirectory = Path.GetDirectoryName(host)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        info.Environment["LOPATA_ISLAND_VOICE_LANGUAGE"] = russian ? "ru" : "en";
        process = Process.Start(info) ?? throw new IOException("Could not start the Island voice worker.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
    }

    internal Task<IslandVoiceResponse> ReadyAsync(CancellationToken token) =>
        ReadAsync(token).WaitAsync(TimeSpan.FromMinutes(2), token);

    internal async Task<IslandVoiceResponse> SendAsync(IslandVoiceRequest request, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(request.Operation == "speak" ? 60 : 10));
        var payload = JsonSerializer.Serialize(request);
        await process.StandardInput.WriteLineAsync(payload.AsMemory(), deadline.Token);
        await process.StandardInput.FlushAsync(deadline.Token);
        return await ReadAsync(deadline.Token);
    }

    private async Task<IslandVoiceResponse> ReadAsync(CancellationToken token)
    {
        string? json = await process.StandardOutput.ReadLineAsync(token);
        return json is not null && JsonSerializer.Deserialize<IslandVoiceResponse>(json) is { } response
            ? response : throw new IOException("The Island voice worker returned no response.");
    }

    internal void Stop()
    {
        process.StandardInput.BaseStream.Close();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Stop();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { process.Dispose(); }
    }
}
