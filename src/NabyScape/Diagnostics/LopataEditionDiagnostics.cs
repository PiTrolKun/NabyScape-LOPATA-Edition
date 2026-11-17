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
            var saved = IslandPreferences.Read();
            if (island.Topmost != saved.AboveWindows) throw new InvalidOperationException("Initial window layer did not match preference.");
            app.ToggleLopataLayer();
            if (IslandPreferences.Read().AboveWindows == saved.AboveWindows || island.Topmost == saved.AboveWindows)
                throw new InvalidOperationException("Window layer did not change.");
            IslandPreferences.Save(saved with { Tone = "Forest", Advanced = true, ClosedWidth = 325 });
            await Task.Delay(1100);
            if (app.Settings.Background != "#14251E" || app.Settings.Width != 325 || island.Topmost != saved.AboveWindows)
                throw new InvalidOperationException("Live settings did not take effect.");
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
            File.WriteAllText(Path.Combine(directory, "island-check.txt"), "PASS: actual WPF overlay, expanded LOPATA actions, persisted window layer, live appearance and language, complete hover controls, scroll edges at start/middle/end.");
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory, "island-check.txt"), e.ToString());
            Environment.ExitCode = 1;
        }
        finally { app.Quit(); }
    }

    private static async Task CheckScrollEdges(App app, OverlayWindow island, string directory)
    {
        double savedHeight = app.Settings.PanelMaxHeight;
        var scroll = Diagnostics.Descendants(island).OfType<ScrollViewer>().First();
        try
        {
            app.Settings.PanelMaxHeight = 270;
            app.ChangeSettings();
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

            app.Settings.PanelMaxHeight = 540;
            app.ChangeSettings();
            island.SelectPage("media", false);
            island.UpdateLayout();
            if (scroll.ScrollableHeight > 0.5 || presenter.OpacityMask is not null)
                throw new InvalidOperationException("Fitting content should have no edge fade.");
        }
        finally
        {
            app.Settings.PanelMaxHeight = savedHeight;
            app.ChangeSettings();
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