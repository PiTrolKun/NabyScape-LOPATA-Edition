using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Lopata.Island;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly LopataMascotMotion mascotMotion = new();
    private readonly DispatcherTimer mascotError = new() { Interval = TimeSpan.FromSeconds(LopataMascotMotion.ErrorSeconds) };
    private LopataMascotView? closedMascot, fullMascot;
    private IslandResponse mascotStatus = new("offline");
    private bool mascotLocalCore, mascotPlaying;
    private string? lastMascotFailure;
    private double mascotFrameTime;

    private bool MascotCanAnimate => app.Settings.LiveActivity && Motion.Enabled && !app.Settings.ReduceMotion;
    private LopataMascotView? VisibleLopataMascot
    {
        get
        {
            if (!app.UsesLopataEdition || !IsVisible || userHidden || IsTucked ||
                WindowState == WindowState.Minimized || peeking || FeedbackVisible) return null;
            if (!expanded) return closedMascot?.IsVisible == true ? closedMascot : null;
            if (fullMascot is not { IsVisible: true, IsLoaded: true } view || !view.IsDescendantOf(scroll)) return null;
            double top = view.TranslatePoint(new Point(), scroll).Y;
            return top + view.ActualHeight > 0 && top < scroll.ActualHeight ? view : null;
        }
    }
    private bool MascotVisualDemand => MascotCanAnimate && mascotMotion.NeedsFrame && VisibleLopataMascot is not null;
    private bool MascotAudioDemand => mascotMotion.State == MascotState.Music && MascotVisualDemand;
    private bool MascotPointerTracking => MascotCanAnimate && mascotMotion.State == MascotState.Idle &&
        VisibleLopataMascot is not null;

    private Button CreateMascotButton(bool closed)
    {
        var view = new LopataMascotView(closed);
        if (closed) closedMascot = view;
        else fullMascot = view;
        var button = UI.Button("", app.ShowLopataChat);
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(0);
        button.MinWidth = button.MinHeight = 0;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.VerticalContentAlignment = VerticalAlignment.Stretch;
        button.ToolTip = Localization.Text("LOPATA chat");
        view.SetBinding(WidthProperty, new Binding(nameof(Width)) { Source = button });
        view.SetBinding(HeightProperty, new Binding(nameof(Height)) { Source = button });
        button.Content = view;
        UI.LocalizedName(button, "LOPATA chat");
        AutomationProperties.SetAutomationId(button, closed ? "Lopata.Chat.CompactMascot" : "Lopata.Chat.Mascot");
        view.Present(mascotMotion.Frame(false));
        view.IsVisibleChanged += (_, _) => SyncLopataMascot();
        view.Loaded += (_, _) => SyncLopataMascot();
        return button;
    }

    private void MountClosedLopataMascot()
    {
        assistantCharacterButton = CreateMascotButton(true);
        assistantCharacterButton.Margin = new Thickness(0, 0, 8, 0);
        assistantCharacterButton.HorizontalAlignment = HorizontalAlignment.Right;
        assistantCharacterButton.VerticalAlignment = VerticalAlignment.Center;
        assistantCharacterHost.Children.Add(assistantCharacterButton);
        visuals.Children.Add(assistantCharacterHost);
        app.LopataLink.Changed += ReadMascotRuntime;
        app.LopataLink.CoreFailed += StartMascotError;
        app.Media.Changed += ReadMascotRuntime;
        app.SettingsChanged += SizeClosedLopataMascot;
        mascotError.Tick += (_, _) => { mascotError.Stop(); UpdateMascotActivity(); };
        scroll.ScrollChanged += (_, args) =>
        {
            if (expanded && ReferenceEquals(args.OriginalSource, scroll)) SyncLopataMascot();
        };
        IsVisibleChanged += (_, _) => SyncLopataMascot();
        SizeClosedLopataMascot();
        ReadMascotRuntime();
    }

    private void SizeClosedLopataMascot()
    {
        double height = Math.Clamp(app.Settings.Height - 2, 24, 36);
        assistantCharacterButton.Width = Math.Round(height * 58 / 34);
        assistantCharacterButton.Height = height;
        assistantHeaderSpace.Width = assistantCharacterButton.Width + 8;
        assistantCharacterHost.Width = ClosedContentWidth;
        assistantCharacterHost.Height = app.Settings.Height;
        SyncLopataMascot();
    }

    private void ReadMascotRuntime() => ObserveMascotRuntime(app.LopataLink.State, app.LopataLink.CoreWorking,
        app.Media.Available && app.Media.Playing);

    internal void ObserveMascotRuntime(IslandResponse status, bool localCore, bool playing)
    {
        mascotStatus = status;
        mascotLocalCore = localCore;
        mascotPlaying = playing;
        if (status.ModelFailure is { Length: > 0 } id && id != lastMascotFailure)
        {
            lastMascotFailure = id;
            StartMascotError();
        }
        else UpdateMascotActivity();
    }

    private void StartMascotError()
    {
        mascotError.Stop();
        mascotMotion.RestartError();
        mascotError.Start();
        UpdateMascotActivity();
    }

    private void UpdateMascotActivity()
    {
        bool work = mascotLocalCore || (mascotStatus.ModelActive ??
            (mascotStatus.Jobs?.Any(job => job.Phase is "Running" or "Pausing") == true));
        mascotMotion.SetActivity(work, mascotPlaying, mascotError.IsEnabled);
        SyncLopataMascot();
    }

    private void SyncLopataMascot()
    {
        if (closedMascot is null) return;
        var visibility = !expanded && !peeking && !FeedbackVisible && !IsTucked && !userHidden
            ? Visibility.Visible : Visibility.Collapsed;
        if (assistantCharacterHost.Visibility != visibility) assistantCharacterHost.Visibility = visibility;
        if (assistantHeaderSpace.Visibility != visibility) assistantHeaderSpace.Visibility = visibility;
        ConfigureDisplayFollow();
        VisibleLopataMascot?.Present(mascotMotion.Frame(MascotCanAnimate));
        WakeLiveRendering();
    }

    private void TrackMascotPointer(Point screenPoint)
    {
        if (VisibleLopataMascot is not { } view) return;
        var local = view.PointFromScreen(screenPoint);
        mascotMotion.LookToward(new Vector((local.X - view.ActualWidth / 2) / 37, (local.Y - view.ActualHeight / 2) / 42));
        WakeLiveRendering();
    }

    private void AdvanceMascot(double seconds, double peak, bool fresh)
    {
        if (!MascotCanAnimate || VisibleLopataMascot is not { } view) return;
        mascotMotion.Advance(seconds, peak, fresh);
        mascotFrameTime += seconds;
        if (mascotFrameTime < 1d / 30 && mascotMotion.NeedsFrame) return;
        mascotFrameTime = 0;
        view.Present(mascotMotion.Frame(true));
    }

    private void AddFullMascot(DockPanel header)
    {
        var mascot = CreateMascotButton(false);
        mascot.Width = 70;
        mascot.Height = 62;
        mascot.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(mascot, Dock.Left);
        header.Children.Insert(0, mascot);
    }

    private void CleanupLopataMascot()
    {
        mascotError.Stop();
        app.LopataLink.Changed -= ReadMascotRuntime;
        app.LopataLink.CoreFailed -= StartMascotError;
        app.Media.Changed -= ReadMascotRuntime;
        app.SettingsChanged -= SizeClosedLopataMascot;
    }
}
