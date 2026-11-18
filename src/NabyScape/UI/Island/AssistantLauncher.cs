using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private Button AssistantLauncher()
    {
        var orb=new AssistantOrb{Width=26,Height=26,ReduceMotion=app.Settings.ReduceMotion};
        if (app.UsesLopataEdition)
        {
            var mascot = AssistantOrb.Launcher(Localization.Text("LOPATA chat"), app.ShowLopataChat, orb);
            mascot.Width = mascot.MinHeight = 28;
            mascot.ToolTip = Localization.Text("LOPATA chat");
            System.Windows.Automation.AutomationProperties.SetAutomationId(mascot, "Lopata.Chat.Mascot");
            void RefreshMotion() => orb.ReduceMotion = app.Settings.ReduceMotion;
            mascot.Loaded += (_, _) => { app.SettingsChanged += RefreshMotion; RefreshMotion(); };
            mascot.Unloaded += (_, _) => app.SettingsChanged -= RefreshMotion;
            return mascot;
        }
        var button=AssistantOrb.Launcher("AI companion",app.ShowAssistant,orb);button.Width=28;button.MinHeight=28;button.ToolTip=Localization.Text("Open AI companion");
        void Refresh()
        {button.Visibility=app.Assistant.Options.Enabled?Visibility.Visible:Visibility.Collapsed;orb.State=app.Assistant.WakeListening?AssistantState.Listening:app.Assistant.State;orb.Live=app.Assistant.VoiceConnected || app.Assistant.WakeListening;orb.Level=app.Assistant.Level;orb.ReduceMotion=app.Settings.ReduceMotion;button.ToolTip=app.Assistant.WakeListening?app.Assistant.WakeStatus:Localization.Text("Open AI companion");}
        button.Loaded+=(_,_)=>{app.Assistant.Changed+=Refresh;app.Assistant.VisualChanged+=Refresh;Refresh();};
        button.Unloaded+=(_,_)=>{app.Assistant.Changed-=Refresh;app.Assistant.VisualChanged-=Refresh;};
        Refresh();return button;
    }
}
