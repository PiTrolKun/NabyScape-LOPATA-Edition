using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
        var toolbar = new DockPanel();
        if (app.UsesLopataEdition) AddFullMascot(toolbar);
        lopataWork = new LopataWorkPanel(app, toolbar, showLopataJobs, open =>
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
    private readonly Border taskCount;
    private readonly TextBlock warning = UI.Text("", 11, UI.Muted);
    private bool commanding;

    internal LopataWorkPanel(App app, DockPanel toolbar, bool open, Action<bool> changed)
    {
        this.app = app;
        this.changed = changed;
        Margin = new(0, 0, 0, 8);
        var launch = LopataActions.Button(LopataEdition.OpenCaption, "open-lopata", LopataEdition.OpenHost);
        var layer = LopataActions.Button(LopataEdition.LayerCaption, "pin", app.ToggleLopataLayer);
        toggle = LopataActions.Button("LOPATA tasks", "tasks", () => ShowDetails(details!.Visibility != Visibility.Visible));
        taskCount = LopataActions.AddBadge(toggle);
        var chat = LopataActions.Button("LOPATA chat", "chat", app.ShowLopataChat);
        var updates = LopataActions.Button("LOPATA updates", "updates", app.ShowLopataUpdates);
        var updateCount = LopataActions.AddBadge(updates);
        var vk = SocialButton("VK", "vk", LopataEdition.VkUrl);
        var youtube = SocialButton("YouTube", "youtube", LopataEdition.YouTubeUrl);
        youtube.Margin = new(0);
        void RefreshLayer() => LopataActions.SetLayer(layer, app.LopataAboveWindows);
        void RefreshUpdates() => LopataActions.SetCount(updates, updateCount,
            app.LopataUpdates.Versions.Length, singleAsDot: true);
        Loaded += (_, _) =>
        {
            app.SettingsChanged += RefreshLayer;
            app.LopataUpdates.Changed += RefreshUpdates;
            RefreshLayer();
            RefreshUpdates();
        };
        Unloaded += (_, _) =>
        {
            app.SettingsChanged -= RefreshLayer;
            app.LopataUpdates.Changed -= RefreshUpdates;
        };
        var actions = UI.Row(launch, layer, toggle, chat, updates, vk, youtube);
        actions.VerticalAlignment = VerticalAlignment.Center;
        toolbar.Children.Add(actions);
        Children.Add(new Viewbox
        {
            Child = toolbar,
            HorizontalAlignment = HorizontalAlignment.Left,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly
        });
        details = UI.Scroll(rows);
        details.MaxHeight = 150;
        details.Margin = new(0, 6, 0, 0);
        details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Children.Add(details);
        Children.Add(warning);
    }

    private Button SocialButton(string label, string icon, string url)
    {
        var button = LopataActions.Button(label, icon, () => SupportLinks.Open(url, app.Store.Log));
        AutomationProperties.SetHelpText(button, url);
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
        LopataActions.SetCount(toggle, taskCount, jobs.Length);
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
