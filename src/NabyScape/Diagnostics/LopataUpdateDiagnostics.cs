using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lopata.Updates;

namespace NabyScape;

internal sealed class LopataUpdateDiagnostics
{
    private LopataUpdateTarget target = new(@"C:\Fixtures\NoRunningLopata\AIHub.exe", "1.0.0", UpdateDelivery.FilePatch, true);
    private bool offline;
    private PublishedUpdateVersion[] published = [];

    internal LopataUpdateMonitor CreateMonitor(Action<Exception> log) => new(() => target, (_, _) =>
    {
        if (offline) throw new HttpRequestException("Offline fixture.");
        UpdateOffer offer = new("1.0.2-beta", UpdateDelivery.FilePatch, [], null, null, null) { PublishedVersions = published };
        return Task.FromResult<UpdateOffer?>(offer);
    }, log);

    internal async Task Run(App app, OverlayWindow island, string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            island.DiagnosticRun = true;
            await Task.Delay(400);
            var monitor = app.LopataUpdates;
            TextBlock Badge() => Diagnostics.Descendants(island).OfType<TextBlock>()
                .Single(label => UI.Identity(label).StartsWith(Localization.Text("LOPATA updates") + " · ", StringComparison.Ordinal));
            async Task Refresh() => await monitor.RefreshAsync(manual: true);
            void Require(bool test, string description)
            {
                if (!test) throw new InvalidOperationException(description);
                File.AppendAllText(Path.Combine(directory, "updates-checks.txt"), "PASS " + description + Environment.NewLine);
            }

            published = [new("1.0.1-beta", "v1.0.1-beta")];
            await Refresh();
            Require(Badge().IsVisible && Badge().Text == "↑", "One patch shows a mark without a number");
            published = [new("1.0.2-beta", "v1.0.2-beta"), .. published, .. published];
            await Refresh();
            await Refresh();
            Require(Badge().Text == "↑ 2", "Two unique published patches are counted once");
            Capture(island, Path.Combine(directory, "island-two-updates.png"));

            island.Reveal(false);
            await Task.Delay(650);
            var buttons = Diagnostics.Descendants(island).OfType<Button>().Where(button => button.IsVisible).ToArray();
            foreach (var (label, url) in new[] { ("VK", LopataEdition.VkUrl), ("YouTube", LopataEdition.YouTubeUrl) })
                Require(buttons.Any(button => UI.Identity(button) == label && button.Content is Grid &&
                    Equals(button.ToolTip, Localization.Text(label)) && AutomationProperties.GetHelpText(button) == url),
                    label + " is visible and targets the official page");

            var open = buttons.Single(button => UI.Identity(button) == "LOPATA updates");
            var badge = ((Grid)open.Content).Children.OfType<Border>().Single();
            var count = (TextBlock)badge.Child;
            Require(badge.IsVisible && count.IsVisible && count.Text == "2" &&
                AutomationProperties.GetItemStatus(open) == "2", "Expanded update icon displays the total and exposes it to assistive tools");
            Capture(island, Path.Combine(directory, "island-community.png"));

            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(200);
            var list = app.Windows.Cast<Window>().Single(window => window.Title == Localization.Text("LOPATA updates"));
            Require(list.IsVisible && Diagnostics.Descendants(list).OfType<Button>().Any(button =>
                Equals(button.Content, "1.0.2-beta ↗")) && Badge().Text == "↑ 2", "Opening the release list keeps the badge and lists the versions");
            Capture(list, Path.Combine(directory, "island-release-list.png"));
            list.Close();

            using var restarted = CreateMonitor(app.Store.Log);
            offline = true;
            await restarted.RefreshAsync(manual: true);
            Require(restarted.Versions.Length == 2, "A restart and failed network check preserve the counter");
            target = target with { Channel = UpdateDelivery.FullInstaller };
            await Refresh();
            Require(!Badge().IsVisible, "Switching channel does not display the previous channel's patches");
            target = target with { Channel = UpdateDelivery.FilePatch, Version = "1.0.1" };
            await Refresh();
            Require(Badge().IsVisible && Badge().Text == "↑", "An intermediate installation drops only the installed patch even offline");
            Require(badge.Visibility == Visibility.Visible && count.Visibility == Visibility.Collapsed,
                "One patch shows a dot on the expanded update icon");
            target = target with { Version = "1.0.2" };
            await Refresh();
            Require(Badge().Visibility == Visibility.Collapsed && badge.Visibility == Visibility.Collapsed,
                "The current installed version clears both badges even offline");
            using var afterInstall = CreateMonitor(app.Store.Log);
            await afterInstall.RefreshAsync(manual: true);
            Require(afterInstall.Versions.Length == 0, "The cleared count survives a restart");
        }
        catch (Exception error)
        {
            File.AppendAllText(Path.Combine(directory, "updates-checks.txt"), error.ToString());
            Environment.ExitCode = 1;
        }
        finally { app.Quit(); }
    }

    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var image = new PngBitmapEncoder();
        image.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        image.Save(file);
    }
}
