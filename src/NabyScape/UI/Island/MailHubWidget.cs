using System;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Lopata.MailHub;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private MailHubPlayback? mailPlayback;
    private TextBlock? closedMailHubBadge;

    private void InitializeClosedMailHubBadge(Grid header)
    {
        if (app.MailHub is not { } files) return;
        var badge = closedMailHubBadge = new TextBlock
        {
            Text = "✉", FontSize = 14, Foreground = UI.Brush("#A1E8C4"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(60, 0, 0, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(badge, "Lopata.MailHub.Ready");
        AutomationProperties.SetName(badge, Localization.Mail("MailHub.Ready"));
        Grid.SetColumnSpan(badge, header.ColumnDefinitions.Count);
        header.Children.Add(badge);
        void Update() => badge.Visibility = files.IsReady ? Visibility.Visible : Visibility.Collapsed;
        app.MailHubChanged += Update;
        Closed += (_, _) => { app.MailHubChanged -= Update; mailPlayback?.Dispose(); };
        Update();
    }

    private FrameworkElement BuildMailHubWidget()
    {
        var panel = new StackPanel { Margin = new Thickness(2, 0, 2, 4) };
        var header = new Grid();
        header.ColumnDefinitions.Add(new() { Width = new GridLength(3, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
        var title = UI.Heading(Localization.Mail("MailHub.Title"), 17);
        title.TextWrapping = TextWrapping.Wrap;
        header.Children.Add(title);
        var countdown = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = UI.Muted,
            Text = Localization.Mail("MailHub.NoSchedule") };
        AutomationProperties.SetAutomationId(countdown, "Lopata.MailHub.Countdown");
        Grid.SetColumn(countdown, 1);
        header.Children.Add(countdown);
        panel.Children.Add(header);
        var description = new TextBlock { Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap, Foreground = UI.Muted };
        panel.Children.Add(description);
        if (app.MailHub is not { } files)
        {
            description.Text = Localization.Mail("MailHub.SetupInHost");
            return panel;
        }
        var player = mailPlayback ??= new MailHubPlayback(files, this, Localization.Mail,
            key => app.Notify(Localization.Mail("MailHub.Title"), Localization.Mail(key)));
        var actions = new WrapPanel();
        var playback = UI.Button(Localization.Mail("MailHub.Play"), () =>
        { if (files.SafeLatest(player.PlayingId) is { } report) player.Toggle(report); });
        var stop = UI.Button(Localization.Mail("MailHub.Stop"), player.Stop);
        var delete = UI.Button(Localization.Mail("MailHub.Delete"), () =>
        { if (files.SafeLatest(player.PlayingId) is { } report) { player.Delete(report); app.RefreshMailHub(); } });
        var request = UI.Button(Localization.Mail("MailHub.CheckNow"), () =>
        {
            try { files.TryRequest(files.BindToIsland(), DateTimeOffset.Now); app.RefreshMailHub(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { app.Notify(Localization.Mail("MailHub.Title"), Localization.Mail("MailHub.FileFailed")); }
        });
        AutomationProperties.SetAutomationId(playback, "Lopata.MailHub.Play");
        AutomationProperties.SetAutomationId(delete, "Lopata.MailHub.Delete");
        AutomationProperties.SetAutomationId(request, "Lopata.MailHub.CheckNow");
        foreach (var button in new[] { playback, stop, delete, request })
        {
            button.Margin = new Thickness(0, 0, 5, 5);
            actions.Children.Add(button);
        }
        panel.Children.Add(actions);

        void Update()
        {
            var report = files.SafeLatest(player.PlayingId);
            var state = app.MailHubState;
            var now = DateTimeOffset.Now;
            var next = files.NextCheck(state, now, TimeZoneInfo.Local);
            string timing = Localization.Mail(state.Paused ? "MailHub.Paused" : "MailHub.NoSchedule");
            if (next.At is { } at)
            {
                var remaining = TimeSpan.FromSeconds(Math.Max(0, Math.Ceiling((at - now).TotalSeconds)));
                string clock = remaining.ToString(@"hh\:mm\:ss");
                if (remaining.Days > 0)
                    clock = string.Format(Localization.Culture, Localization.Mail("MailHub.CountdownDays"), remaining.Days, clock);
                timing = string.Format(Localization.Culture, Localization.Mail(next.Retry ? "MailHub.RetryCountdown" : "MailHub.NextCountdown"), clock);
            }
            countdown.Text = timing;
            playback.IsEnabled = delete.IsEnabled = report is not null;
            stop.IsEnabled = player.PlayingId is not null;
            playback.Content = Localization.Mail(player.PlayingId is null ? "MailHub.Play" : player.Paused ? "MailHub.Resume" : "MailHub.Pause");
            if (report is not null)
            {
                description.Text = string.Format(Localization.Culture, Localization.Mail("MailHub.Recording"), report.Created.LocalDateTime.ToString("g", Localization.Culture));
                if (state.OutcomeReport == report.Id && state.UnavailableAccounts.Length > 0)
                    description.Text += "\n" + Localization.Mail("MailHub.Partial") + " " + string.Join(", ", state.UnavailableAccounts);
            }
            else description.Text = state.Status.Length > 0 ? Localization.Mail(state.Status) : Localization.Mail("MailHub.NoRecording");
            request.IsEnabled = state.Pending is null && state.ManualRequest is null && files.HasAccounts() && files.IsProcessOpen(files.BindToIsland());
        }
        panel.Loaded += (_, _) => { player.Changed += Update; app.MailHubChanged += Update; Update(); };
        panel.Unloaded += (_, _) => { player.Changed -= Update; app.MailHubChanged -= Update; };
        Update();
        return panel;
    }
}
