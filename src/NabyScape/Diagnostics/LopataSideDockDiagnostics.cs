using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

internal static class LopataSideDockDiagnostics
{
    internal static async Task Check(App app, OverlayWindow island, string directory, Action<OverlayWindow, string> capture)
    {
        var settings = app.Settings;
        var saved = (settings.DockEdge, settings.DockVertical, settings.DockInset, settings.TwoStageHover);
        var report = new List<string>();
        var screen = Native.ChooseScreen(settings);
        var area = screen.WorkingArea;
        double scale = Native.Scale(screen);
        async Task Settle()
        {
            await Task.Delay(350);
            for (int i = 0; i < 40 && (island.IsAnimating || island.TuckMoving); i++) await Task.Delay(30);
            island.UpdateLayout();
        }
        void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label +
                $" (edge={settings.DockEdge}, size={island.PaintedWidth:0.0}×{island.PaintedHeight:0.0})");
            report.Add("PASS " + label);
        }
        void AtEdge(string edge, string state)
        {
            var start = island.PointToScreen(new Point((island.ActualWidth - island.PaintedWidth) / 2, 0));
            double right = start.X + island.PaintedWidth * scale;
            double bottom = start.Y + island.PaintedHeight * scale;
            double inset = settings.DockInset * scale;
            Require(edge == "Left" ? Math.Abs(start.X - area.Left - inset) <= 3 : Math.Abs(right - area.Right + inset) <= 3,
                edge + " " + state + " paints inward from the requested edge");
            Require(start.Y >= area.Top - 2 && bottom <= area.Bottom + 2, edge + " " + state + " fits the work area");
        }
        Button Button(string id) => Diagnostics.Descendants(island).OfType<Button>()
            .Single(button => button.IsVisible && UI.Identity(button) == id);

        try
        {
            settings.DockVertical = 50;
            settings.DockInset = 0;
            settings.TwoStageHover = true;
            app.Media.DiagnosticFixture = true;
            app.Media.DiagnosticPlayback(true);
            foreach (string edge in new[] { "Left", "Right" })
            {
                island.Collapse();
                settings.DockEdge = edge;
                app.ChangeSettings();
                await Settle();
                AtEdge(edge, "closed");
                Require(island.PaintedHeight > 3.5 * island.PaintedWidth && Button("Lopata.Chat.CompactMascot").ActualHeight >= 24,
                    edge + " collapsed island is a slim vertical strip with a clickable mascot");
                capture(island, Path.Combine(directory, $"side-{edge.ToLowerInvariant()}-closed.png"));

                island.PreviewActivity();
                await Settle();
                AtEdge(edge, "hover");
                Require(island.IsPeeking && island.PaintedWidth < island.PaintedHeight &&
                    island.PaintedHeight < 1.6 * island.PaintedWidth,
                    edge + " hover preview is nearly square");
                var peek = Button("Play or pause from preview");
                Require(peek.TranslatePoint(new Point(peek.ActualWidth / 2, peek.ActualHeight / 2), island).Y <
                    island.PaintedHeight, edge + " preview shows the play button within the contour");
                island.SelectPeek("media");
                await Settle();
                AtEdge(edge, "detailed hover");
                capture(island, Path.Combine(directory, $"side-{edge.ToLowerInvariant()}-preview.png"));
                var overflow = island.HoverOverflow();
                Require(island.PeekDetailed && overflow.Length == 0, edge + " detailed preview controls fit" +
                    (overflow.Length > 0 ? " — " + overflow : ""));

                island.Reveal(false);
                island.SelectPage("media", false);
                await Settle();
                AtEdge(edge, "expanded");
                Require(island.IsExpanded && island.PaintedHeight > 1.2 * island.PaintedWidth &&
                    Button("Lopata.Chat.Mascot").IsVisible && Button("Open music list").IsVisible,
                    edge + " tall expanded panel retains the toolbar and music controls");
                capture(island, Path.Combine(directory, $"side-{edge.ToLowerInvariant()}-expanded.png"));
            }

            settings.DockEdge = "Left";
            settings.DockInset = 17;
            settings.DockVertical = 72;
            app.ChangeSettings();
            await Settle();
            AtEdge("Left", "switched while expanded");
            Require(island.IsExpanded && island.ActivePage == "media", "Changing sides keeps the open page");
            island.Collapse();
            await Settle();
            island.ToggleTuck();
            await Settle();
            Require(island.IsTucked && island.VisibleBounds.Width <= settings.TuckEdge + 1 &&
                island.VisibleBounds.Height > 200, $"The hidden side strip uses its real width and height ({island.VisibleBounds})");
            island.ToggleTuck();
            await Settle();

            island.OpenNotificationCenter();
            await Settle();
            AtEdge("Left", "notification center");
            Require(island.NotificationCenterOpen && island.PaintedHeight > island.PaintedWidth,
                "The notification center uses portrait layout at the side");
            island.Collapse();
            await Settle();

            foreach (string edge in new[] { "Top", "Bottom" })
            {
                settings.DockEdge = edge;
                app.ChangeSettings();
                await Settle();
                Require(island.PaintedWidth > 3 * island.PaintedHeight &&
                    Button("Lopata.Chat.CompactMascot").IsVisible,
                    edge + " returns to a horizontal collapsed island");
            }
            File.WriteAllLines(Path.Combine(directory, "side-dock-check.txt"), report);
        }
        finally
        {
            (settings.DockEdge, settings.DockVertical, settings.DockInset, settings.TwoStageHover) = saved;
            app.Media.DiagnosticPlayback(false);
            app.Media.DiagnosticFixture = false;
            app.ChangeSettings();
            await Settle();
        }
    }
}
