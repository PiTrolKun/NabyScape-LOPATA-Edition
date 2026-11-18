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

        var settings = app.Settings;
        bool compact = settings.CompactLayout;
        string prior = settings.HomeWidget;
        var enabled = settings.Widgets.Where(widget => widget.Id is "media" or "timer" or "notes")
            .ToDictionary(widget => widget.Id, widget => widget.Enabled);
        SettingsWindow? window = null;
        try
        {
            settings.CompactLayout = true;
            settings.HomeWidget = "media";
            foreach (var widget in settings.Widgets.Where(widget => enabled.ContainsKey(widget.Id))) widget.Enabled = true;
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
            island.SelectPage("focus",false);
            await Settle();
            Require(!Selected(Star()), "The Timer star is an outline before selection");
            var mascot = Diagnostics.Descendants(island).OfType<Button>().Single(button =>
                button.IsVisible && AutomationProperties.GetAutomationId(button) == "Lopata.Chat.Mascot");
            var starBounds = Star().TransformToAncestor(island).TransformBounds(new Rect(Star().RenderSize));
            var mascotBounds = mascot.TransformToAncestor(island).TransformBounds(new Rect(mascot.RenderSize));
            var hitPoint = new Point(starBounds.Left + starBounds.Width / 2, starBounds.Top + starBounds.Height / 2);
            var hit = island.InputHitTest(hitPoint) as DependencyObject;
            Require(starBounds.Top >= mascotBounds.Bottom - .1 &&
                Math.Abs(hitPoint.X - mascotBounds.Left - mascotBounds.Width / 2) < 1 &&
                (hit == Star() || hit is not null && Star().IsAncestorOf(hit)),
                "The star is centered under the mascot and has a separate hit target");

            Click(Star());
            Require(Selected(Star()) && Equals(chooser.SelectedValue, "focus") &&
                app.Store.Load<Settings>("settings").HomeWidget == "focus", "The star updates settings and persists the Timer");
            Click(Star());
            Require(settings.HomeWidget == "focus", "Clicking the selected star leaves exactly one main widget");
            capture(island, Path.Combine(directory,"home-widget-timer.png"));
            island.SelectPage("notes",false);
            Require(!Selected(Star()), "The Notes star remains an outline");
            island.Collapse();
            island.Reveal(false);
            Require(island.ActivePage == "focus", "Reopening the island returns to the selected Timer");

            chooser.SelectedValue = "notes";
            Require(settings.HomeWidget == "notes" && !Selected(Star()), "Changing settings clears the old star without switching the open widget");
            island.Collapse();
            island.Reveal(false);
            Require(island.ActivePage == "notes" && Selected(Star()), "The new choice opens first and only its star is filled");
            island.Collapse();
            island.ShowTools("links",false);
            Require(island.ActivePage == "links", "A direct shortcut still opens its requested tool");

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
            settings.CompactLayout = compact;
            foreach (var widget in settings.Widgets)
                if (enabled.TryGetValue(widget.Id, out bool wasEnabled)) widget.Enabled = wasEnabled;
            app.ChangeSettings();
        }
    }
}
