using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Media;
using Forms=System.Windows.Forms;

namespace NabyScape;

public sealed partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? openEvent,exitEvent;
    private RegisteredWaitHandle? openWait,exitWait;
    private Forms.NotifyIcon? tray;
    private DispatcherTimer? tick;
    private SettingsWindow? settingsWindow;
    private OverlayWindow? overlay;
    private bool quitting;
    internal InternalNotifications Notifications {get;}=new();
    internal ExternalNotificationWatcher? ExternalNotifications {get;private set;}
    internal LyricsService Lyrics {get;}=new();
    internal WeatherService Weather {get;private set;}=null!;
    internal CountdownAudio CountdownAudio {get;}=new();
    internal DownloadArrivals? Downloads {get;private set;}
    private bool diagnosticMode;
    internal bool DiagnosticMode => diagnosticMode;
    private string downloadConfiguration="";
    private int downloadEpoch;
    private void DownloadReady(string[] paths)
    {
        int epoch=downloadEpoch;Dispatcher.BeginInvoke(()=>
        {
            if(epoch!=downloadEpoch || !Settings.DownloadsEnabled || Downloads?.Running!=true || paths.Length==0 || !string.Equals(Path.GetDirectoryName(paths[0]),Downloads.Folder,StringComparison.OrdinalIgnoreCase))return;
            PostNotice(new("downloads",paths.Length==1?"Download ready":Localization.Format("{0} downloads ready", paths.Length),"Folder arrival inferred · drag originals or add to shelf",paths));
        });
    }
    private void DownloadFailed(string error)
    {
        int epoch=downloadEpoch;Dispatcher.BeginInvoke(()=>
        {
            if(epoch==downloadEpoch && Settings.DownloadsEnabled)Notify("Downloads unavailable",error);
        });
    }
    internal void OpenNotifications()=>overlay?.OpenNotificationCenter();
    // Shows exactly how an alert appears on the dock and in the notification center.
    internal void SendTestNotification()
    {
        Notifications.Post(Settings,new("test",Localization.Text("Test notification"),Localization.Text("This is how alerts appear on your dock. Click it, or let it fold away on its own."),null,"NabyScape"),overlay?.ManuallyHidden==true,n=>tray?.ShowBalloonTip(4500,n.DisplayTitle,n.DisplayDetail,Forms.ToolTipIcon.Info),DateTimeOffset.UtcNow);
    }
    private void ConfigureDownloads()
    {
        if(diagnosticMode)return;
        string key=Settings.DownloadsEnabled+"|"+Settings.DownloadsFolder;if(key==downloadConfiguration)return;downloadConfiguration=key;
        Downloads?.Configure(false,"");downloadEpoch++;
        try{Downloads?.Configure(Settings.DownloadsEnabled,Settings.DownloadsEnabled?(Settings.DownloadsFolder.Length==0?DownloadArrivals.KnownFolder():Settings.DownloadsFolder):"");}
        catch(Exception ex){Notify("Downloads unavailable",ex.Message);}
    }
    internal void PostNotice(InternalNotice notice)
    {
        if(diagnosticMode && !Array.Exists(Environment.GetCommandLineArgs(),a=>a=="--stage4-only"))return;
        Notifications.Post(Settings,notice,overlay?.ManuallyHidden==true,n=>tray?.ShowBalloonTip(4500,n.DisplayTitle,n.DisplayDetail,Forms.ToolTipIcon.Info),DateTimeOffset.UtcNow);
    }
    public Store Store { get; private set; }=null!;
    public Settings Settings { get; private set; }=null!;
    public SessionState Session { get; private set; }=null!;
    public MediaService Media { get; private set; }=null!;
    public BrightnessService Brightness { get; private set; }=null!;
    public string BrightnessStatus=>Brightness?.Status??"Brightness feedback unavailable";
    public AudioService Audio { get; private set; }=null!;
    internal AmbientAudioDriver AmbientAudio {get;private set;}=null!;
    public ProductivityService Productivity {get;private set;}=null!;
    internal DetachedPreviewManager Previews {get;private set;}=null!;
    public CalendarService Calendar {get;private set;}=null!;
    public KeepAwakeService Awake {get;private set;}=null!;
    public string HotkeyStatus=>!Settings.HotkeyEnabled?"Hotkey off":overlay?.HotkeyError.Length>0?overlay.HotkeyError:$"Active · {Settings.HotkeyModifiers}+{Settings.HotkeyKey}";
    public string VisibilityHotkeyStatus=>!Settings.VisibilityHotkeyEnabled?"Visibility shortcut off":overlay?.VisibilityHotkeyError.Length>0?overlay.VisibilityHotkeyError:$"Hide / show · {Settings.VisibilityHotkeyModifiers}+{Settings.VisibilityHotkeyKey}";
    public string TuckHotkeyStatus=>!Settings.TuckHotkeyEnabled?"Tuck shortcut off":overlay?.TuckHotkeyError.Length>0?overlay.TuckHotkeyError:$"Tuck / return to automatic · {Settings.TuckHotkeyModifiers}+{Settings.TuckHotkeyKey}";
    internal void FlushPreferenceEditors()=>overlay?.FlushPreferenceEditors();
    // Countdown completions route through the shared notification manager.
    internal bool UsesLopataEdition { get; private set; }
    internal bool SupportsWidget(string id) => id is not ("voice" or "focus");
    public event Action? SettingsChanged;
    public event Action? Updated;
    internal RuntimeChange CurrentUpdate {get;private set;}=RuntimeChange.All;
    internal void PublishUpdate(RuntimeChange change){var previous=CurrentUpdate;CurrentUpdate=change;try{Updated?.Invoke();}finally{CurrentUpdate=previous;}}
    public (double cpu,double used,double total) Usage { get; private set; }
    public static new App Current=>(App)Application.Current;
    [STAThread] public static void Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--lyrics-romaji-worker"){LyricsRomajiWorker.Run();return;}
        var culture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture=culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=culture;
        if (!IslandRestart.WaitForPrevious(args)) { Environment.ExitCode = 1; return; }
        if (Array.Exists(args, a => a is "--update-handoff" or "--install" or "--uninstall" or "--widget-mcp")) return;
        if(WindowFixture.TryRun(args))return;
        if(Array.Exists(args,a=>a=="--exit")) {try {using var signal=EventWaitHandle.OpenExisting(LopataEdition.Instance + ".Exit");signal.Set();}catch(WaitHandleCannotBeOpenedException){}return;}
        if(Array.Exists(args,a=>a=="--self-test")) { SelfTests.Run(args);return; }

        new App().Run();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        var startupClock=System.Diagnostics.Stopwatch.StartNew();
        PrivateDiagnostics.SetServiceState(DiagnosticService.Application,DiagnosticServiceState.Starting);
        base.OnStartup(e);
        bool showcaseFixture=Array.Exists(e.Args,a=>a=="--showcase") && Array.Exists(e.Args,a=>a=="--diagnostics");
        bool isolatedFixture=Array.Exists(e.Args,a=>a=="--isolated-diagnostics") && Array.IndexOf(e.Args,"--diagnostics") is int diagnosticAt && diagnosticAt>=0 && diagnosticAt+1<e.Args.Length;
        string instance = LopataEdition.Instance + (isolatedFixture ? ".IsolatedDiagnostics" : showcaseFixture ? ".Showcase" : "");
        mutex=new Mutex(true,instance+".Desktop",out var created);
        if(!created) {try {using var signal=EventWaitHandle.OpenExisting(instance+".OpenSettings");signal.Set();}catch(WaitHandleCannotBeOpenedException){}Shutdown();return; }
        openEvent=new EventWaitHandle(false,EventResetMode.AutoReset,instance+".OpenSettings");
        exitEvent=new EventWaitHandle(false,EventResetMode.AutoReset,instance+".Exit");
        openWait=ThreadPool.RegisterWaitForSingleObject(openEvent,(_,_)=>Dispatcher.BeginInvoke(ShowSettings),null,Timeout.Infinite,false);
        exitWait=ThreadPool.RegisterWaitForSingleObject(exitEvent,(_,_)=>Dispatcher.BeginInvoke(Quit),null,Timeout.Infinite,false);
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/LopataIsland;component/Theme.xaml",UriKind.Relative)});
        var diagnosticIndex=Array.IndexOf(e.Args,"--diagnostics");
        var diagnosticPath=diagnosticIndex>=0 && diagnosticIndex+1<e.Args.Length ? e.Args[diagnosticIndex+1] : null;
        diagnosticMode=diagnosticPath!=null;
        Store=new(diagnosticPath==null ? LopataEdition.ProfileDirectory : Path.Combine(diagnosticPath,"test-profile"));bool existingProfile=File.Exists(Path.Combine(Store.DirectoryPath,"settings.json"));Settings=diagnosticPath==null?Store.Load<Settings>("settings"):new Settings();Settings.Normalize();Localization.Configure(Settings);Settings.AutoStart=StartupService.Enabled();
        if (!diagnosticMode && !existingProfile)
        {
            Settings.ClipboardHistory = false;
            Settings.DownloadsEnabled = false;
            Settings.LyricsEnabled = false;
        }
        bool lopataFixture = isolatedFixture && Array.Exists(e.Args, a => a == "--lopata-edition-only") &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOPATA_ISLAND_PROFILE"));
        bool voiceFixture = Array.Exists(e.Args, a => a == "--lopata-voice-only");
        bool musicFixture = isolatedFixture && (voiceFixture || Array.Exists(e.Args, a => a == "--lopata-music-only")) &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOPATA_ISLAND_PROFILE"));
        bool updatesFixture = isolatedFixture && Array.Exists(e.Args, a => a == "--lopata-updates-only") &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOPATA_ISLAND_PROFILE"));
        bool languageFixture = isolatedFixture && Array.Exists(e.Args, a => a == "--lopata-language-only") &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOPATA_ISLAND_PROFILE"));
        bool restartFixture = isolatedFixture && Array.Exists(e.Args, a => a == "--lopata-restart-only") &&
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOPATA_ISLAND_PROFILE"));
        bool inspectorFixture = isolatedFixture && Array.Exists(e.Args, a => a == "--inspector-advice-only");
        UsesLopataEdition = !diagnosticMode || lopataFixture || musicFixture || updatesFixture || languageFixture || restartFixture;
        if (UsesLopataEdition) PrepareLopataSettings();
        if (UsesLopataEdition && diagnosticPath is null) StartIslandMailHub();
        if (musicFixture || languageFixture || lopataFixture || updatesFixture || restartFixture) Settings.MediaIntegrationEnabled = false;
        if (musicFixture) Settings.LyricsEnabled = false;
        if(isolatedFixture){Settings.HotkeyEnabled=Settings.VisibilityHotkeyEnabled=Settings.TuckHotkeyEnabled=false;Settings.HideFullscreen=Settings.HideMaximized=false;}
        InitializeLopataUpdates();
        var updateDiagnostics = updatesFixture ? new LopataUpdateDiagnostics() : null;
        if (updateDiagnostics is not null)
        {
            LopataUpdates.Dispose();
            LopataUpdates = updateDiagnostics.CreateMonitor(Store.Log);
        }
        ApplyAppearance();
        Motion.Configure(Settings);
        Session=diagnosticPath==null?Store.Load<SessionState>("session"):new SessionState();Session.Timer??=new();Session.Stopwatch??=new();Session.Stopwatch.Recover();
        DispatcherUnhandledException+=(_,args)=> { Store.Log(args.Exception);args.Handled=true;Notify("Something went wrong", "The operation failed. Details were saved to errors.log."); };
        Audio=new(Store.Log){SpectrumEnabled=Settings.WaveformSource=="Spectrum"};Media=new(Store.Log,musicFixture ? Path.Combine(diagnosticPath!,"collection") : null, Store.DirectoryPath);Media.Configure(Settings);Productivity=new(Store,Store.Log,diagnosticPath==null);Productivity.SetClipboardEnabled(Settings.ClipboardHistory);
        AmbientAudio=new(this);Weather=new(Store);
        Brightness=new();Brightness.Start(diagnosticPath==null && Settings.ReactiveFeedback && Settings.BrightnessFeedback);
        Calendar=new(Store);Awake=new();
        tray=new Forms.NotifyIcon{Text=LopataEdition.Name,Visible=diagnosticPath==null};
        using(var stream=GetResourceStream(new Uri("pack://application:,,,/Assets/lopata-island.ico")).Stream) tray.Icon=new System.Drawing.Icon(stream);
        var menu=new Forms.ContextMenuStrip();
        Forms.ToolStripItem AddTray(string caption, Action action, Forms.ToolStripItemCollection? items = null)
        {
            var item = (items ?? menu.Items).Add(Localization.Text(caption), null, (_, _) => Dispatcher.Invoke(action));
            item.Tag = caption;
            return item;
        }
        AddTray("Open island", () => overlay?.Reveal());
        AddTray("Command bar", () => overlay?.ShowCommands());
        AddTray("Notifications", OpenNotifications);
        AddTray(InspectorAdvicePage.Title, () => overlay?.OpenInspectorAdvice());
        AddTray("Send test notification", SendTestNotification);
        AddTray("Settings", ShowSettings);
        AddTray(UsesLopataEdition ? "Move island" : "Move & resize island", () => overlay?.ToggleDockEditing());
        AddTray("LOPATA chat", ShowLopataChat);
        AddTray("LOPATA updates", ShowLopataUpdates);
        AddTray("Tuck / Return to automatic", () => overlay?.ToggleTuck());
        AddTray("Hide / show", () => overlay?.ToggleVisibility());
        menu.Items.Add(new Forms.ToolStripSeparator());
        var support = new Forms.ToolStripMenuItem(Localization.Text(SupportLinks.DonationHeading)) { Tag = SupportLinks.DonationHeading };
        AddTray("Donate with Saweria", () => SupportLinks.Open(SupportLinks.Saweria, Store.Log), support.DropDownItems);
        AddTray("Donate with Ko-fi", () => SupportLinks.Open(SupportLinks.KoFi, Store.Log), support.DropDownItems);
        menu.Items.Add(support);
        AddTray(LopataEdition.OpenCaption, LopataEdition.OpenHost);
        AddTray(LopataEdition.LayerCaption, ToggleLopataLayer);
        AddTray(LopataEdition.RestartCaption, RestartIsland);
        AddTray(LopataEdition.CloseCaption, Quit);
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowSettings);
        overlay=new OverlayWindow(this);MainWindow=overlay;StartupTrace.Attach(overlay,e.Args,Store.Log);overlay.Show();Previews=new(this,overlay.OpenFeaturePage);
        if (!diagnosticMode) LopataUpdates.Start();
        Notifications.Changed+=()=>overlay.RefreshNotice();
        Downloads=new(DownloadReady,DownloadFailed);
        ExternalNotifications=new(ex=>Store.Log(ex));
        ExternalNotifications.Arrived+=toast=>{if(ExternalNotificationPolicy.Allowed(Settings,toast.App))PostNotice(new("external",toast.Title,toast.Body,null,toast.App,toast.AppId));};
        void ConfigureExternal()=>ExternalNotifications?.Configure(Settings.ExternalNotifications && !diagnosticMode);
        SettingsChanged+=ConfigureExternal;ConfigureExternal();
        ConfigureDownloads();
        Media.LocalNavigationFailed += message => Notify(Localization.Text("Media"), Localization.Text(message));
        history = new LocalMusicHistory(Store.DirectoryPath, Store.Log);
        history.Prune(DateTimeOffset.Now);
        Media.LocalPlaybackStarted += (track, artist) =>
        {
            if (Settings.MusicListeningHistory) history.Record(track, artist, DateTimeOffset.Now);
        };
        Media.Changed+=()=>{Lyrics.ObserveTrack(Media);PublishUpdate(RuntimeChange.Media);};
        Audio.Changed+=()=>{PrivateDiagnostics.SetServiceState(DiagnosticService.Audio,Audio.Available?DiagnosticServiceState.Ready:DiagnosticServiceState.Degraded);PublishUpdate(RuntimeChange.Audio);};
        Productivity.Changed+=()=>PublishUpdate(RuntimeChange.Productivity);Calendar.Changed+=()=>PublishUpdate(RuntimeChange.Calendar);
        if (lopataFixture || musicFixture) Media.SetEnabled(false);
        else if(isolatedFixture || Array.Exists(e.Args,a=>a is "--performance-benchmark" or "--performance-checks-only" or "--stage1-only" or "--stage2-only" or "--stage4-only" or "--stage5-only" or "--stage6-only" or "--integration-settings-only" or "--interaction-hotfix-only" or "--interaction-tuck-hide-only" or "--interaction-classic-mixer-only") || diagnosticPath!=null && Array.Exists(e.Args,a=>a=="--showcase"))Media.DiagnosticFixture=true;else Media.SetEnabled(MediaFeaturesWanted);
        Audio.Read();Usage=Native.SystemUsage();
        int counter=0;
        tick=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        tick.Tick+=(_,_)=>
        {
            CheckCountdowns();if(NotificationPolicy.Quiet(Settings,DateTimeOffset.UtcNow) && Notifications.Count>0 && Notifications.Current?.Kind!="preview" && !(Notifications.Current?.Kind=="external" && Settings.ExternalNotificationQuietHours))Notifications.Clear();else Notifications.Tick(DateTimeOffset.UtcNow,overlay.NotificationBusy,Settings.NotificationSeconds);
            CheckVoiceMusicStop(System.Diagnostics.Stopwatch.GetTimestamp());
            try{Awake.Tick(DateTimeOffset.UtcNow);}catch(Exception ex){Store.Log(ex);Notify("Could not update keep awake",ex.Message);}
            var changed=RuntimeChange.Clock;
            if(Session.Timer.Running || Session.Stopwatch.Running)changed|=RuntimeChange.Countdown;
            if(++counter%2==0)
            {
                Audio.Poll();Audio.TrimIdleMedia();bool system=overlay.SystemSummaryVisible || Previews.AnyVisible("system") || settingsWindow?.NeedsSystemSample==true;
                if(system || overlay.NeedsAdaptiveCpuSample)Usage=Native.SystemUsage();
                if(system)changed|=RuntimeChange.System;
                overlay.CheckEnvironment();
            }
            if(counter%10==0)Productivity.DisplayThumbnails.TrimIdle();
            WeatherTick();
            PublishUpdate(changed);
        };
        tick.Start();CheckCountdowns();
        if(Store.Warning!=null) Notify("Settings recovered",Store.Warning);
        if(!Array.Exists(e.Args,a=>a=="--background")) Notify("NabyScape is ready", "Click the island for your widgets. Right-click the tray icon for settings.");
        InitializeRuntimeRecovery();
        PrivateDiagnostics.SetServiceState(DiagnosticService.Application,DiagnosticServiceState.Ready);
        PrivateDiagnostics.Record(DiagnosticStage.Application,DiagnosticCode.Completed,startupClock.Elapsed);
        if (!diagnosticMode || lopataFixture || languageFixture || musicFixture && !voiceFixture) ObserveLopataSettings();
        if(musicFixture && voiceFixture) _=IslandVoicePlaybackDiagnostics.RunAsync(this, overlay, diagnosticPath!);
        else if(musicFixture) _=LopataMusicDiagnostics.Run(this,overlay,diagnosticPath!);
        else if(lopataFixture) _=LopataEditionDiagnostics.Run(this,overlay,diagnosticPath!);
        else if(updateDiagnostics is not null) _=updateDiagnostics.Run(this,overlay,diagnosticPath!);
        else if(languageFixture) _=LopataLanguageDiagnostics.Run(this,diagnosticPath!);
        else if(restartFixture) _=RunIslandRestartDiagnostics(overlay, diagnosticPath!, e.Args);
        else if(inspectorFixture) _=InspectorAdviceDiagnostics.Run(this, overlay, diagnosticPath!);
        else if(diagnosticPath!=null) _=Diagnostics.Run(this,overlay,diagnosticPath);
        else
        {
            bool offerWelcome=false;
            try{offerWelcome=WelcomeSetup.ShouldOffer(Store,existingProfile);}catch(Exception ex){Store.Log(ex);}
            if(offerWelcome)ShowWelcome();
            else if(Array.Exists(e.Args,a=>a=="--settings"))ShowSettings();
        }
    }
    public void CheckCountdowns()
    {
        var now=DateTimeOffset.UtcNow;bool changed=false;
        if(Session.Timer.Complete(now)) { changed=true;PostNotice(new("timer","Timer finished","Your timer has finished."));if(Settings.NotifyTimer && !NotificationPolicy.Quiet(Settings,now))CountdownAudio.Play(Settings); }
        if(changed) SaveSession();
    }
    internal void OpenMixer()=>overlay?.OpenMixer();
    internal void OpenClipboardHistory()=>overlay?.ShowTools("clips");
    public void SaveSession() { try { Session.Stopwatch.Checkpoint();Store.Save("session",Session); } catch(Exception ex) { Store.Log(ex);Notify("Not saved yet", "Could not save the timer state."); } PublishUpdate(RuntimeChange.Countdown); }
    internal bool SettingsSaveSucceeded {get;private set;}
    public void ChangeSettings()
    {
        if (UsesLopataEdition) LopataEdition.ApplyAppearance(Settings, lopataAppearance);
        Settings.Normalize();Media.Configure(Settings);Media.SetEnabled(MediaFeaturesWanted);Brightness.Start(!diagnosticMode && Settings.ReactiveFeedback && Settings.BrightnessFeedback);Audio.SpectrumEnabled=Settings.WaveformSource=="Spectrum";ApplyAppearance();Motion.Configure(Settings);
        Productivity?.SetClipboardEnabled(Settings.ClipboardHistory);
        SettingsSaveSucceeded=false;
        try { Store.Save("settings",Settings);SettingsSaveSucceeded=true; } catch(Exception ex) { Store.Log(ex);Notify("Not saved yet","These settings will apply until you close the app."); }
        ConfigureDownloads();if(Settings.NotificationDelivery is "Off" or "Windows" || NotificationPolicy.Quiet(Settings,DateTimeOffset.UtcNow))Notifications.Clear();
        Previews?.ApplySettings();SettingsChanged?.Invoke();PublishUpdate(RuntimeChange.Settings);
    }
    internal void ShowSettings(string category){ShowSettings();settingsWindow?.OpenCategory(category);}
    public void ShowSettings()
    {
        overlay?.Collapse();
        if(settingsWindow==null) { settingsWindow=new SettingsWindow(this);settingsWindow.Closed+=(_,_)=>settingsWindow=null; }
        if(diagnosticMode)settingsWindow.ShowActivated=false;
        settingsWindow.Show();if(!diagnosticMode)settingsWindow.Activate();
    }
    public void Notify(string title,string text)
    { PostNotice(new("failure",title,text)); }
    private bool restarting;
    internal void RestartIsland()
    {
        if (quitting || restarting) return;
        restarting = true;
        try
        {
            FlushPreferenceEditors();
            var executable = Environment.ProcessPath ?? throw new IOException("The running island executable is unavailable.");
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            using var replacement = IslandRestart.StartReplacement(executable, current, restartDiagnosticArguments);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            restarting = false;
            Store.Log(error);
            MessageBox.Show(Localization.Text("Could not restart the island."), LopataEdition.Name);
            return;
        }
        Quit();
    }

    public void Quit()
    { quitting=true;Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        StopIslandMailHub();
        StopIslandVoice();
        DisposeRuntimeRecovery();
        welcomeWindow?.Close();tick?.Stop();Weather?.Dispose();AmbientAudio?.Dispose();Lyrics.Dispose();CountdownAudio.Dispose();Downloads?.Dispose();ExternalNotifications?.Dispose();
        if(Session!=null) SaveSession();
        Previews?.Dispose();overlay?.Cleanup();try{Awake?.Dispose();}catch(Exception ex){Store?.Log(ex);}Productivity?.Dispose();Media?.Dispose();Audio?.Dispose();Brightness?.Dispose();
        if(tray!=null) { tray.Visible=false;tray.Dispose(); }
        hostSettingsWatcher?.Stop();
        LopataLink.Dispose();lopataChat?.Close();
        lopataUpdatesWindow?.Close();LopataUpdates?.Dispose();lopataUpdateHttp?.Dispose();
        openWait?.Unregister(null);exitWait?.Unregister(null);openEvent?.Dispose();exitEvent?.Dispose();mutex?.Dispose();base.OnExit(e);
    }
    public bool Quitting=>quitting;
}
