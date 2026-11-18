using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NabyScape;

internal static class CountdownPanelDiagnostics
{
    internal static async Task Run(App app,OverlayWindow island,string directory,Action<string,bool> check)
    {
        var s=app.Settings;s.CompactLayout=true;s.CategoryStyle="Dots";s.PanelWidth=526;s.PanelMaxHeight=500;s.PanelPadding=26;s.PanelScale=1;s.ReduceMotion=true;s.HideMaximized=s.HideFullscreen=s.AlwaysTucked=false;s.NotificationDelivery="Off";s.CountdownSound="Off";
        foreach(var widget in s.Widgets)widget.Enabled=widget.Id is "timer" or "media";
        app.Session.Timer.Reset();app.ChangeSettings();island.Reveal(false);island.SelectPage("timer",false);await Task.Delay(150);
        CountdownView View()=>Diagnostics.Descendants(island).OfType<CountdownView>().Single();
        Button Button(string action="primary")=>Diagnostics.Descendants(View()).OfType<Button>().Single(b=>AutomationProperties.GetAutomationId(b)=="timer-"+action);
        TextBox Entry()=>Diagnostics.Descendants(View()).OfType<TextBox>().Single();
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        void Capture(string name,double scale=1)
        {
            island.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(island.ActualWidth*scale),(int)Math.Ceiling(island.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(island);
            int w=Math.Min(bitmap.PixelWidth,(int)Math.Ceiling(island.PaintedWidth*scale)),h=Math.Min(bitmap.PixelHeight,(int)Math.Ceiling(island.PaintedHeight*scale));
            var crop=new CroppedBitmap(bitmap,new Int32Rect(Math.Max(0,(bitmap.PixelWidth-w)/2),0,w,h));var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(crop));using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
        }
        check("Only one main action and Reset exist ",Diagnostics.Descendants(View()).OfType<Button>().Count(b=>AutomationProperties.GetAutomationId(b).EndsWith("-primary") || AutomationProperties.GetAutomationId(b).EndsWith("-reset"))==2);
        Capture("ready");
        {
            const string label="Timer";var state=app.Session.Timer;
            var entry=Entry();var view=View();
            entry.Text="1.5";
            check(label+" starts with a clear Start action",AutomationProperties.GetItemStatus(Button())=="Start" && Button().IsEnabled);
            Click(Button());double expected=90;var run=state.RunId;
            check(label+" uses the entered duration",state.Running && Math.Abs(state.DurationSeconds-expected)<.01);
            check(label+" changes Start to Pause immediately",AutomationProperties.GetItemStatus(Button())=="Pause" && AutomationProperties.GetName(Button())=="Pause "+label.ToLowerInvariant());
            check(label+" locks duration during the session",!entry.IsEnabled);
            Click(Button());double paused=state.Remaining(DateTimeOffset.UtcNow);await Task.Delay(160);
            check(label+" pauses rather than restarting",!state.Running && state.RunId==run && state.DurationSeconds==expected && state.Remaining(DateTimeOffset.UtcNow)==paused && AutomationProperties.GetItemStatus(Button())=="Resume");
            Capture("timer-paused");
            Click(Button());
            check(label+" resumes the same session",state.Running && state.RunId==run && state.DurationSeconds==expected && state.Remaining(DateTimeOffset.UtcNow)<=paused && AutomationProperties.GetItemStatus(Button())=="Pause");
            check(label+" keeps its controls across shared heartbeats",ReferenceEquals(view,View()) && ReferenceEquals(entry,Entry()));
        }
        Capture("running");Capture("running-150",1.5);Capture("running-200",2);
        {
            Click(Button("reset"));var entry=Entry();
            check("Timer"+" Reset restores editable ready state",AutomationProperties.GetItemStatus(Button())=="Start" && !Button("reset").IsEnabled && entry.IsEnabled);
            entry.Text="NaN";check("Timer"+" rejects invalid duration inline",!Button().IsEnabled);
            entry.Text="5";
        }
        var draft=Entry();draft.Focus();draft.Text="7.5";draft.Select(0,2);app.SaveSession();await Task.Delay(60);
        check("Heartbeat preserves duration draft and selection",draft.Text=="7.5" && draft.SelectionStart==0 && draft.SelectionLength==2);
        int unchanged=View().Refreshes;app.PublishUpdate(RuntimeChange.Audio|RuntimeChange.Media);
        check("Audio and media updates do not refresh countdown controls",View().Refreshes==unchanged);
        app.Session.Timer.Start(90,DateTimeOffset.UtcNow.AddSeconds(-30));app.SaveSession();
        check("Progress reflects elapsed session time",Diagnostics.Descendants(View()).OfType<ProgressBar>().Single().Value is >.33 and <.35);
        check("Timer started from another surface shows its actual duration",Entry().Text=="1.5" && AutomationProperties.GetItemStatus(Button())=="Pause");
        Click(Button("reset"));
        // Expiration uses the existing absolute-deadline service, without real notifications.
        app.Session.Timer.Start(60,DateTimeOffset.UtcNow.AddSeconds(-61));app.CheckCountdowns();
        check("Finished timer displays completion and can restart",AutomationProperties.GetItemStatus(Button())=="Start" && Button("reset").IsEnabled && Diagnostics.Descendants(View()).OfType<TextBlock>().Any(t=>t.Text=="Complete"));
        app.Session.Timer.Reset();app.SaveSession();
        var previous=View();island.SelectPage("media",false);await Task.Delay(80);int refreshes=previous.Refreshes;app.SaveSession();
        check("Hidden countdown releases its heartbeat subscription",!previous.Subscribed && previous.Refreshes==refreshes);
        island.SelectPage("timer",false);await Task.Delay(80);check("Reopening countdown resumes visible updates",View().Subscribed);
        foreach(double width in new[]{300d,526d})
        {
            s.PanelWidth=width;app.ChangeSettings();await Task.Delay(90);island.SelectPage("timer",false);island.UpdateLayout();
                {
                var view=View();var bounds=new Rect(0,0,view.ActualWidth,view.ActualHeight);
                check("Timer"+" actions fit at "+width+" DIP",Diagnostics.Descendants(view).OfType<Button>().All(b=>{var r=b.TransformToAncestor(view).TransformBounds(new Rect(0,0,b.ActualWidth,b.ActualHeight));return r.Left>=-.5 && r.Right<=bounds.Right+.5 && r.Bottom<=bounds.Bottom+.5;}));
            }
            Capture("ready-"+width);
        }
        s.CompactLayout=false;app.ChangeSettings();await Task.Delay(100);island.Reveal(false);
        Click(Button());check("Classic Timer uses the same state-driven action",AutomationProperties.GetItemStatus(Button())=="Pause");Click(Button("reset"));
        Capture("classic");
        File.WriteAllText(Path.Combine(directory,"capture-notes.txt"),"Isolated synthetic profile. No personal notes, clipboard, provider keys or network AI requests. 150/200% captures are WPF raster exports, not physical monitor DPI tests. Countdown completion delivery and sound disabled only in this fixture.");
    }
}
