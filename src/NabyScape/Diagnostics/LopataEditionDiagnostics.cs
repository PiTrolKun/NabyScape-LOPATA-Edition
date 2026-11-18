using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lopata.Island;

namespace NabyScape;

internal static class LopataEditionDiagnostics
{
    internal static async Task Run(App app, OverlayWindow island, string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            await Task.Delay(900);
            if (!island.IsVisible || island.IsExpanded) throw new InvalidOperationException("Collapsed island not visible.");
            Capture(island, Path.Combine(directory, "island-compact.png"));
            island.DiagnosticRun = true;
            await CheckMascot(app, island, directory);
            app.Media.DiagnosticFixture = true;
            app.Media.DiagnosticPlayback(false);
            // Reproduce a profile saved by the earlier build, before the preference minimum is applied.
            app.Settings.HoverHeight = 146;
            island.PreviewActivity();
            island.SelectPeek("media");
            for (int i = 0; i < 30 && !island.HoverSettled; i++) await Task.Delay(50);
            var overflow = island.HoverOverflow();
            if (!island.PeekDetailed || overflow.Length != 0)
                throw new InvalidOperationException("Detailed music preview clips its controls: " + overflow);
            Capture(island, Path.Combine(directory, "island-hover-controls.png"));
            island.Collapse();
            app.Media.DiagnosticFixture = false;
            app.Settings.HoverHeight = IslandPreferences.Read().HoverHeight;
            island.Reveal(false);
            await Task.Delay(700);
            if (!island.IsExpanded || !Diagnostics.Descendants(island).OfType<Button>().Any(
                button => UI.Identity(button) == LopataEdition.OpenCaption))
                throw new InvalidOperationException("Expanded island missing LOPATA actions.");
            Capture(island, Path.Combine(directory, "island-open.png"));

            await CheckActionToolbar(app, island, directory);
            var exit = Diagnostics.Descendants(island).OfType<Button>().Single(button =>
                button.IsVisible && UI.Identity(button) == "Island exit options");
            exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var exitMenu = exit.ContextMenu;
            if (exitMenu?.IsOpen != true || exitMenu.Items.OfType<MenuItem>().Select(item => item.Header).SequenceEqual(
                new object[] { Localization.Text(LopataEdition.CloseCaption), Localization.Text(LopataEdition.RestartCaption) }) != true)
                throw new InvalidOperationException("Island exit menu did not present Close and Restart.");
            exitMenu.IsOpen = false;
            File.WriteAllText(Path.Combine(directory, "island-exit-menu-check.txt"), "PASS The header opens both localized actions.");
            await CheckScrollEdges(app, island, directory);
            await CheckWorkAndChat(island, directory);
            await LopataSideDockDiagnostics.Check(app,island,directory,Capture);
            await LopataWidgetReviewDiagnostics.Check(app,island,directory,Capture);
            await LopataHomeWidgetDiagnostics.Check(app,island,directory,Capture);
            var saved = IslandPreferences.Read();
            await CheckSharedPreferences(app, island, saved, directory);
            var originalLanguage = IslandPreferences.ReadJson<IslandLanguage>("language.json");
            IslandPreferences.SaveJson("language.json", new IslandLanguage("en", new()
            {
                [LopataEdition.OpenCaption] = "Custom host caption"
            }));
            await Task.Delay(1000);
            if (Localization.Text(LopataEdition.OpenCaption) != "Custom host caption" || app.MainWindow == island)
                throw new InvalidOperationException("Live localization did not take effect.");
            if (originalLanguage is not null) IslandPreferences.SaveJson("language.json", originalLanguage);
            IslandPreferences.Save(saved);
            File.WriteAllText(Path.Combine(directory, "island-check.txt"), "PASS: actual WPF overlay, expanded LOPATA actions, persisted window layer, live appearance and language, complete hover controls, scroll edges at start/middle/end, two job states and progress, draft kept on cancel and removed after acknowledged transfer.");
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory, "island-check.txt"), e.ToString());
            Environment.ExitCode = 1;
        }
        finally { app.Quit(); }
    }

    private static async Task CheckActionToolbar(App app, OverlayWindow island, string directory)
    {
        var saved = IslandPreferences.Read();
        double scale = app.Settings.PanelScale;
        var order = new[]
        {
            LopataEdition.OpenCaption, LopataEdition.LayerCaption,
            "LOPATA tasks", "LOPATA chat", "LOPATA updates", "VK", "YouTube"
        };
        void CheckRow()
        {
            island.UpdateLayout();
            var panel = Diagnostics.Descendants(island).OfType<LopataWorkPanel>().Single();
            var navigation = Diagnostics.Descendants(island).OfType<Grid>().Single(grid => Equals(grid.Tag, "panel-navigation"));
            var close = Find(island, "Island exit options");
            var tabs = navigation.Children.OfType<ScrollViewer>().Single();
            var closeBounds = close.TransformToAncestor(navigation).TransformBounds(new Rect(close.RenderSize));
            var tabBounds = tabs.TransformToAncestor(navigation).TransformBounds(new Rect(tabs.RenderSize));
            var closeCenter = new Point(closeBounds.Left + closeBounds.Width / 2, closeBounds.Top + closeBounds.Height / 2);
            var closeHit = navigation.InputHitTest(closeCenter) as DependencyObject;
            if (!close.IsVisible || !close.IsTabStop || !Equals(close.ToolTip, Localization.Text("Island exit options")) ||
                close.Parent != navigation || closeBounds.Left < 0 || tabBounds.Left - closeBounds.Right < 12 ||
                (closeHit != close && (closeHit is null || !close.IsAncestorOf(closeHit))))
                throw new InvalidOperationException("The close button is not isolated in the leading navigation slot.");
            var buttons = order.Select(id => Find(panel, id)).ToArray();
            var mascot = Find(panel, "Lopata.Chat.Mascot");
            Rect Bounds(Button button) => button.TransformToAncestor(panel)
                .TransformBounds(new Rect(button.RenderSize));
            var figure = Bounds(mascot);
            double previousRight = figure.Right;
            foreach (var button in buttons)
            {
                var bounds = Bounds(button);
                var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                var hit = panel.InputHitTest(center) as DependencyObject;
                while (hit is not null && hit != button) hit = VisualTreeHelper.GetParent(hit);
                if (button.Content is not Grid || !button.IsVisible || bounds.Width < 20 ||
                    bounds.Left < previousRight - .1 || bounds.Right > panel.ActualWidth + .1 ||
                    Math.Abs(center.Y - figure.Top - figure.Height / 2) > 1 ||
                    hit != button || !button.Focusable || !button.IsTabStop ||
                    AutomationProperties.GetName(button) != Localization.Text(UI.Identity(button)) ||
                    string.IsNullOrWhiteSpace(button.ToolTip as string))
                    throw new InvalidOperationException($"Action is missing, clipped or inaccessible at {panel.ActualWidth:0.#}: {UI.Identity(button)}.");
                previousRight = bounds.Right;
            }
        }
        async Task Apply(IslandPreferences appearance, double contentScale)
        {
            IslandPreferences.Save(appearance);
            for (int i = 0; i < 30 && app.Settings.PanelWidth != (appearance.Advanced ? appearance.PanelWidth : new IslandPreferences().PanelWidth); i++)
                await Task.Delay(100);
            app.Settings.PanelScale = contentScale;
            app.ChangeSettings();
            await Task.Delay(370);
        }

        CheckRow();
        try
        {
            await Apply(saved with { Advanced = true, PanelWidth = 320, PanelHeight = 270 }, 1.25);
            if (app.Settings.PanelWidth != 320) throw new InvalidOperationException("Minimum panel width was not applied.");
            CheckRow();
            Capture(island, Path.Combine(directory, "island-actions-narrow.png"));
        }
        finally { await Apply(saved, scale); }
        File.WriteAllText(Path.Combine(directory, "action-toolbar-check.txt"),
            "PASS Close has its own hit target to the left of the tabs, separate from the seven action icons and mascot, at default width and 320 DIP with 125% content scale.");
    }

    private static async Task CheckMascot(App app, OverlayWindow island, string directory)
    {
        var report = new System.Collections.Generic.List<string>();
        void Check(bool success, string name)
        {
            if (!success) throw new InvalidOperationException(name);
            report.Add("PASS " + name);
        }
        async Task WaitFor(Func<bool> condition, string name)
        {
            for (int i = 0; i < 50 && !condition(); i++) await Task.Delay(40);
            Check(condition(), name);
        }
        LopataMascotView View(bool compact) => Diagnostics.Descendants(island).OfType<LopataMascotView>()
            .Single(view => view.Compact == compact);

        static bool ShowsScreen(LopataMascotView view)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),
                (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            int stride = 4 * bitmap.PixelWidth;
            var bytes = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(bytes, stride, 0);
            int green = 0;
            for (int i = 0; i < bytes.Length; i += 4)
                if (bytes[i+1] > 70 && bytes[i+1] > bytes[i+2] * 1.6 && bytes[i+1] > bytes[i] * 1.6) green++;
            return green > 8;
        }

        var small = View(true);
        Check(small.IsVisible && small.Frame.State == MascotState.Idle && ShowsScreen(small),
            "compact original CRT artwork is drawn with its green screen");
        var model = new LopataMascotMotion();
        model.LookToward(new Vector(50, -50));
        for (int i = 0; i < 20; i++) model.Advance(.04, 0, false);
        Check(model.Frame(true).Gaze == new Vector(8, -5) && !model.NeedsFrame,
            "gaze stays in bounds and stops repainting when it settles");

        model.SetActivity(false, true, false);
        double low = 0, high = 0;
        for (int i = 0; i < 6 * 60; i++)
        {
            model.Advance(1d / 60, .12, i % 2 == 0);
            if (i < 3 * 60) continue;
            low = Math.Min(low, model.Frame(true).Sway);
            high = Math.Max(high, model.Frame(true).Sway);
        }
        Check(low < -.20 && high > .20, "audible even-level music keeps moving between detectable accents");
        for (int i = 0; i < 90; i++) model.Advance(1d / 60, 0, i % 2 == 0);
        Check(model.Frame(true) is { Sway: 0, Accent: 0 }, "silence smoothly returns the shovel to center");

        var lateStroke = new MascotFrame(MascotState.Work, false, Seconds: 1.04);
        Check(MascotArt.DigStroke(lateStroke) < MascotArt.DigStroke(lateStroke with { Seconds = 1.25 }),
            "the dig continues through the former frozen part of the cycle");
        Check(Math.Abs(MascotArt.DigStroke(lateStroke with { Seconds = 1.35 }) -
            MascotArt.DigStroke(lateStroke with { Seconds = 0 })) < .001, "the digging loop joins smoothly");

        float peak = 0;
        app.Audio.DiagnosticPeak = () => peak;
        app.Media.DiagnosticFixture = true;
        try
        {
            app.Media.DiagnosticPlayback(true);
            await WaitFor(() => small.Frame.State == MascotState.Music, "media session switches compact mascot to dance");
            await Task.Delay(240);
            Check(small.Frame.Sway == 0, "silence keeps the compact shovel centered");
            peak = .65f;
            await WaitFor(() => small.Frame.Sway > .15, "first audio accent moves the compact shovel right");
            peak = 0;
            await Task.Delay(310);
            peak = .7f;
            await WaitFor(() => small.Frame.Sway < -.15, "second audio accent moves the compact shovel left");
            Capture(island, Path.Combine(directory, "mascot-compact-music.png"));

            island.Reveal(false);
            island.UpdateLayout();
            await WaitFor(() => View(false).Frame.State == MascotState.Music && !island.IsAnimating,
                "expanding keeps the music state and presents the full figure");
            var full = View(false);
            Check(full.IsVisible && ShowsScreen(full) && full.ActualHeight >= 54, "full mascot artwork fits the header");
            Capture(island, Path.Combine(directory, "mascot-open-music.png"));

            var running = new IslandResponse("ok", [new("fixture", "Fixture", "Running", true, false)], ModelActive: true);
            island.ObserveMascotRuntime(running, false, true);
            await WaitFor(() => full.Frame.State == MascotState.Work && full.Frame.Seconds > .16,
                "active task has priority over music and starts the digging animation");
            Capture(island, Path.Combine(directory, "mascot-open-work.png"));
            var failed = running with { ModelFailure = "one-confirmed-failure" };
            island.ObserveMascotRuntime(failed, false, true);
            await WaitFor(() => full.Frame.State == MascotState.Error && full.Frame.Seconds > .10,
                "confirmed failure interrupts work with a short reaction");
            Capture(island, Path.Combine(directory, "mascot-open-error.png"));
            await WaitFor(() => full.Frame.State == MascotState.Work, "error expires and returns to the active task");
            island.ObserveMascotRuntime(failed, false, true);
            Check(full.Frame.State == MascotState.Work, "a repeated status cannot replay the same failure");
            island.ObserveMascotRuntime(new("ok", [new("fixture", "Fixture", "Paused", false, true)], ModelActive: false), false, true);
            Check(full.Frame.State == MascotState.Music, "pausing a job returns to music");
            app.Media.DiagnosticPlayback(false);
            await WaitFor(() => full.Frame.State == MascotState.Idle, "pausing playback returns to idle");
            island.ObserveMascotRuntime(new("offline"), true, false);
            Check(full.Frame.State == MascotState.Work, "an autonomous local core can drive the mascot");

            var reduce = app.Settings.ReduceMotion;
            try
            {
                app.Settings.ReduceMotion = true;
                island.ObserveMascotRuntime(new("offline"), true, false);
                Check(full.Frame is { State: MascotState.Work, Still: true }, "reduced motion retains the work pose without animation");
            }
            finally { app.Settings.ReduceMotion = reduce; }

            app.Media.DiagnosticPlayback(true);
            await WaitFor(() => full.Frame.State == MascotState.Music, "music animation resumes in the open island");
            island.ToggleVisibility();
            await Task.Delay(120);
            long reads = app.Audio.DiagnosticMeterReads;
            await Task.Delay(240);
            Check(island.UserHidden && app.Audio.DiagnosticMeterReads == reads,
                "hidden island stops sampling playback for the mascot");
            app.Media.DiagnosticPlayback(false);
            island.ToggleVisibility();
            await WaitFor(() => small.Frame.State == MascotState.Idle, "showing the island uses the current paused player state");
        }
        finally
        {
            island.ObserveMascotRuntime(new("offline"), false, false);
            app.Media.DiagnosticPlayback(false);
            app.Media.DiagnosticFixture = false;
            app.Audio.DiagnosticPeak = null;
            island.Collapse();
            await Task.Delay(450);
        }
        File.WriteAllLines(Path.Combine(directory, "mascot-check.txt"), report);
    }

    private static async Task CheckSharedPreferences(App app, OverlayWindow island, IslandPreferences saved, string directory)
    {
        var log = new System.Collections.Generic.List<string>();
        var handle = new System.Windows.Interop.WindowInteropHelper(island).Handle;
        int registrations = 0;
        island.DiagnosticHotkeyRegistration = (_, _, _, _) => { registrations++; return true; };
        var screens = System.Windows.Forms.Screen.AllScreens;
        var primary = screens.Single(screen => screen.Primary);
        var other = screens.FirstOrDefault(screen => !screen.Primary);

        async Task Apply(IslandPreferences value)
        {
            IslandPreferences.Save(value);
            await Task.Delay(850);
            if (Native.IsTopmost(handle) != value.AboveWindows || island.Topmost != value.AboveWindows)
                throw new InvalidOperationException("The visible Windows window did not adopt the saved layer.");
        }

        var custom = saved with
        {
            Tone = "Forest", AboveWindows = false, Advanced = true, ReduceMotion = true, Hotkey = true,
            FollowPointer = false, Monitor = (other ?? primary).DeviceName,
            ClosedWidth = 329, ClosedHeight = 43, HoverWidth = 379, HoverHeight = 213, PanelWidth = 460, PanelHeight = 415
        };
        await Apply(custom);
        var s = app.Settings;
        if (s.Background != "#14251E" || s.Accent != custom.Accent || !s.ReduceMotion || Motion.Enabled
            || !s.HotkeyEnabled || !s.VisibilityHotkeyEnabled || registrations < 2
            || s.Width != 329 || s.Height != 43 || s.HoverWidth != 379 || s.HoverHeight != 213
            || s.PanelWidth != 460 || s.PanelMaxHeight != 415)
            throw new InvalidOperationException("At least one advanced preference did not reach the running island.");
        log.Add("PASS forest scheme and accent, six sizes, reduced motion and both hotkey registrations");
        log.Add("PASS ordinary layer on the existing Windows window");
        if (System.Windows.Forms.Screen.FromHandle(handle).DeviceName != custom.Monitor)
            throw new InvalidOperationException("The real window did not move to the selected screen.");
        await Apply(custom with { Monitor = primary.DeviceName, AboveWindows = true, FollowPointer = false, Tone = "Ocean" });
        if (s.Monitor != primary.DeviceName || s.DisplayMode != "Selected" || s.Background != "#14233B"
            || System.Windows.Forms.Screen.FromHandle(handle).DeviceName != primary.DeviceName)
            throw new InvalidOperationException("Returning to the primary display or the Ocean scheme did not apply.");
        log.Add($"PASS Ocean scheme and native topmost layer; fixed monitor verified on {screens.Length} physical screens");

        Native.EnsureWindowLayer(handle, false);
        await Task.Delay(850);
        if (!Native.IsTopmost(handle)) throw new InvalidOperationException("Layer drift was not corrected.");
        log.Add("PASS persisted layer survives an external change to the actual window");

        var following = custom with { FollowPointer = true, Hotkey = false, HoverWidth = 285 };
        await Apply(following);
        if (s.DisplayMode != "Follow" || s.HotkeyEnabled || s.VisibilityHotkeyEnabled || s.HoverWidth != 300)
            throw new InvalidOperationException("Follow-pointer mode, hotkey opt-out, or consistent hover limits did not apply.");
        log.Add("PASS follow-pointer mode, disabled hotkeys and 300 DIP minimum for old hover widths");

        await Apply(custom with { Advanced = false, Tone = "Graphite" });
        var defaults = new IslandPreferences();
        if (s.DisplayMode != "Primary" || s.Width != defaults.ClosedWidth || s.Height != defaults.ClosedHeight
            || s.HoverWidth != defaults.HoverWidth || s.HoverHeight != defaults.HoverHeight
            || s.PanelWidth != defaults.PanelWidth || s.PanelMaxHeight != defaults.PanelHeight
            || s.ReduceMotion || s.HotkeyEnabled || s.Background != "#121820")
            throw new InvalidOperationException("Turning off advanced options did not restore defaults.");
        log.Add("PASS general switch restores default sizes, behavior and primary monitor");
        await Apply(saved);
        File.WriteAllLines(Path.Combine(directory, "live-appearance-check.txt"), log);
    }

    private static async Task CheckWorkAndChat(OverlayWindow island, string directory)
    {
        var panel = Diagnostics.Descendants(island).OfType<LopataWorkPanel>().Single();
        var jobs = new IslandJob[]
        {
            new("a", "Первый эскиз", "Paused", false, true),
            new("b", "Пакет фотографий", "Running", true, false, 0.4),
        };
        panel.Refresh(new("ok", jobs, true, "b"));
        Click(Find(panel, "LOPATA tasks"));
        island.UpdateLayout();
        var labels = Diagnostics.Descendants(panel).OfType<TextBlock>().Select(value => value.Text).ToArray();
        if (AutomationProperties.GetItemStatus(Find(panel, "LOPATA tasks")) != "2" ||
            !labels.Contains(jobs[0].Title) || !labels.Contains(jobs[1].Title)
            || !labels.Any(text => text.Contains("40")) || Find(panel, "resume.a").Visibility != Visibility.Visible
            || Find(panel, "pause.b").Visibility != Visibility.Visible)
            throw new InvalidOperationException("The expanded island did not show both jobs, selected actions and progress.");
        await Task.Delay(420);
        if (island.PaintedHeight > 280)
            throw new InvalidOperationException("The task panel still reserves room for the hidden media page.");
        Capture(island, Path.Combine(directory, "island-model-tasks.png"));
        Click(Find(panel, "LOPATA tasks"));

        var host = Path.Combine(directory, "fixture-host");
        Directory.CreateDirectory(host);
        using var owner = new System.Threading.Mutex(false, @"Local\" + IslandLink.InstallationName(host));
        using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var connection = new LopataConnection(() => Path.Combine(host, "AIHub.exe"));
        int calls = 0;
        var server = ServeAsync();
        var chat = new LopataChatWindow(connection) { ShowActivated = false };
        try
        {
            chat.Show();
            var draft = Diagnostics.Descendants(chat).OfType<TextBox>().Single();
            const string expected = "Черновик для «Если накипело»";
            draft.Text = expected;
            Click(Find(chat, "Send"));
            await UntilAsync(() => Find(chat, "Pause task and continue").IsVisible);
            Click(Find(chat, "Cancel"));
            if (draft.Text != expected || calls != 1)
                throw new InvalidOperationException("Cancel did not retain the draft or sent a pause.");
            Click(Find(chat, "Send"));
            await UntilAsync(() => Find(chat, "Pause task and continue").IsVisible);
            Click(Find(chat, "Pause task and continue"));
            await UntilAsync(() => !chat.IsVisible);
            if (draft.Text.Length != 0 || calls != 3)
                throw new InvalidOperationException("Acknowledged transfer did not consume the correct draft.");
            await server.WaitAsync(deadline.Token);
        }
        finally { chat.Hide(); await deadline.CancelAsync(); }

        async Task UntilAsync(Func<bool> condition)
        {
            while (true)
            {
                try { if (condition()) return; } catch (InvalidOperationException) { }
                await Task.Delay(50, deadline.Token);
            }
        }

        async Task ServeAsync()
        {
            for (int i = 0; i < 3; i++)
            {
                await using var pipe = IslandLink.ListenHost(host);
                await pipe.WaitForConnectionAsync(deadline.Token);
                var request = await IslandLink.ReadAsync<IslandRequest>(pipe, deadline.Token);
                if (request.Action != "draft" || request.Text != "Черновик для «Если накипело»"
                    || request.PauseId != (i == 2 ? "b" : null))
                    throw new InvalidOperationException("Incorrect handoff payload.");
                calls++;
                await IslandLink.WriteAsync(pipe, i == 2 ? new IslandResponse("transferred")
                    : new IslandResponse("busy", jobs, true, "b"), deadline.Token);
            }
        }

        static void Click(Button button) =>
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
    }

    private static Button Find(DependencyObject scope, string id) => Diagnostics.Descendants(scope).OfType<Button>()
        .Single(button => AutomationProperties.GetAutomationId(button) == id);

    private static async Task CheckScrollEdges(App app, OverlayWindow island, string directory)
    {
        var saved = IslandPreferences.Read();
        var scroll = Diagnostics.Descendants(island).OfType<ScrollViewer>().First();

        async Task ApplyHeight(IslandPreferences appearance)
        {
            IslandPreferences.Save(appearance);
            int height = appearance.Advanced ? appearance.PanelHeight : new IslandPreferences().PanelHeight;
            for (int i = 0; i < 30 && app.Settings.PanelMaxHeight != height; i++) await Task.Delay(100);
            if (app.Settings.PanelMaxHeight != height)
                throw new InvalidOperationException($"Panel height {height} did not reach the running island.");
        }

        try
        {
            await ApplyHeight(saved with { Advanced = true, PanelHeight = 270 });
            island.SelectPage("status", false);
            for (int i = 0; i < 30 && !island.MonitorReady; i++) await Task.Delay(100);
            island.UpdateLayout();
            if (scroll.Template.FindName("PART_ScrollContentPresenter", scroll) is not ScrollContentPresenter presenter)
                throw new InvalidOperationException("Island viewport missing.");
            if (scroll.ScrollableHeight < 8) throw new InvalidOperationException("System fixture did not overflow.");

            void Check(bool top, bool bottom)
            {
                if (presenter.OpacityMask is not LinearGradientBrush mask ||
                    (mask.GradientStops.First().Color.A == 0) != top ||
                    (mask.GradientStops.Last().Color.A == 0) != bottom)
                    throw new InvalidOperationException($"Incorrect scroll edge fade at offset {scroll.VerticalOffset} of {scroll.ScrollableHeight}.");
            }

            scroll.ScrollToTop();
            island.UpdateLayout();
            Check(top: false, bottom: true);
            Capture(island, Path.Combine(directory, "island-scroll-top.png"));
            scroll.ScrollToVerticalOffset(scroll.ScrollableHeight / 2);
            island.UpdateLayout();
            Check(top: true, bottom: true);
            scroll.ScrollToEnd();
            island.UpdateLayout();
            Check(top: true, bottom: false);
            Capture(island, Path.Combine(directory, "island-scroll-end.png"));

            await ApplyHeight(saved with { Advanced = true, PanelHeight = 540 });
            island.SelectPage("media", false);
            island.UpdateLayout();
            if (scroll.ScrollableHeight > 0.5 || presenter.OpacityMask is not null)
                throw new InvalidOperationException("Fitting content should have no edge fade.");
        }
        finally
        {
            await ApplyHeight(saved);
        }
    }

    private static void Capture(OverlayWindow island, string output)
    {
        island.UpdateLayout();
        var full = new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth),
            (int)Math.Ceiling(island.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        full.Render(island);
        int width = Math.Min(full.PixelWidth, (int)Math.Ceiling(island.PaintedWidth));
        int height = Math.Min(full.PixelHeight, (int)Math.Ceiling(island.PaintedHeight));
        var cropped = new CroppedBitmap(full, new Int32Rect((full.PixelWidth - width) / 2, 0, width, height));
        var canvas = new DrawingVisual();
        using (var dc = canvas.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(40, 49, 65)), null, new Rect(0, 0, width + 64, height + 48));
            dc.DrawImage(cropped, new Rect(32, 24, width, height));
        }
        var image = new RenderTargetBitmap(width + 64, height + 48, 96, 96, PixelFormats.Pbgra32);
        image.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(output);
        encoder.Save(file);
    }
}
