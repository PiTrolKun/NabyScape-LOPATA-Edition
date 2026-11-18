using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lopata.Collection;
using Lopata.Island;

namespace NabyScape;

internal static class LopataMusicDiagnostics
{
    private sealed class CountingLyrics : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    internal static async Task Run(App app, OverlayWindow island, string directory)
    {
        var report = new List<string>();
        void Check(bool success, string condition)
        {
            if (!success) throw new InvalidOperationException(condition);
            report.Add("PASS " + condition);
        }
        var local = (ILocalMusicTransport)app.Media;
        var folder = Path.Combine(directory,"music-folder");
        var known = Path.Combine(directory,"known");
        var imported = Path.Combine(directory,"imported", "Transferred.flac");
        var root = Path.Combine(directory,"collection");
        island.DiagnosticRun = true;
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = limit.Token;
        try
        {
            Check(Process.GetProcessesByName("AIHub").Length==0 &&
                Process.GetProcessesByName("LopataIslandPreview").Length==0, "LOPATA is closed");
            new CollectionConfig { Enabled = true, Roots = [known], SearchEnabled = false }.Save(root);
            var music = await local.TracksAsync(LocalMusicSource.Collection, "", token);
            Check(music.Any(t => t.Name=="ThisPC") && music.All(t => t.Name!="Transferred"), "known music works without Explorer worker snapshot");
            var config = CollectionConfig.Load(root);
            Check(CollectionOriginReader.Read(imported) is not null, "copied file retains the LOPATA origin tags");
            Check(CollectionPublication.TryPublish(root, CollectionPublication.Revision(config), [new(imported, "Audio", "Music")]), "worker snapshot admits an indexed exported file");
            string refreshRequest = Path.Combine(root, "refresh.request");
            var requestedAt = File.GetLastWriteTimeUtc(refreshRequest);
            await Task.Delay(30,token);
            music = await local.TracksAsync(LocalMusicSource.Collection, "", token);
            Check(File.GetLastWriteTimeUtc(refreshRequest)>requestedAt, "island requests a fresh collection snapshot");
            Check(music.Any(t => t.Path==imported) && music.Any(t => t.Name=="ThisPC"), "island reads refreshed snapshot and known tracks together");
            var files = await local.TracksAsync(LocalMusicSource.Folder, folder, token);
            Check(files.Any(t => t.Name=="External") && files.Any(t => t.Name=="Copied") && files.All(t => t.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)),
                "separate folder finds marked and unmarked files outside Windows Search");

            using var fake = new CountingLyrics();
            using var lyrics = new LyricsService(fake);
            lyrics.SelectedSource = "LRCLIB";

            async Task Play(LocalMusicTrack track, IReadOnlyList<LocalMusicTrack> queue)
            {
                bool ok = await local.PlayLocalAsync(track,queue);
                Check(ok,"opens "+track.Name+" "+Path.GetExtension(track.Path)+" "+app.Media.LocalOpenError);
                Check(local.CurrentMusic.Track==track && local.CurrentMusic.Playing && local.CurrentMusic.Duration>0, "live transport for "+track.Name);
            }

            app.Settings.LocalMusicFolder = folder;
            island.Reveal(false);
            island.SelectPage("media", false);
            await Task.Delay(350, token);
            Check(!app.Media.Available, "the player starts without an active media session");
            CheckMusicActionsInView(island, Check, "at the standard height");
            Check(Diagnostics.Descendants(island).OfType<TextBlock>().Any(text =>
                text.IsVisible && text.Text == Localization.Text(OverlayWindow.IdleMusicHint)),
                "the idle player points to the track list");
            Capture(island, Path.Combine(directory, "idle-player.png"));
            await CheckShortMusicPanel(app, island, token, Check);
            var controls = Diagnostics.Descendants(island).OfType<MediaControls>().Single();
            controls.LibraryButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, controls.LibraryButton));
            var idleListWindow = island.OwnedWindows.OfType<LocalMusicWindow>().Single();
            Check(idleListWindow.IsVisible, "Track list opens a library before anything is playing");
            idleListWindow.Close();
            controls.SourceButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, controls.SourceButton));
            await Task.Delay(120, token);
            island.UpdateLayout();
            var source = Diagnostics.Descendants(controls.Popover).OfType<Button>().Single(button =>
                AutomationProperties.GetName(button) == "Use media source " + Localization.Text("Music folder"));
            source.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, source));
            var libraryWindow = island.OwnedWindows.OfType<LocalMusicWindow>().Single();
            await Task.Delay(80, token);
            libraryWindow.UpdateLayout();
            var list = Diagnostics.Descendants(libraryWindow).OfType<ListBox>().Single();
            for (int i = 0; i < 30 && list.Items.Count == 0; i++) await Task.Delay(70, token);
            Check(list.Items.Cast<LocalMusicTrack>().Any(track => track.Name == "External"), "source picker opens the folder list on the real island");
            list.SelectedItem = list.Items.Cast<LocalMusicTrack>().Single(track => track.Name == "External");
            Capture(libraryWindow, Path.Combine(directory,"music-folder.png"));
            var playButton = Diagnostics.Descendants(libraryWindow).OfType<Button>().Single(button =>
                button.Content?.ToString() == Localization.Text("Play selected track"));
            playButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, playButton));
            for (int i = 0; i < 40 && !app.Media.Playing; i++) await Task.Delay(70, token);
            Check(local.CurrentMusic.Track?.Name == "External" && app.Media.Playing, "the library play button starts the same island transport");
            CheckMusicActionsInView(island, Check, "during playback");

            var primary = music.Single(t => t.Name=="ThisPC");
            await Play(primary,music);
            await Task.Delay(520,token);
            Check(local.CurrentMusic.Position>0.20,"decoded media clock advances");
            await local.Command("toggle");
            double paused = local.CurrentMusic.Position;
            await Task.Delay(270,token);
            Check(!local.CurrentMusic.Playing && Math.Abs(paused-local.CurrentMusic.Position)<0.12,"pause stops the media clock");
            await local.Seek(0.50);
            Check(Math.Abs(local.CurrentMusic.Position-local.CurrentMusic.Duration*.5)<0.45,"seek moves the media clock");
            await local.Command("toggle");
            Check(local.CurrentMusic.Playing,"resume restarts playback");
            await lyrics.LoadFor(app.Media,true);
            Check(!app.Media.NetworkLyricsAllowed && lyrics.Result.Lines.Any(line=>line.At>=1 && line.Text.Contains("Вторая строка")) && fake.Requests==0,
                "embedded lyrics carry timestamps with zero network requests");
            Check(app.Media.Artwork is not null,"embedded artwork is used in the player");
            app.Settings.LyricsEnabled = true;
            app.ChangeSettings();
            island.Reveal(false);
            island.SelectPage("media",false);
            await Task.Delay(350,token);
            var heart = Diagnostics.Descendants(island).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "Toggle current track favorite");
            var textButton = Diagnostics.Descendants(island).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "Show or hide synced lyrics");
            Check(heart.IsVisible && heart.IsEnabled && textButton.IsVisible && heart.Parent == textButton.Parent,
                "favorite control and lyrics toggle are visible in the same playback row");
            var row = (Grid)heart.Parent;
            var left = heart.TranslatePoint(new Point(heart.ActualWidth/2,heart.ActualHeight/2),row);
            var right = textButton.TranslatePoint(new Point(textButton.ActualWidth/2,textButton.ActualHeight/2),row);
            var centerButton = Diagnostics.Descendants(row).OfType<Button>().Single(button =>
                AutomationProperties.GetAutomationId(button) == "Play or pause");
            var playCenter = centerButton.TranslatePoint(new Point(centerButton.ActualWidth/2,centerButton.ActualHeight/2),row);
            Check(Math.Abs(left.Y-right.Y)<.5 && Math.Abs(left.X+right.X-2*playCenter.X)<.5,
                "favorite control mirrors the lyrics button around Play");
            Check(left.X-heart.ActualWidth/2>=20 && row.ActualWidth-right.X-textButton.ActualWidth/2>=20,
                "both side buttons have at least 20 DIPs of inset in the expanded player");
            Check(heart.ActualWidth==textButton.ActualWidth && heart.ActualHeight==textButton.ActualHeight &&
                Equals(heart.Tag,textButton.Tag) && Equals(heart.Background,textButton.Background),
                "favorite and open lyrics buttons use the same circular background");
            Check(AutomationProperties.GetItemStatus(heart) == "Off","a track starts with an empty favorite icon");
            heart.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,heart));
            Check(AutomationProperties.GetItemStatus(heart) == "On" && new LocalMusicLists(app.Store).Favorites.Single()==primary,
                "favorite button adds the currently playing track to the shared saved list");
            Capture(island,Path.Combine(directory,"player-heart-on.png"));
            heart.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,heart));
            Check(AutomationProperties.GetItemStatus(heart) == "Off" && app.MusicLists.Favorites.Count==0,
                "pressing the favorite button again removes the current track");
            Capture(island,Path.Combine(directory,"local-player.png"));
            var slider = Diagnostics.Descendants(island).OfType<Slider>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "Playback position");
            Check(slider.ActualHeight >= 32 &&
                HitsSlider(island,slider,new Point(slider.ActualWidth*.24,4)) &&
                HitsSlider(island,slider,new Point(slider.ActualWidth*.78,slider.ActualHeight-4)),
                "timeline accepts real WPF hit tests above and below the line, far from its thumb");

            app.Settings.LyricsEnabled = false;
            app.ChangeSettings();
            await Play(music.Single(t => t.Name=="Transferred"),music);
            await lyrics.LoadFor(app.Media,true);
            var blocked = await lyrics.Search("Fixture","Artist",token);
            Check(!app.Media.NetworkLyricsAllowed && blocked.Length==0 && fake.Requests==0, "imported track cannot request lyrics from the network");

            await Play(files.Single(t => t.Name=="Copied"),files);
            await lyrics.LoadFor(app.Media,true);
            Check(!app.Media.NetworkLyricsAllowed && fake.Requests==0,"a marked file in the ordinary folder is detected too");
            await Play(music.Single(t => t.Name=="WithoutLyrics"),music);
            await lyrics.LoadFor(app.Media,true);
            Check(lyrics.Result.Lines.Length==0 && lyrics.Result.Plain.Length==0 && fake.Requests==0,"missing lyrics on a LOPATA track make no online request");
            await Play(files.Single(t => t.Name=="External"),files);
            Check(app.Media.NetworkLyricsAllowed, "external WAV is allowed to use lyrics providers");
            await Play(files.Single(t => t.Name=="MP3"),files);
            await lyrics.LoadFor(app.Media,true);
            Check(app.Media.NetworkLyricsAllowed && fake.Requests==1,
                $"external track with embedded title and artist can use the lyrics provider (calls: {fake.Requests}; {app.Media.Title}; {app.Media.Artist})");

            var queue = new[] { files.Single(t => t.Name=="External"), files.Single(t => t.Name=="MP3") };
            await Play(queue[0],queue);
            await local.Command("next");
            Check(local.CurrentMusic.Track==queue[1] && local.CurrentMusic.Playing,"next uses the same local queue and transport");
            await local.Command("previous");
            Check(local.CurrentMusic.Track==queue[0],"previous goes back in the local queue");
            await Play(files.Single(t => t.Name=="Opus"),files);
            await CheckLists(app,island,directory,token,Check);
            app.Media.SelectNone();
            Check(local.CurrentMusic.Track is null && !local.CurrentMusic.Playing, "changing source stops local playback");
            Check(!heart.IsEnabled,"favorite button is unavailable without an addressed local file");
        }
        catch (Exception error) { report.Add("FAIL "+error); Environment.ExitCode=1; }
        finally
        {
            File.WriteAllLines(Path.Combine(directory,"music-check.txt"),report);
            app.Quit();
        }
    }

    private static bool HitsSlider(OverlayWindow island, Slider slider, Point point)
    {
        for (var hit = island.InputHitTest(slider.TranslatePoint(point,island)) as DependencyObject;
             hit is Visual; hit = VisualTreeHelper.GetParent(hit))
            if (hit == slider) return true;
        return false;
    }

    private static void CheckMusicActionsInView(OverlayWindow island, Action<bool, string> check, string scenario)
    {
        island.UpdateLayout();
        var controls = Diagnostics.Descendants(island).OfType<MediaControls>().Single();
        var scroll = Diagnostics.Descendants(island).OfType<ScrollViewer>().First();
        var viewport = (ScrollContentPresenter)scroll.Template.FindName("PART_ScrollContentPresenter", scroll);
        double bottomFade = scroll.ScrollableHeight - scroll.VerticalOffset > 0.5 ? 16 : 0;
        foreach (var button in new[] { controls.LibraryButton, controls.SourceButton })
        {
            var bounds = button.TransformToAncestor(viewport).TransformBounds(new Rect(button.RenderSize));
            var center = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), island);
            var hit = island.InputHitTest(center) as Visual;
            check(button.IsVisible && button.IsEnabled && bounds.Top >= -0.5 &&
                bounds.Left >= -0.5 && bounds.Right <= viewport.ActualWidth + 0.5 &&
                bounds.Bottom <= viewport.ActualHeight - bottomFade + 0.5 &&
                (hit == button || hit?.IsDescendantOf(button) == true),
                $"{AutomationProperties.GetName(button)} is in the visible viewport {scenario} " +
                $"(bottom {bounds.Bottom:0.#}, viewport {viewport.ActualHeight:0.#}, fade {bottomFade:0.#})");
        }
    }

    private static async Task CheckShortMusicPanel(App app, OverlayWindow island, CancellationToken token, Action<bool, string> check)
    {
        var saved = IslandPreferences.Read();
        var scale = app.Settings.PanelScale;
        var compact = app.Settings.CompactLayout;
        async Task Apply(IslandPreferences appearance)
        {
            IslandPreferences.Save(appearance);
            for (int i = 0; i < 30 && app.Settings.PanelMaxHeight != appearance.PanelHeight; i++)
                await Task.Delay(100, token);
            if (app.Settings.PanelMaxHeight != appearance.PanelHeight)
                throw new InvalidOperationException("The test island did not apply the saved height.");
        }

        try
        {
            await Apply(saved with { Advanced = true, PanelHeight = 270, PanelWidth = 320 });
            app.Settings.PanelScale = 1.25;
            app.ChangeSettings();
            island.SelectPage("media", false);
            await Task.Delay(350, token);
            CheckMusicActionsInView(island, check, "at minimum size with 125% content scale");
            app.Settings.CompactLayout = false;
            app.ChangeSettings();
            island.Collapse();
            island.Reveal(false);
            await Task.Delay(350, token);
            CheckMusicActionsInView(island, check, "in the classic panel at minimum size");
        }
        finally
        {
            app.Settings.CompactLayout = compact;
            app.Settings.PanelScale = scale;
            app.ChangeSettings();
            await Apply(saved);
            island.SelectPage("media", false);
            await Task.Delay(350, token);
        }
    }

    private static async Task CheckLists(App app, OverlayWindow island, string directory, CancellationToken token, Action<bool,string> check)
    {
        void Click(Button control) => control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,control));
        Button ButtonById(Window window,string id) => Diagnostics.Descendants(window).OfType<Button>()
            .Single(button => AutomationProperties.GetAutomationId(button) == id);
        async Task SelectTrack(ListBox list,string name)
        {
            for (int i = 0; i < 40; i++)
            {
                var match = list.Items.OfType<LocalMusicTrack>().FirstOrDefault(item => item.Name == name);
                if (match is not null) { list.SelectedItem = match; return; }
                await Task.Delay(60,token);
            }
            throw new InvalidOperationException("Track missing from actual music list: " + name);
        }

        var controls = Diagnostics.Descendants(island).OfType<MediaControls>().Single();
        check(controls.LibraryButton.IsVisible, "a dedicated track-list button is visible");
        app.MusicLists.SelectList(LocalMusicList.Folder);
        Click(controls.LibraryButton);
        await Task.Delay(150,token);
        var window = island.OwnedWindows.OfType<LocalMusicWindow>().Single();
        window.UpdateLayout();
        check(window.Title == Localization.Text("Music folder"), "dedicated button opens the selected folder view");
        var list = Diagnostics.Descendants(window).OfType<ListBox>().Single();
        await SelectTrack(list,"External");
        Click(ButtonById(window,"Add to favorites"));
        Click(ButtonById(window,"Music made with LOPATA"));
        await SelectTrack(list,"ThisPC");
        Click(ButtonById(window,"Add to favorites"));
        Click(ButtonById(window,"Favorite tracks"));
        await SelectTrack(list,"ThisPC");
        check(list.Items.OfType<LocalMusicTrack>().Select(track => track.Source).Distinct().Count()==2 &&
            list.Items.Count==2,"favorites combine tracks from the folder and LOPATA collection");
        Capture(window,Path.Combine(directory,"favorite-tracks.png"));

        Click(ButtonById(window,"Play selected track"));
        for(int i=0;i<35 && island.OwnedWindows.OfType<LocalMusicWindow>().Any();i++) await Task.Delay(75,token);
        var current = ((ILocalMusicTransport)app.Media).CurrentMusic;
        check(current.Track?.Name=="ThisPC" && current.Playing,"favorites use the actual local player");
        check(new LocalMusicLists(app.Store).Favorites.Count==2 &&
            new LocalMusicLists(app.Store).Selected==LocalMusicList.Favorites, "playlist selection and favorites can be read back from disk");

        Click(controls.LibraryButton);
        await Task.Delay(120,token);
        window=island.OwnedWindows.OfType<LocalMusicWindow>().Single();
        check(window.Title==Localization.Text("Favorite tracks"),"list button reopens the previously selected favorites");
        list=Diagnostics.Descendants(window).OfType<ListBox>().Single();
        await SelectTrack(list,"ThisPC");
        Click(ButtonById(window,"Remove from favorites"));
        check(list.Items.Count==1 && new LocalMusicLists(app.Store).Favorites.Single().Name=="External",
            "removing one favorite keeps the other source intact");
        window.Close();
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var view = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
        view.Render(window);
        BitmapSource bitmap = view;
        if (window is OverlayWindow island)
        {
            int width = Math.Min(view.PixelWidth, (int)Math.Ceiling(island.PaintedWidth));
            int height = Math.Min(view.PixelHeight, (int)Math.Ceiling(island.PaintedHeight));
            bitmap = new CroppedBitmap(view,new Int32Rect((view.PixelWidth-width)/2,0,width,height));
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
