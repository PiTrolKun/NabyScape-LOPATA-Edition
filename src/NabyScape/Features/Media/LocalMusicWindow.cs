using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class Settings
{
    public string LocalMusicFolder { get; set; } = "";
}

internal sealed class LocalMusicWindow : Window
{
    private readonly App app;
    private readonly LocalMusicSource source;
    private readonly ILocalMusicTransport transport;
    private readonly ListBox tracks = new() { DisplayMemberPath = nameof(LocalMusicTrack.Name) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly TextBlock folder = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox search = new() { Margin = new(0, 3, 0, 8) };
    private readonly Button play;
    private CancellationTokenSource? loading, opening;
    private IReadOnlyList<LocalMusicTrack> listing = [];
    private bool closed, playing;

    internal LocalMusicWindow(App app, LocalMusicSource source)
    {
        this.app = app;
        this.source = source;
        transport = app.Media;
        Title = Localization.Text(SourceName(source));
        Width = 430; Height = 390; MinWidth = 300; MinHeight = 250;
        ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UI.Brush("#151B25");
        var dock = new DockPanel { Margin = new(16), LastChildFill = true };
        var header = new StackPanel();
        var actions = new WrapPanel();
        if (source == LocalMusicSource.Folder)
        {
            actions.Children.Add(UI.CaptionButton("Choose music folder…", ChooseFolder));
            folder.Foreground = UI.Muted;
            header.Children.Add(folder);
        }
        actions.Children.Add(UI.CaptionButton("Refresh tracks", () => _ = LoadAsync()));
        header.Children.Add(actions);
        search.ToolTip = Localization.Text("Filter tracks by name");
        search.Foreground = Brushes.White;
        search.Background = UI.Brush("#202A39");
        search.Padding = new(6);
        UI.LocalizedName(search, "Filter tracks by name");
        search.TextChanged += (_, _) => ApplyFilter();
        var filterLabel = new Label { Content = Localization.Text("Filter tracks by name"), Target = search,
            Foreground = UI.Muted, Padding = new(0), Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
        header.Children.Add(filterLabel);
        header.Children.Add(search);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);

        play = UI.CaptionButton("Play selected track", () => _ = PlayAsync());
        play.IsEnabled = false;
        var footer = new StackPanel();
        status.Foreground = UI.Muted;
        footer.Children.Add(status);
        footer.Children.Add(play);
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        tracks.Background = UI.Brush("#202A39");
        tracks.Foreground = Brushes.White;
        VirtualizingPanel.SetIsVirtualizing(tracks,true);
        VirtualizingPanel.SetVirtualizationMode(tracks,VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(tracks,true);
        UI.LocalizedName(tracks, "Local music tracks");
        tracks.SelectionChanged += (_, _) => play.IsEnabled = tracks.SelectedItem is LocalMusicTrack && !playing;
        tracks.MouseDoubleClick += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject child && ItemsControl.ContainerFromElement(tracks,child) is ListBoxItem)
                _ = PlayAsync();
        };
        tracks.KeyDown += (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; _ = PlayAsync(); } };
        dock.Children.Add(tracks);
        Content = dock;
        Loaded += (_, _) => _ = LoadAsync();
        Closed += (_, _) => { closed = true; loading?.Cancel(); opening?.Cancel(); };
    }

    internal static string SourceName(LocalMusicSource source) =>
        source == LocalMusicSource.Collection ? "Music made with LOPATA" : "Music folder";

    private void ChooseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Localization.Text("Choose music folder…") };
        if (Directory.Exists(app.Settings.LocalMusicFolder)) dialog.InitialDirectory = app.Settings.LocalMusicFolder;
        if (dialog.ShowDialog(this) != true) return;
        app.Settings.LocalMusicFolder = dialog.FolderName;
        app.ChangeSettings();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (closed) return;
        loading?.Cancel();
        using var request = new CancellationTokenSource();
        loading = request;
        string path = app.Settings.LocalMusicFolder ?? "";
        folder.Text = path;
        folder.ToolTip = path;
        listing = [];
        ApplyFilter();
        status.Text = Localization.Text("Reading the music list…");
        try
        {
            var tracks = await transport.TracksAsync(source, path, request.Token);
            if (request.IsCancellationRequested || closed) return;
            listing = tracks;
            ApplyFilter();
            status.Text = listing.Count == 0 ? Localization.Text(source == LocalMusicSource.Folder && path.Length == 0 ?
                "Choose a folder to see its tracks." : "No playable audio files found.") : "";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException or System.Text.Json.JsonException)
        {
            if (!request.IsCancellationRequested && !closed) status.Text = Localization.Text("The music list could not be read.");
        }
        finally
        {
            if (ReferenceEquals(loading,request)) loading = null;
        }
    }

    private void ApplyFilter()
    {
        var selected = tracks.SelectedItem;
        string query = search.Text.Trim();
        tracks.ItemsSource = query.Length == 0 ? listing : listing.Where(track => track.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if (selected is not null) tracks.SelectedItem = selected;
    }

    private async Task PlayAsync()
    {
        if (playing || tracks.SelectedItem is not LocalMusicTrack track) return;
        using var request = new CancellationTokenSource();
        opening = request;
        playing = true;
        play.IsEnabled = false;
        status.Text = Localization.Text("Opening audio…");
        try
        {
            if (await transport.PlayLocalAsync(track,listing,request.Token))
            {
                if (!closed) Close();
            }
            else if (!closed) status.Text = Localization.Text(app.Media.LocalOpenError ?? "The track could not be opened.");
        }
        finally
        {
            opening = null;
            playing = false;
            if (!closed) play.IsEnabled = tracks.SelectedItem is LocalMusicTrack;
        }
    }
}