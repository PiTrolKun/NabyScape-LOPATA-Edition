using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using Lopata.Island;
using Lopata.MailHub;

namespace NabyScape;

public sealed partial class App
{
    private Mutex? mailHubPresence;
    private DispatcherTimer? mailHubTimer;
    private Process? mailHubWorker;
    private DateTimeOffset mailHubStarted, mailHubLastNotice;
    private DateTimeOffset retryWorkerAfter;
    internal MailHubFiles? MailHub { get; private set; }
    internal MailHubState MailHubState { get; private set; } = new();
    internal event Action? MailHubChanged;

    private void StartIslandMailHub()
    {
        MailHub = new MailHubFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI_HUB"));
        mailHubStarted = DateTimeOffset.Now;
        mailHubPresence = MailHub.ProcessMarker(island: true);
        mailHubTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        mailHubTimer.Tick += (_, _) => RefreshMailHub();
        mailHubTimer.Start();
    }

    internal void RefreshMailHub()
    {
        if (MailHub is not { } files) return;
        try
        {
            MailHubState = files.Load();
            var executable = LopataEdition.HostExecutable();
            bool hostRunning = executable is not null && IslandLink.HostRunning(Path.GetDirectoryName(executable)!);
            bool launch = executable is not null && files.HasAccounts() && files.BindToIsland() && !hostRunning;
            if (launch && (mailHubWorker is null || mailHubWorker.HasExited) && DateTimeOffset.Now >= retryWorkerAfter)
            {
                mailHubWorker?.Dispose();
                mailHubWorker = null;
                retryWorkerAfter = DateTimeOffset.Now.AddSeconds(15);
                using var owner = Process.GetCurrentProcess();
                var start = new ProcessStartInfo(executable!) { UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(executable!)! };
                start.ArgumentList.Add("--mail-hub-island");
                start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                start.ArgumentList.Add(owner.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
                mailHubWorker = Process.Start(start);
            }
            if (MailHubState.StatusAt is { } at && at > mailHubStarted && at > mailHubLastNotice)
            {
                mailHubLastNotice = at;
                bool notify = MailHubState.Status is "MailHub.EmptyNotification" or "MailHub.DeferredNotice" or "MailHub.AllUnavailable"
                    || MailHubState.Status == "MailHub.Partial" && MailHubState.OutcomeReport is null;
                if (notify && !hostRunning)
                {
                    string info = Localization.Mail(MailHubState.Status);
                    if (MailHubState.StatusDetail is { } detail) info += " " + Localization.Mail(detail);
                    if (MailHubState.UnavailableAccounts.Length > 0) info += " " + string.Join(", ", MailHubState.UnavailableAccounts);
                    Notify(Localization.Mail("MailHub.Title"), info);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { }
        MailHubChanged?.Invoke();
    }

    private void StopIslandMailHub()
    {
        mailHubTimer?.Stop();
        mailHubPresence?.Dispose();
        mailHubWorker?.Dispose();
    }
}
