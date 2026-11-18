using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NabyScape;

internal static class LopataWidgetReviewDiagnostics
{
    internal static async Task Check(App app, OverlayWindow island, string directory, Action<OverlayWindow,string> capture)
    {
        var log = new List<string>();
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            log.Add("PASS " + message);
        }
        async Task WaitFor(Func<bool> condition, string message)
        {
            for (int i=0;i<80 && !condition();i++) await Task.Delay(50);
            island.UpdateLayout();
            Require(condition(), message);
        }
        async Task Settle()
        {
            for(int i=0;i<36 && island.IsAnimating;i++)await Task.Delay(35);
            island.UpdateLayout();
        }
        bool TextIs(DependencyObject root, string caption) => Diagnostics.Descendants(root).OfType<TextBlock>()
            .Any(text=>text.IsVisible && text.Text==Localization.Text(caption));
        Button Action(string id) => Diagnostics.Descendants(island).OfType<Button>()
            .Single(button => button.IsVisible && (UI.Identity(button)==id || AutomationProperties.GetName(button)==Localization.Text(id)));
        var settings=app.Settings;
        var history=settings.ClipboardHistory;
        try
        {
            Require(!app.SupportsWidget("focus") && !app.Previews.CanOpen("focus") &&
                !island.HoverDeckIds.Contains("focus"), "LOPATA filters Focus from old widget settings, hover and detached views");
            island.Reveal(false);
            island.SelectPage("focus",false);
            await WaitFor(()=>TextIs(island,"Timer") && !TextIs(island,"Focus"),
                "The countdown page shows the normal Timer and omits Focus");
            await Settle();
            capture(island, Path.Combine(directory,"review-timer.png"));

            island.SelectPage("status",false);
            await WaitFor(()=>TextIs(island,"System uptime"),"The overview renders the last system row");
            var uptime=Diagnostics.Descendants(island).OfType<TextBlock>().Single(text=>text.IsVisible && text.Text==Localization.Text("System uptime"));
            var viewport=Diagnostics.Descendants(island).OfType<ScrollViewer>().First(view=>view.IsVisible);
            viewport.ScrollToEnd();
            await Task.Delay(260);
            island.UpdateLayout();
            var bottom=uptime.TranslatePoint(new Point(0,uptime.ActualHeight),island).Y;
            Require(bottom<=island.PaintedHeight-5,"The entire final overview row fits in its scroll region");
            capture(island,Path.Combine(directory,"review-status.png"));

            settings.ClipboardHistory=false;
            app.ChangeSettings();
            island.ShowTools("clips",false);
            Action("Enable history").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(()=>TextIs(island,"History is on. Copy text, a link or an image with Ctrl+C to see it here."),
                "Enabling clipboard history immediately tells the user how to add an item");
            const string clip="https://example.com/?id=42&utm_source=mail";
            Require(app.Productivity.IngestText(clip),"The clipboard service saves a test item when history is enabled");
            await WaitFor(()=>Diagnostics.Descendants(island).OfType<Button>().Any(button=>
                button.IsVisible && AutomationProperties.GetName(button).StartsWith(Localization.Text("View Link"),StringComparison.OrdinalIgnoreCase)),
                "New clipboard history appears in the open island");
            await Settle();
            capture(island,Path.Combine(directory,"review-clipboard.png"));
            app.Productivity.ClipboardPaused=true;
            Require(!app.Productivity.IngestText("not recorded") && TextIs(island,"Capture is paused. Turn off Pause capture to save new items."),
                "Pausing prevents new entries and displays its current state");

            island.ShowTools("links",false);
            TextBox Field(string caption)=>Diagnostics.Descendants(island).OfType<TextBox>()
                .Single(input=>input.IsVisible && UI.Identity(input)==Localization.Text(caption));
            var original=Field("Link to clean");
            var cleaned=Field("Cleaned link");
            Require(Diagnostics.Descendants(island).OfType<Label>().Any(label=>label.IsVisible && label.Target==original) &&
                Diagnostics.Descendants(island).OfType<Label>().Any(label=>label.IsVisible && label.Target==cleaned),
                "The original and cleaned URL fields have visible labels");
            original.Text=clip+"#section";
            Require(cleaned.Text=="https://example.com/?id=42#section" && Action("Copy cleaned link").IsEnabled,
                "Cleaning drops a tracking tag and keeps the regular parameter and page fragment");
            await Settle();
            var copyLink=Action("Copy cleaned link");
            Require(copyLink.TranslatePoint(new Point(0,copyLink.ActualHeight),island).Y<=island.PaintedHeight-8,
                "The copy button fits in the normal expanded panel");
            capture(island,Path.Combine(directory,"review-clean-links.png"));
            original.Text="This is not a URL";
            Require(cleaned.Text.Length==0 && !Action("Copy cleaned link").IsEnabled,
                "An invalid URL clears the old result and disables copying");

            app.ShowSettings("Content");
            var window=app.Windows.OfType<SettingsWindow>().Single();
            window.UpdateLayout();
            Require(!TextIs(window,"Focus sessions") && !TextIs(window,"Focus mode") &&
                !Diagnostics.Descendants(window).OfType<ComboBox>().Any(choice=>choice.Items.OfType<ComboBoxItem>().Any(item=>Equals(item.Tag,"Focus"))),
                "The settings and sample choices omit Focus");
            window.OpenCategory("Notifications");
            window.UpdateLayout();
            Require(!Diagnostics.Descendants(window).OfType<CheckBox>().Any(check=>Equals(check.Content,Localization.Text("Focus finished"))),
                "Notifications settings omit Focus completion");
            window.Close();
            File.WriteAllLines(Path.Combine(directory,"widget-review-check.txt"),log);
        }
        finally
        {
            settings.ClipboardHistory=history;
            app.Productivity.ClipboardPaused=false;
            app.ChangeSettings();
            island.Collapse();
        }
    }
}
