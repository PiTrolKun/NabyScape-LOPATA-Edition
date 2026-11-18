using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private void MediaExperienceSettings()
    {
        var s=app.Settings;var mode=new ComboBox{ItemsSource=new[]{"Frequency spectrum","Amplitude history"}.Select(Localization.Text).ToArray(),SelectedIndex=s.WaveformSource=="Spectrum"?0:1};UI.Named(mode,Localization.Text("Visualizer input"));
        mode.SelectionChanged+=(_,_)=>{s.WaveformSource=mode.SelectedIndex==0?"Spectrum":"Envelope";Save();};
        var sensitivity=new StackPanel();AddSlider(sensitivity,"Visualizer sensitivity",s.WaveformSensitivity,.5,3,v=>s.WaveformSensitivity=v,"×",.05);
        page.Children.Add(Section("Visualizer",mode,sensitivity,UI.Text(Localization.Text("Spectrum separates real bass, midrange and treble from the selected media app's active output. Other apps sharing that output can influence the bands. Muting the selected app silences the display. Output samples stay in memory; microphone capture is never used. Amplitude history remains the fallback for unsupported output formats."),11,UI.Muted)));
        var presets=new ComboBox{ItemsSource=new[]{"Slide · flowing lines","Focus · soft emphasis","Karaoke · line sweep"}.Select(Localization.Text).ToArray(),SelectedIndex=Array.IndexOf(new[]{"Slide","Focus","Karaoke"},s.LyricsAnimation)};UI.Named(presets,Localization.Text("Lyrics animation preset"));presets.SelectionChanged+=(_,_)=>{if(presets.SelectedIndex<0)return;s.LyricsAnimation=new[]{"Slide","Focus","Karaoke"}[presets.SelectedIndex];Save();};
        var timing=new StackPanel();AddSlider(timing,"Lyrics timing offset",s.LyricsOffset,-10,10,v=>s.LyricsOffset=v," s",.1);var local=UI.Text(Localization.Text("Local LRC files can supply timings when the service is unavailable. Imports last for this app session."),11,UI.Muted);timing.Children.Add(UI.Button(Localization.Text("Load local timed lyrics…"),()=>local.Text=Localization.Text(LyricsFiles.Import(app,this))));timing.Children.Add(local);
        var textMode=new ComboBox{ItemsSource=new[]{"Original script","Japanese Romaji · Latin letters"}.Select(Localization.Text).ToArray(),SelectedIndex=s.LyricsTextMode=="Romaji"?1:0};UI.Named(textMode,Localization.Text("Lyrics writing system"));
        textMode.SelectionChanged+=(_,_)=>{s.LyricsTextMode=textMode.SelectedIndex==1?"Romaji":"Original";app.Lyrics.SetTextMode(s.LyricsTextMode);Save();};
        timing.Children.Insert(0,UI.Text(Localization.Text("Lyric text"),11,UI.Muted));timing.Children.Insert(1,textMode);
        page.Children.Add(Section("Lyrics",LyricsAnimationExample(),Check("Show synced lyrics in the media panel",s.LyricsEnabled,v=>s.LyricsEnabled=v),UI.Caption(app.UsesLopataEdition?"Tracks made in LOPATA use their saved lyrics. Online lookup is available for other tracks. You can also load a local LRC file.":"Automatic finds timed lyrics across public catalogues using the current title, artist and duration. Choose a source from the Lyrics menu. Romaji is generated offline; availability varies by track.",11,UI.Muted),presets,timing,UI.Text(Localization.Text("The lyrics icon beside playback controls toggles this preference. Slide follows each new line; Focus softly emphasizes it; Karaoke sweeps across the line using line timing. Reduce Motion keeps the text static. Tab to the lyrics button and press Space to show or hide."),11,UI.Muted)));
    }
    private FrameworkElement LyricsAnimationExample()
    {
        var sample=new LyricResult(new[]{new LyricLine(0,Localization.Text("A little closer to the music")),new LyricLine(4,Localization.Text("One line, right on time")),new LyricLine(8,Localization.Text("Let the next moment arrive"))},Localization.Text("Animation preview · original sample text"));
        var view=new LyricsView(app){Height=124,Margin=new(0,0,0,14)};view.SetFixture(sample,5.5,12);
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};var clock=System.Diagnostics.Stopwatch.StartNew();
        void Sync(){if(ElementVisibility.InViewport(view) && !app.Settings.ReduceMotion && Motion.Enabled)timer.Start();else timer.Stop();view.SetFixture(sample,5.5,12);}
        ScrollChangedEventHandler scrolled=(_,_)=>Sync();EventHandler state=(_,_)=>Sync();
        timer.Tick+=(_,_)=>{if(!ElementVisibility.InViewport(view)){timer.Stop();return;}view.SetFixture(sample,clock.Elapsed.TotalSeconds%12,12);};view.Loaded+=(_,_)=>{app.SettingsChanged+=Sync;settingsScroll.ScrollChanged+=scrolled;StateChanged+=state;Sync();};view.Unloaded+=(_,_)=>{app.SettingsChanged-=Sync;settingsScroll.ScrollChanged-=scrolled;StateChanged-=state;timer.Stop();};view.IsVisibleChanged+=(_,_)=>Sync();view.SizeChanged+=(_,_)=>Sync();return view;
    }
    private void DockEditingSettings()
    {
        if (app.UsesLopataEdition)
        {
            page.Children.Add(Section("Move & resize", new DockPositionEditor(app, Save),
                Check("Show island move handle", app.Settings.DockUnlocked, value => app.Settings.DockUnlocked = value),
                UI.Caption("Drag the top handle to move the island on the current screen. Use the LOPATA settings to change its sizes or screen.", 11, UI.Muted)));
            return;
        }
        page.Children.Add(Section("Move & resize",new DockPositionEditor(app,Save),Check("Show island move and resize handles",app.Settings.DockUnlocked,value=>app.Settings.DockUnlocked=value),UI.Text(Localization.Text("Drag the top grip to reposition the island. Drag either lower corner to resize the current state: closed, hover or open panel. Position and each size are saved separately. Hold Alt while the island is focused to reveal handles temporarily; Esc cancels a drag. Dragging down detaches a notch into a pill; a drag that begins as a notch can snap back to the top edge."),11,UI.Muted)));
    }
    private void CompletionSoundSettings()
    {
        var s=app.Settings;var sounds=UI.CaptionChoice(new[]{"Chime","Bell","Custom","Off"},s.CountdownSound);UI.Named(sounds,Localization.Text(app.UsesLopataEdition?"Timer sound":"Timer and Focus completion sound"));
        var path=UI.Text(s.CountdownSoundFile.Length>0?System.IO.Path.GetFileName(s.CountdownSoundFile):Localization.Text("No custom sound chosen"),11,UI.Muted);
        var volume=new StackPanel();AddSlider(volume,"Completion sound volume",s.CountdownSoundVolume*100,0,100,value=>s.CountdownSoundVolume=value/100,"%");
        sounds.SelectionChanged+=(_,_)=>{s.CountdownSound=sounds.SelectedValue?.ToString()??"Chime";Save();};
        var feedback=UI.Text("",11,UI.Muted);
        var choose=UI.Button(Localization.Text("Choose WAV…"),()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Filter=Localization.Text("Wave audio (*.wav)")+"|*.wav",Title=Localization.Text("Choose a completion sound (up to 15 seconds)")};if(dialog.ShowDialog(this)!=true)return;s.CountdownSoundFile=dialog.FileName;s.CountdownSound="Custom";sounds.SelectedValue="Custom";path.Text=System.IO.Path.GetFileName(dialog.FileName);Save();});
        var soundHelp=app.UsesLopataEdition?"The timer plays this sound through the current Windows output when timer alerts are enabled. Choose Off for silence. Custom WAV files may be up to 15 seconds and must be under 10 MB.":"Timer and Focus play this sound directly when their per-type notification toggle is on. It uses the current Windows output; Off silences it. Custom WAV files must be under 10 MB and at most 15 seconds. File paths are kept locally and excluded from layout exports.";
        page.Children.Add(Section("Completion sound",sounds,volume,path,choose,UI.Button(Localization.Text("Preview sound"),()=>{app.CountdownAudio.Play(s);feedback.Text=app.CountdownAudio.LastError??Localization.Text("Played through your Windows output. Sound does not depend on Windows notifications.");}),feedback,UI.Caption(soundHelp,11,UI.Muted)));
    }
}
