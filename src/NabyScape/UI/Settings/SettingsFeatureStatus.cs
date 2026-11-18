using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
namespace NabyScape;
public sealed partial class SettingsWindow
{
    private readonly List<Action> homeStatusUpdates=new();
    private void InitializeHomeStatuses()
    {
        app.Updated+=HomeStatusChanged;app.Lyrics.Changed+=RefreshHomeStatuses;
        Closed+=(_,_)=>{app.Updated-=HomeStatusChanged;app.Lyrics.Changed-=RefreshHomeStatuses;homeStatusUpdates.Clear();};
    }
    private void HomeStatusChanged(){if((app.CurrentUpdate&(RuntimeChange.Media|RuntimeChange.Settings))!=0)RefreshHomeStatuses();}
    private void RefreshHomeStatuses()
    {
        if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(RefreshHomeStatuses));return;}
        if(current!="Home" || !IsVisible)return;foreach(var update in homeStatusUpdates.ToArray())update();
    }
    private void HomeFeatureStatuses()
    {
        homeStatusUpdates.Clear();var group=new StackPanel();
        void Row(string title,Func<(string State,string Detail,string Action)> assess,Action open)
        {
            var row=new DockPanel{Margin=new(0,0,0,16)};var action=UI.CaptionButton("Open",open);action.MinWidth=116;action.VerticalAlignment=VerticalAlignment.Center;action.Margin=new(16,0,0,0);DockPanel.SetDock(action,Dock.Right);row.Children.Add(action);
            var text=new StackPanel();var state=UI.Text("",14);state.FontWeight=FontWeights.SemiBold;text.Children.Add(state);var detail=UI.Text("",12,UI.Muted);detail.Margin=new(0,5,0,0);text.Children.Add(detail);row.Children.Add(text);group.Children.Add(row);
            void Update(){var value=assess();Motion.Text(state,Localization.Text(title)+" · "+Localization.Text(value.State));Motion.Text(detail,Localization.Text(value.Detail));action.Content=Localization.Text(value.Action);UI.LocalizedName(action,value.Action);}
            homeStatusUpdates.Add(Update);Update();
        }
        Row("Music",()=>!app.Settings.MediaIntegrationEnabled?("Off","Music apps are disconnected. Playback in your music app is unaffected.","Enable music"):
            !app.Settings.Widgets.Any(w=>w.Enabled && w.Id=="media")?("Hidden","Enable the music widget to show playback controls.","Choose widgets"):
            !app.Media.Available?("Waiting for music","Play a song in your music app, then select its media session.","Music settings"):("Ready","Playback controls use the selected music session.","Music settings"),()=>
            {if(!app.Settings.MediaIntegrationEnabled){app.Settings.MediaIntegrationEnabled=true;app.ChangeSettings();}else Navigate(app.Settings.Widgets.Any(w=>w.Enabled && w.Id=="media")?"Audio":"Content");});
        Row("LOPATA chat", () => ("Local core", "Talk to LOPATA through its installed local core.", "Open chat"), app.ShowLopataChat);
        Row("Lyrics",()=>!app.Settings.LyricsEnabled && app.Settings.ClosedMediaContent!="Lyrics"?("Off","Enable panel lyrics or choose Live lyrics for the closed dock.","Choose lyrics"):
            !app.Media.Available?("Waiting for music","Play a song to look up timed lyrics.","Music settings"):
            !LyricTiming.CanUse(app.Media)?("Unavailable","This player has not provided enough track details or playback timing.","Music settings"):
            app.Lyrics.Result.Lines.Length>0?("Ready","Timed lyrics are available for the selected track.","Lyrics settings"):
            app.Lyrics.Result.Status.StartsWith("Finding",StringComparison.Ordinal)?("Finding lyrics","Looking up timed lyrics for this song.","Lyrics settings"):
            app.Lyrics.Result.Status.StartsWith("No synced",StringComparison.Ordinal) || app.Lyrics.Result.Status.StartsWith("Instrumental",StringComparison.Ordinal)?("Unavailable for this song","No timed lyrics were found. The closed dock uses the song title.","Lyrics settings"):
            app.Lyrics.Result.Status.Contains("unavailable",StringComparison.OrdinalIgnoreCase) || app.Lyrics.Result.Status.Contains("timed out",StringComparison.OrdinalIgnoreCase)?("Needs attention","Lyrics lookup could not complete. Open the lyrics panel to retry.","Lyrics settings"):
            ("Waiting for lyrics","Show the lyrics view to look up this song. Some songs have no timed lyrics.","Lyrics settings"),()=>Navigate("Audio"));
        Row("Weather",()=>WeatherService.Key(app.Settings).Length==0?("Needs a city","Choose your city to see the forecast.","Choose city"):
            app.Weather.Cached?.Key==WeatherService.Key(app.Settings)?("Forecast saved","Open Weather to view the forecast and its update time.","Open weather"):
            ("Needs a forecast","Your city is selected. Open Weather to load its forecast.","Open weather"),()=>Navigate("Weather"));
        page.Children.Add(Section("Your setup",group));
    }
}
