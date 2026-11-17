using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Lopata.Island;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly TextBlock lopataActivity = UI.Text("", 11);
    private LopataWorkPanel? lopataWork;
    private bool showLopataJobs;

    private void InitializeLopataWork(Grid header)
    {
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(lopataActivity, header.ColumnDefinitions.Count - 1);
        lopataActivity.Margin = new(6, 0, 0, 0);
        lopataActivity.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        UI.LocalizedName(lopataActivity, "LOPATA model activity");
        header.Children.Add(lopataActivity);
        app.LopataLink.Changed += RefreshLopataWork;
        Closed += (_, _) => app.LopataLink.Changed -= RefreshLopataWork;
        RefreshLopataWork();
    }

    private void RefreshLopataWork()
    {
        var state = app.LopataLink.State;
        var jobs = state.Jobs ?? [];
        var active = jobs.Count(job => job.Phase is "Running" or "Pausing");
        lopataActivity.Text = active > 1 ? $"● {active}" : "●";
        lopataActivity.Visibility = state.Busy || active > 0 ? Visibility.Visible : Visibility.Collapsed;
        lopataActivity.ToolTip = Localization.Text("LOPATA model activity");
        lopataWork?.Refresh(state);
    }

    private void AddLopataWork(Panel panel)
    {
        compactPageHost.Visibility = showLopataJobs ? Visibility.Collapsed : Visibility.Visible;
        lopataWork = new LopataWorkPanel(app, showLopataJobs, open =>
        {
            showLopataJobs = open;
            compactPageHost.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            if (expanded) ResizeToolPanel();
        });
        panel.Children.Add(lopataWork);
        RefreshLopataWork();
    }
}

internal sealed class LopataWorkPanel : StackPanel
{
    private readonly App app;
    private readonly Action<bool> changed;
    private readonly StackPanel rows = new();
    private readonly ScrollViewer details;
    private readonly Button toggle;
    private readonly TextBlock warning = UI.Text("", 11, UI.Muted);
    private bool commanding;

    internal LopataWorkPanel(App app, bool open, Action<bool> changed)
    {
        this.app = app;
        this.changed = changed;
        Margin = new(0, 0, 0, 8);
        var row = new Grid();
        var actions = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, MinWidth = 0 };
        var links = new StackPanel { Orientation = Orientation.Horizontal };
        row.ColumnDefinitions.Add(new());
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        toggle = UI.CaptionButton("LOPATA tasks", () => ShowDetails(details!.Visibility != Visibility.Visible));
        var chat = UI.CaptionButton("LOPATA chat", app.ShowLopataChat);
        toggle.Content = Localization.Text("Tasks");
        toggle.ToolTip = Localization.Text("LOPATA tasks");
        chat.Content = Localization.Text("Chat");
        chat.ToolTip = Localization.Text("LOPATA chat");
        var updates = UI.CaptionButton("LOPATA updates", app.ShowLopataUpdates);
        updates.ToolTip = Localization.Text("LOPATA updates");
        void RefreshUpdates()
        {
            var count = app.LopataUpdates.Versions.Length;
            var suffix = count switch { 0 => "", 1 => " ↑", _ => " ↑" + count };
            updates.Content = Localization.Text(row.ActualWidth < 320 ? "Updates…" : "Updates") + suffix;
        }
        row.SizeChanged += (_, _) => RefreshUpdates();
        updates.Loaded += (_, _) => { app.LopataUpdates.Changed += RefreshUpdates; RefreshUpdates(); };
        updates.Unloaded += (_, _) => app.LopataUpdates.Changed -= RefreshUpdates;
        var vk = SocialButton("VK", LopataEdition.VkUrl);
        var youtube = SocialButton("YouTube", LopataEdition.YouTubeUrl);
        foreach (var action in new[] { toggle, chat, updates, vk, youtube })
        {
            action.FontSize = 11;
            action.MinHeight = 26;
            action.Margin = new(0, 0, 4, 0);
            action.Padding = new(4, 3, 4, 3);
        }
        actions.Children.Add(toggle);
        actions.Children.Add(chat);
        actions.Children.Add(updates);
        links.Children.Add(vk);
        links.Children.Add(youtube);
        Grid.SetColumn(links, 1);
        row.Children.Add(actions);
        row.Children.Add(links);
        Children.Add(row);
        details = UI.Scroll(rows);
        details.MaxHeight = 150;
        details.Margin = new(0, 6, 0, 0);
        details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Children.Add(details);
        Children.Add(warning);
    }

    private Button SocialButton(string label, string url)
    {
        var button = UI.CaptionButton(label, () => SupportLinks.Open(url, app.Store.Log));
        button.ToolTip = url;
        return button;
    }

    internal void ShowDetails(bool open)
    {
        details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        changed(open);
    }

    internal void Refresh(IslandResponse state)
    {
        var jobs = state.Jobs ?? [];
        var name = Localization.Text("Tasks");
        toggle.Content = jobs.Length == 0 ? name : name + " · " + jobs.Length;
        var focused = System.Windows.Input.Keyboard.FocusedElement as Button;
        var focusedId = focused is null ? null : UI.Identity(focused);
        var hadFocus = IsKeyboardFocusWithin;
        rows.Children.Clear();
        if (jobs.Length == 0)
        {
            var caption = state.Result switch
            {
                "offline" => "LOPATA is closed",
                "ok" when state.Busy => "LOPATA is busy",
                "ok" => "No active LOPATA tasks",
                _ => "LOPATA task status is unavailable"
            };
            rows.Children.Add(UI.Caption(caption, 11, UI.Muted));
            return;
        }
        foreach (var job in jobs)
        {
            var card = new DockPanel { Margin = new(2, 0, 4, 5) };
            if (job.CanPause || job.CanResume)
            {
                var control = Control(job, job.CanPause ? "pause" : "resume");
                DockPanel.SetDock(control, Dock.Right);
                card.Children.Add(control);
                if (hadFocus && focusedId == UI.Identity(control) && control.IsEnabled)
                    control.Loaded += (_, _) => control.Focus();
            }
            var description = new StackPanel { Margin = new(0, 1, 7, 2) };
            description.Children.Add(UI.Text(job.Title, 12));
            var phase = Localization.Text("LOPATA task " + job.Phase);
            if (job.Progress is { } progress && double.IsFinite(progress))
                phase += $" · {Math.Clamp(progress, 0, 1):0%}";
            description.Children.Add(UI.Text(phase, 10, UI.Muted));
            card.Children.Add(description);
            rows.Children.Add(card);
        }
    }

    private Button Control(IslandJob job, string action)
    {
        var button = UI.CaptionButton(action == "pause" ? "Pause LOPATA task" : "Resume LOPATA task", async () =>
        {
            if (commanding) return;
            commanding = true;
            warning.Text = "";
            Refresh(app.LopataLink.State);
            try
            {
                var result = await app.LopataLink.ControlAsync(action, job.Id, CancellationToken.None);
                if (result.Result != "ok") warning.Text = Localization.Text("LOPATA did not confirm the task command.");
            }
            finally { commanding = false; Refresh(app.LopataLink.State); }
        });
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, action + "." + job.Id);
        button.IsEnabled = !commanding;
        button.Padding = new(7, 3, 7, 3);
        button.Margin = new(0);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }
}
