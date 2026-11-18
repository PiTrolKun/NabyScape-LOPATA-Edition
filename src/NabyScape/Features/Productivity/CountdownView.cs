using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace NabyScape;

// A single state-driven action prevents accidentally restarting a running countdown.
// Uses the shared heartbeat only while visible; no private timer or render loop.
internal sealed class CountdownView : StackPanel
{
    private readonly App app;
    private readonly TextBlock digits=UI.Caption("",29), status=UI.Caption("Ready",10,UI.Muted), hint=UI.Caption("",10,UI.Muted), durationLabel=UI.Caption("",10,UI.Muted);
    private readonly ProgressRing progress=new();
    private readonly TextBox minutes;
    private readonly Button primary, reset;
    private bool syncing, subscribed;
    private Guid shownRun;
    private double shownDuration=-1;
    private Countdown State=>app.Session.Timer;
    internal int Refreshes {get;private set;}
    internal bool Subscribed=>subscribed;

    internal CountdownView(App app,bool heading=true,Func<FrameworkElement,FrameworkElement>? decorateHeading=null)
    {
        this.app=app;
        var header=new DockPanel{LastChildFill=true,Margin=new(0,0,0,9)};
        status.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(status,Dock.Right);header.Children.Add(status);
        if(heading){var title=UI.Heading(Localization.Text("Timer"),13);title.FontWeight=FontWeights.SemiBold;header.Children.Add(decorateHeading?.Invoke(title)??title);}Children.Add(header);
        Typography.SetNumeralAlignment(digits,FontNumeralAlignment.Tabular);digits.FontWeight=FontWeights.Medium;digits.TextWrapping=TextWrapping.NoWrap;digits.HorizontalAlignment=HorizontalAlignment.Center;
        var instrument=new Grid{Margin=new(0,4,0,14)};instrument.Children.Add(progress);
        var center=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,IsHitTestVisible=false};center.Children.Add(digits);durationLabel.HorizontalAlignment=HorizontalAlignment.Center;durationLabel.Margin=new(0,4,0,0);center.Children.Add(durationLabel);instrument.Children.Add(center);Children.Add(instrument);
        minutes=UI.Input(Localization.Text("Timer duration in minutes"),"5",Localization.Text("0.1–1440 minutes. Reset the timer to change its duration."));
        var fields=new Grid();fields.ColumnDefinitions.Add(new());
        fields.Children.Add(Field("Duration · min",minutes));
        UI.Named(progress,Localization.Text("Timer progress"));
        primary=UI.CaptionButton("Start",Activate,true);AutomationProperties.SetAutomationId(primary,"timer-primary");
        reset=UI.CaptionButton("Reset",()=>{State.Reset();app.SaveSession();});UI.Named(reset,Localization.Text("Reset timer"));AutomationProperties.SetAutomationId(reset,"timer-reset");
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,0,0,18)};
        foreach(var button in new[]{primary,reset}){button.Width=button.Height=36;button.MinHeight=36;button.Margin=new(0);button.Padding=new(9);button.Tag=new CornerRadius(18);button.ToolTip=Localization.Text("Tab to select; Space or Enter to activate");}
        primary.Margin=new(0,0,12,0);reset.Background=Brushes.Transparent;reset.Foreground=UI.Muted;reset.BorderThickness=new(1);reset.BorderBrush=UI.Brush("#3C4048");reset.Content=Icons.Create("reset",17,UI.Muted);actions.Children.Add(primary);actions.Children.Add(reset);Children.Add(actions);
        Children.Add(fields);hint.MinHeight=20;hint.Margin=new(0,10,0,0);hint.VerticalAlignment=VerticalAlignment.Top;Children.Add(hint);
        minutes.TextChanged+=(_,_)=>{if(!syncing)Refresh();};
        minutes.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter)e.Handled=true;};
        Loaded+=(_,_)=>Subscribe();Unloaded+=(_,_)=>Unsubscribe();
        IsVisibleChanged+=(_,_)=>{if(IsVisible)Subscribe();else Unsubscribe();};
        Refresh();
    }

    private static StackPanel Field(string label,TextBox input)
    {
        var panel=new StackPanel();var text=UI.Caption(label,10,UI.Muted);text.Margin=new(0,0,0,7);panel.Children.Add(text);
        input.MinHeight=32;input.MinHeight=32;input.FontSize=12;input.MaxLength=8;input.Padding=new(0,5,0,5);input.Margin=new(0);input.BorderThickness=new(0);input.Background=Brushes.Transparent;input.HorizontalContentAlignment=HorizontalAlignment.Center;
        input.GotKeyboardFocus+=(_,_)=>input.SelectAll();
        var controls=new Grid();controls.ColumnDefinitions.Add(new(){Width=new GridLength(28)});controls.ColumnDefinitions.Add(new());controls.ColumnDefinitions.Add(new(){Width=new GridLength(28)});
        Grid.SetColumn(input,1);controls.Children.Add(input);
        void Step(int direction){if(double.TryParse(input.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double value)){input.Text=Math.Clamp(value+direction,.1,1440).ToString("0.###",CultureInfo.CurrentCulture);}}
        foreach(int direction in new[]{-1,1})
        {
            var button=Icons.Button(direction<0?"minus":"plus",(direction<0?"Decrease ":"Increase ")+label,()=>Step(direction),false,28);button.Content=Icons.Create(direction<0?"minus":"plus",12,UI.Muted);button.Margin=new(0);button.Padding=new(7);button.Background=Brushes.Transparent;
            button.SetBinding(IsEnabledProperty,new System.Windows.Data.Binding("IsEnabled"){Source=input});Grid.SetColumn(button,direction<0?0:2);controls.Children.Add(button);
        }
        panel.Children.Add(new Border{Background=UI.Brush("#14161A"),BorderBrush=UI.Brush("#30343B"),BorderThickness=new(.7),CornerRadius=new(8),Child=controls});return panel;
    }

    private void Subscribe(){if(!IsLoaded || !IsVisible || subscribed)return;app.Updated+=OnUpdate;subscribed=true;Refresh();}
    private void Unsubscribe(){if(!subscribed)return;app.Updated-=OnUpdate;subscribed=false;}
    private void OnUpdate(){if((app.CurrentUpdate & (RuntimeChange.Countdown|RuntimeChange.Settings))!=0)Refresh();}
    private bool Valid(out double seconds)
    {
        seconds = 0;
        return (double.TryParse(minutes.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
            || double.TryParse(minutes.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            && double.IsFinite(value) && value >= .1 && value <= 1440 && (seconds = value * 60) > 0;
    }

    private void Activate()
    {
        var now=DateTimeOffset.UtcNow;
        if(State.Running)State.Pause(now);
        else if(State.Remaining(now)>0)State.Resume(now);
        else
        {
            if(!Valid(out double seconds)){Refresh();minutes.Focus();return;}
            State.Start(seconds,now);
        }
        app.SaveSession();Refresh();
    }
    private void Refresh()
    {
        if(Window.GetWindow(this)?.WindowState==WindowState.Minimized)return;
        Refreshes++;var now=DateTimeOffset.UtcNow;var state=State;double remaining=state.Remaining(now);bool active=remaining>0, completed=!active && state.DurationSeconds>0;
        if(state.DurationSeconds>0 && (shownRun!=state.RunId || shownDuration!=state.DurationSeconds))
        {
            syncing=true;minutes.Text=(state.DurationSeconds/60).ToString("0.###",CultureInfo.CurrentCulture);syncing=false;
            shownRun=state.RunId;shownDuration=state.DurationSeconds;
        }
        bool valid=Valid(out double seconds);
        digits.Text=active || completed?state.Display(now):Countdown.FormatSeconds(valid?seconds:0);
        Accessibility.Announce(status,Localization.Text(active?(state.Running?"Running":"Paused"):completed?"Complete":"Ready"));
        string action=state.Running?"Pause":active?"Resume":"Start";
        if(AutomationProperties.GetItemStatus(primary)!=action){primary.Content=Icons.Create(state.Running?"pause":"play",17,Brushes.Black);AutomationProperties.SetItemStatus(primary,action);}
        AutomationProperties.SetName(primary,Localization.Text(state.Running?"Pause timer":active?"Resume timer":"Start"));
        primary.IsEnabled=active || valid;reset.IsEnabled=active || completed;
        minutes.IsEnabled=!active;
        progress.Value=active || completed?state.Progress(now):0;
        string duration = active || completed ? (state.DurationSeconds / 60).ToString("0.#", Localization.Culture) : minutes.Text;
        durationLabel.Text = Localization.Format("of {0} min", duration);
        hint.Text=!active && !valid?Localization.Text("Enter 0.1–1440 minutes."):completed?Localization.Text("Time is up."):"";
        hint.Foreground=!active && !valid?UI.Brush("#F0B8A8"):UI.Muted;
    }
}
