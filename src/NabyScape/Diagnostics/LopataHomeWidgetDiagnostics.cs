using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace NabyScape;

internal static class LopataHomeWidgetDiagnostics
{
    internal static async Task Check(App app, OverlayWindow island, string directory, Action<OverlayWindow,string> capture)
    {
        var log = new List<string>();
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            log.Add("PASS " + message);
        }

        IEnumerable<Button> Stars() => Diagnostics.Descendants(island).OfType<Button>()
            .Where(button => button.IsVisible && AutomationProperties.GetAutomationId(button) == "Lopata.HomeWidget");
        Button Star() => Stars().Single();
        bool Selected(Button button) => ((ShapePath)button.Content).Fill != Brushes.Transparent &&
            AutomationProperties.GetItemStatus(button) == Localization.Text("Selected");
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Settle()
        {
            for (int i=0; i<40 && island.IsAnimating; i++) await Task.Delay(35);
            island.UpdateLayout();
        }
        void CheckPlacement(string page, Func<FrameworkElement, bool>? isExpectedHeading = null)
        {
            var star = Star();
            var row = (Grid)star.Parent;
            var heading = (FrameworkElement)row.Children[0];
            var mascot = Diagnostics.Descendants(island).OfType<Button>().Single(button =>
                button.IsVisible && AutomationProperties.GetAutomationId(button) == "Lopata.Chat.Mascot");
            Rect Bounds(FrameworkElement item) => item.TransformToAncestor(island).TransformBounds(new Rect(item.RenderSize));
            var starBounds = Bounds(star);
            var headingBounds = Bounds(heading);
            var mascotBounds = Bounds(mascot);
            var center = new Point(starBounds.Left + starBounds.Width / 2, starBounds.Top + starBounds.Height / 2);
            var hit = island.InputHitTest(center) as DependencyObject;
            Require((isExpectedHeading is null || isExpectedHeading(heading)) &&
                starBounds.Left >= headingBounds.Right && starBounds.Left - headingBounds.Right < 8 &&
                Math.Abs(center.Y - headingBounds.Top - headingBounds.Height / 2) < 1 &&
                starBounds.Top > mascotBounds.Bottom + 8 &&
                (hit == star || hit is not null && star.IsAncestorOf(hit)),
                $"The {page} star is clickable beside its own heading and separate from the mascot");
        }

        var settings = app.Settings;
        bool compact = settings.CompactLayout;
        bool hover = settings.HoverPeek;
        string prior = settings.HomeWidget;
        var enabled = settings.Widgets.ToDictionary(widget => widget.Id, widget => widget.Enabled);
        SettingsWindow? window = null;
        try
        {
            settings.CompactLayout = true;
            settings.HomeWidget = "media";
            foreach (var widget in settings.Widgets.Where(widget => app.SupportsWidget(widget.Id))) widget.Enabled = true;
            app.ChangeSettings();
            app.ShowSettings("Appearance");
            window = app.Windows.OfType<SettingsWindow>().Single();
            window.UpdateLayout();
            var boxes = Diagnostics.LogicalDescendants(window).OfType<ComboBox>().ToArray();
            var chooser = boxes.Single(box => UI.Identity(box) == LopataHomeWidget.Caption);
            var preview = boxes.Single(box => UI.Identity(box) == "Preview content");
            preview.SelectedValue = "System";
            Require(Equals(chooser.SelectedValue, "media") && LopataHomeWidget.Current(app) == "media",
                "The preview sample is independent of the saved opening widget");

            island.Reveal(false);
            await Settle();
            Require(island.ActivePage == "media" && Selected(Star()), "The main widget opens with a filled star");
            CheckPlacement("media", heading => UI.Identity(heading) == "Media source");
            capture(island, Path.Combine(directory,"home-star-media.png"));
            island.SelectPage("timer",false);
            await Settle();
            Require(!Selected(Star()), "The Timer star is an outline before selection");
            CheckPlacement("timer", heading => heading is TextBlock text && text.Text == Localization.Text("Timer"));

            Click(Star());
            Require(Selected(Star()) && Equals(chooser.SelectedValue, "timer") &&
                app.Store.Load<Settings>("settings").HomeWidget == "timer", "The star updates settings and persists the Timer");
            Click(Star());
            Require(settings.HomeWidget == "timer", "Clicking the selected star leaves exactly one main widget");
            capture(island, Path.Combine(directory,"home-star-timer.png"));
            island.SelectPage("notes",false);
            Require(!Selected(Star()), "The Notes star remains an outline");
            island.Collapse();
            island.Reveal(false);
            Require(island.ActivePage == "timer", "Reopening the island returns to the selected Timer");

            chooser.SelectedValue = "notes";
            Require(settings.HomeWidget == "notes" && !Selected(Star()), "Changing settings clears the old star without switching the open widget");
            island.Collapse();
            island.Reveal(false);
            Require(island.ActivePage == "notes" && Selected(Star()), "The new choice opens first and only its star is filled");
            island.Collapse();
            settings.HoverPeek = true;
            island.PreviewActivity();
            island.SelectPeek("media");
            island.Collapse();
            island.PreviewActivity();
            Require(island.IsPeeking && island.PeekPage == "media", "Passive hover presents the music preview");
            island.ExpandFromPeek();
            Require(island.ActivePage == "notes", "Expanding the passive preview opens the saved main widget");
            island.Collapse();
            island.PreviewActivity();
            island.SelectPeek("media");
            island.ExpandFromPeek();
            Require(island.ActivePage == "media", "Expanding an explicitly selected preview opens that widget");
            island.Collapse();
            island.ShowTools("links",false);
            Require(island.ActivePage == "links", "A direct shortcut still opens its requested tool");
            foreach (string page in LopataHomeWidget.Choices(app).Where(page => page is not ("media" or "timer")))
            {
                island.SelectPage(page, false);
                await Settle();
                CheckPlacement(page, page == "status" ? heading => heading is TextBlock text && text.Text.Contains(':') : null);
                if (page is "status" or "notes")
                    capture(island, Path.Combine(directory,$"home-star-{page}.png"));
            }

            settings.CompactLayout = false;
            app.ChangeSettings();
            island.Collapse();
            island.Reveal(false);
            await Settle();
            Require(Stars().Count() > 1 && Selected(Stars().First()) && Stars().Count(Selected) == 1,
                "The card layout shows the chosen group first with one filled star");
            File.WriteAllLines(Path.Combine(directory,"home-widget-check.txt"),log);
        }
        catch (Exception ex)
        {
            File.WriteAllLines(Path.Combine(directory,"home-widget-check.txt"),log.Append(ex.ToString()));
            throw;
        }
        finally
        {
            window?.Close();
            island.Collapse();
            settings.HomeWidget = prior;
            settings.HoverPeek = hover;
            settings.CompactLayout = compact;
            foreach (var widget in settings.Widgets)
                if (enabled.TryGetValue(widget.Id, out bool wasEnabled)) widget.Enabled = wasEnabled;
            app.ChangeSettings();
        }
    }
}
