using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Lopata.Updates;

namespace NabyScape;

public partial class App
{
    private HttpClient? lopataUpdateHttp;
    private Window? lopataUpdatesWindow;
    internal LopataUpdateMonitor LopataUpdates { get; private set; } = null!;

    private void InitializeLopataUpdates()
    {
        lopataUpdateHttp = new() { Timeout = TimeSpan.FromSeconds(30) };
        var catalog = new PublishedUpdateCatalog(lopataUpdateHttp, UpdateReleaseKeys.Trusted);
        var probe = new LopataInstalledUpdateProbe();
        LopataUpdates = new(probe.Read,
            (target, token) => catalog.CheckAsync(target.Version, target.Channel!.Value, token, preferFreshManifest: true),
            Store.Log);
    }

    internal void ShowLopataUpdates()
    {
        if (lopataUpdatesWindow is not null)
        {
            lopataUpdatesWindow.Activate();
            return;
        }
        var installed = UI.Text("", 13);
        var status = UI.Text("", 12, UI.Muted);
        var versions = new StackPanel();
        var versionsScroll = UI.Scroll(versions);
        versionsScroll.MaxHeight = 208;
        versionsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var check = UI.CaptionButton("Check for updates", () => { _ = LopataUpdates.RefreshAsync(manual: true); });
        var actions = new WrapPanel();
        actions.Children.Add(check);
        actions.Children.Add(UI.CaptionButton(LopataEdition.OpenCaption, LopataEdition.OpenHost));
        var panel = new StackPanel { Margin = new(16) };
        panel.Children.Add(installed);
        status.Margin = new(0, 6, 0, 8);
        panel.Children.Add(status);
        panel.Children.Add(versionsScroll);
        panel.Children.Add(actions);
        var window = new Window
        {
            Title = Localization.Text("LOPATA updates"), Content = panel, Width = 355,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false
        };
        window.SetResourceReference(Window.BackgroundProperty, "ControlFill");
        lopataUpdatesWindow = window;

        void Refresh()
        {
            var target = LopataUpdates.Target;
            installed.Text = target is null ? "" : Localization.Text("Installed:") + " " + target.Version;
            installed.Visibility = target is null ? Visibility.Collapsed : Visibility.Visible;
            var state = LopataUpdates.Status;
            status.Text = state == "LOPATA update check complete"
                ? (LopataUpdates.Versions.Length == 0 ? Localization.Text("LOPATA is up to date") : "")
                : Localization.Text(state);
            status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            check.IsEnabled = target?.Channel is not null && !LopataUpdates.Checking;
            var incoming = LopataUpdates.Versions;
            if (ReferenceEquals(versions.Tag, incoming)) return;
            versions.Tag = incoming;
            versions.Children.Clear();
            foreach (var release in incoming)
            {
                var url = $"https://github.com/{PublishedUpdateCatalog.Repository}/releases/tag/{release.Tag}";
                var link = UI.CaptionButton(release.Version + " ↗", () => SupportLinks.Open(url, Store.Log));
                link.HorizontalAlignment = HorizontalAlignment.Stretch;
                versions.Children.Add(link);
            }
        }

        LopataUpdates.Changed += Refresh;
        window.Closed += (_, _) => { LopataUpdates.Changed -= Refresh; lopataUpdatesWindow = null; };
        Refresh();
        overlay?.Collapse();
        window.Show();
        window.Activate();
    }
}

public sealed partial class OverlayWindow
{
    private readonly TextBlock lopataUpdateBadge = UI.Text("", 11);

    private void InitializeLopataUpdateBadge(Grid header)
    {
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        // The animated character is a separate visual over the rightmost header slot.
        Grid.SetColumn(lopataUpdateBadge, Grid.GetColumn(assistantHeaderSpace));
        Grid.SetColumn(assistantHeaderSpace, header.ColumnDefinitions.Count - 1);
        lopataUpdateBadge.Margin = new(7, 0, 0, 0);
        lopataUpdateBadge.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        lopataUpdateBadge.ToolTip = Localization.Text("LOPATA updates");
        header.Children.Add(lopataUpdateBadge);
        void Refresh()
        {
            int count = app.LopataUpdates.Versions.Length;
            lopataUpdateBadge.Text = count > 1 ? "↑ " + count : "↑";
            lopataUpdateBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UI.Named(lopataUpdateBadge, Localization.Text("LOPATA updates") + " · " + count);
        }
        app.LopataUpdates.Changed += Refresh;
        Closed += (_, _) => app.LopataUpdates.Changed -= Refresh;
        Refresh();
    }
}
