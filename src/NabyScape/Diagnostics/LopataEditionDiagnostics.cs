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
            island.Reveal(false);
            await Task.Delay(700);
            if (!island.IsExpanded || !Diagnostics.Descendants(island).OfType<Button>().Any(
                button => button.Content?.ToString() == Localization.Text(LopataEdition.OpenCaption)))
                throw new InvalidOperationException("Expanded island missing LOPATA actions.");
            Capture(island, Path.Combine(directory, "island-open.png"));

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
            File.WriteAllText(Path.Combine(directory, "island-check.txt"), "PASS: actual WPF overlay, expanded LOPATA actions, persisted window layer, live appearance and language.");
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(directory, "island-check.txt"), e.ToString());
            Environment.ExitCode = 1;
        }
        finally { app.Quit(); }
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