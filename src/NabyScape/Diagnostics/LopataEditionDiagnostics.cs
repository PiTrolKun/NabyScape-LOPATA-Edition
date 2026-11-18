using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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
                button => button.Content?.ToString() == Localization.Text(LopataEdition.OpenCaption)))
                throw new InvalidOperationException("Expanded island missing LOPATA actions.");
            Capture(island, Path.Combine(directory, "island-open.png"));

            await CheckScrollEdges(app, island, directory);
            await CheckWorkAndChat(island, directory);
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
        if (!labels.Contains(jobs[0].Title) || !labels.Contains(jobs[1].Title)
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

        static Button Find(DependencyObject scope, string id) => Diagnostics.Descendants(scope).OfType<Button>()
            .Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == id);
        static void Click(Button button) =>
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
    }

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
