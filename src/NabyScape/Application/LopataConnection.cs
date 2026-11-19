using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Island;

namespace NabyScape;

internal sealed class LopataConnection(Func<string?> findExecutable) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private bool polling;
    private long commandVersion;
    internal IslandResponse State { get; private set; } = new("offline");
    internal event Action? Changed;
    internal bool CoreWorking { get; private set; }
    internal event Action? CoreFailed;

    internal async Task RefreshAsync()
    {
        if (polling || lifetime.IsCancellationRequested) return;
        polling = true;
        var version = commandVersion;
        try
        {
            var response = await RequestAsync(new("status"), lifetime.Token, seconds: 3);
            if (version == commandVersion) Publish(response);
        }
        finally { polling = false; }
    }

    internal async Task<IslandResponse> ControlAsync(string action, string id, CancellationToken token)
    {
        ++commandVersion;
        var response = await RequestAsync(new(action, id), token);
        ++commandVersion;
        Publish(response);
        return response;
    }

    internal Task<IslandResponse> ReadVoiceStateAsync(CancellationToken token) =>
        RequestAsync(new("status"), token, seconds: 3);

    internal Task<IslandResponse> TransferAsync(string id, string text, string? pauseId, CancellationToken token) =>
        RequestAsync(new("draft", id, text, pauseId), token);

    private async Task<IslandResponse> RequestAsync(IslandRequest request, CancellationToken token, int seconds = 48)
    {
        var path = findExecutable();
        var folder = path is null ? null : Path.GetDirectoryName(path);
        if (folder is null || !IslandLink.HostRunning(folder)) return new("offline");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        limit.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { return await IslandLink.AskHostAsync(folder, request, limit.Token); }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException or JsonException)
        { return new(token.IsCancellationRequested ? "cancelled" : "unavailable"); }
    }

    internal async Task<IslandResponse> CoreAsync(string text, IslandTurn[] history, CancellationToken token)
    {
        var executable = findExecutable();
        if (executable is null) return new("host_missing");
        var folder = Path.GetDirectoryName(executable)!;
        if (IslandLink.HostRunning(folder)) return new("host_opened");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        limit.CancelAfter(TimeSpan.FromMinutes(8));
        var channel = IslandLink.CorePrefix + Guid.NewGuid().ToString("N");
        try
        {
            await using var pipe = IslandLink.ListenCore(channel);
            var start = new ProcessStartInfo(executable) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--island-core");
            start.ArgumentList.Add(channel);
            using var worker = Process.Start(start) ?? throw new IOException("The core worker did not start.");
            CoreWorking = true;
            Changed?.Invoke();
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(limit.Token);
            connect.CancelAfter(TimeSpan.FromSeconds(12));
            await pipe.WaitForConnectionAsync(connect.Token);
            await IslandLink.WriteAsync(pipe, new IslandRequest("core", Text: text, History: history), limit.Token);
            var reply = await IslandLink.ReadAsync<IslandResponse>(pipe, limit.Token);
            if (reply.Result == "core_failed" && !token.IsCancellationRequested) CoreFailed?.Invoke();
            return reply;
        }
        catch (OperationCanceledException)
        {
            if (token.IsCancellationRequested || lifetime.IsCancellationRequested) return new("cancelled");
            CoreFailed?.Invoke();
            return new("core_failed");
        }
        catch (Exception error) when (error is IOException or Win32Exception or JsonException)
        {
            if (!lifetime.IsCancellationRequested && !token.IsCancellationRequested) CoreFailed?.Invoke();
            return new("core_failed");
        }
        finally
        {
            if (CoreWorking)
            {
                CoreWorking = false;
                Changed?.Invoke();
            }
        }
    }

    private void Publish(IslandResponse response)
    {
        if (lifetime.IsCancellationRequested) return;
        var jobs = response.Jobs ?? [];
        string result = response.Jobs is not null ? "ok" : response.Result;
        if (State.Result == result && State.Busy == response.Busy && State.ModelActive == response.ModelActive
            && State.ModelFailure == response.ModelFailure && (State.Jobs ?? []).SequenceEqual(jobs)) return;
        State = response with { Result = result, Jobs = jobs };
        Changed?.Invoke();
    }

    public void Dispose() => lifetime.Cancel();
}

public partial class App
{
    internal LopataConnection LopataLink { get; } = new(LopataEdition.HostExecutable);
    private LopataChatWindow? lopataChat;

    internal void ShowLopataChat()
    {
        if (lopataChat is null)
        {
            lopataChat = new(LopataLink);
            lopataChat.Closed += (_, _) => lopataChat = null;
        }
        lopataChat.Show();
        lopataChat.Activate();
        lopataChat.FocusDraft();
    }
}
