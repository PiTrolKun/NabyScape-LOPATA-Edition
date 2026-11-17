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
            music = await local.TracksAsync(LocalMusicSource.Collection, "", token);
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
            var controls = Diagnostics.Descendants(island).OfType<MediaControls>().Single();
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
            Capture(island,Path.Combine(directory,"local-player.png"));

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
            app.Media.SelectNone();
            Check(local.CurrentMusic.Track is null && !local.CurrentMusic.Playing, "changing source stops local playback");
        }
        catch (Exception error) { report.Add("FAIL "+error); Environment.ExitCode=1; }
        finally
        {
            File.WriteAllLines(Path.Combine(directory,"music-check.txt"),report);
            app.Quit();
        }
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