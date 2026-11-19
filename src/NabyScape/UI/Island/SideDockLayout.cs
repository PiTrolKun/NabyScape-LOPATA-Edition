using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NabyScape;

internal static class SideDockLayout
{
    internal static bool IsSide(string edge) => edge is "Left" or "Right";
    internal static double ClosedWidth(Settings settings) => Math.Max(68, settings.Height + 28);
    internal static double ClosedHeight(Settings settings) => Math.Clamp(settings.Width * 1.56, 340, 650);
    internal static double PeekWidth(double savedWidth, double availableWidth, double availableHeight)
        => Math.Min(Math.Clamp(savedWidth * .80, 244, 304), Math.Min(availableWidth, availableHeight * .88));
    internal static double PanelWidth(double savedWidth, double availableWidth, double availableHeight)
        => Math.Min(Math.Clamp(savedWidth * .82, 264, 374), Math.Min(availableWidth, availableHeight * .80));
}

public sealed partial class OverlayWindow
{
    private readonly Grid horizontalClosed = new();
    private Grid? portraitClosed;
    private readonly List<ClosedSlot> horizontalClosedSlots = new();
    private readonly record struct ClosedSlot(FrameworkElement Element, int Column, int Span,
        Thickness Margin, HorizontalAlignment Alignment);
    private bool PortraitDock => app.UsesLopataEdition && SideDockLayout.IsSide(app.Settings.DockEdge);
    private double portraitAvailableHeight = 900;
    private double portraitPeekWidth = 280;
    private int? portraitHoverPointer;
    private double ClosedSurfaceHeight => PortraitDock
        ? Math.Min(SideDockLayout.ClosedHeight(app.Settings), portraitAvailableHeight) : app.Settings.Height;

    private void ConfigureClosedDock()
    {
        bool side = PortraitDock;
        if (side && compact.Content == horizontalClosed)
        {
            if (portraitClosed is null)
            {
                portraitClosed = new Grid();
                // The middle carries the current activity. The mascot has a separate hit target at the foot.
                double[] fixedHeights = [14, 0, 0, 8, 0, -1, 0, 8, 0, 0, 0, 52];
                foreach (double height in fixedHeights)
                    portraitClosed.RowDefinitions.Add(new()
                    {
                        Height = height < 0 ? new GridLength(1, GridUnitType.Star)
                            : height > 0 ? new GridLength(height) : GridLength.Auto
                    });
            }
            Move(compactClock, 1);
            Move(idleDate, 2);
            Move(islandVoiceIndicator, 3);
            Move(closedCover, 4);
            Move(activity, 4);
            Move(feedbackIcon, 4);
            Move(closedSummaryHost, 5);
            closedSummaryHost.LayoutTransform = new RotateTransform(-90);
            closedSummaryHost.VerticalAlignment = VerticalAlignment.Stretch;
            closedSummaryHost.Margin = new Thickness(0, 6, 0, 6);
            Move(closedTitleWave, 5, stretch: true);
            Move(feedbackLevel, 6);
            Move(lopataActivity, 8);
            Move(lopataUpdateBadge, 9);
            Move(awakeMark, 10);
            compact.Content = portraitClosed;
            summary.TextAlignment = TextAlignment.Center;
        }
        else if (!side && compact.Content != horizontalClosed)
        {
            foreach (var slot in horizontalClosedSlots)
            {
                portraitClosed!.Children.Remove(slot.Element);
                Grid.SetRow(slot.Element, 0);
                Grid.SetColumn(slot.Element, slot.Column);
                Grid.SetColumnSpan(slot.Element, slot.Span);
                slot.Element.Margin = slot.Margin;
                slot.Element.HorizontalAlignment = slot.Alignment;
                horizontalClosed.Children.Add(slot.Element);
            }
            horizontalClosedSlots.Clear();
            compact.Content = horizontalClosed;
            closedSummaryHost.ClearValue(LayoutTransformProperty);
            closedSummaryHost.VerticalAlignment = VerticalAlignment.Center;
            summary.ClearValue(TextBlock.TextAlignmentProperty);
            clockSlot = null;
        }

        compact.Padding = side ? new Thickness(4, 0, 4, 0) : new Thickness(14, 0, 14, 0);
        compact.Width = ClosedContentWidth;
        compact.Height = compact.MinHeight = ClosedSurfaceHeight;
        if (side) portraitClosed!.Height = ClosedSurfaceHeight;
        if (app.UsesLopataEdition) ArrangeClosedMascot();

        void Move(FrameworkElement element, int row, bool stretch = false)
        {
            horizontalClosedSlots.Add(new(element, Grid.GetColumn(element), Grid.GetColumnSpan(element),
                element.Margin, element.HorizontalAlignment));
            horizontalClosed.Children.Remove(element);
            Grid.SetColumn(element, 0);
            Grid.SetColumnSpan(element, 1);
            Grid.SetRow(element, row);
            element.Margin = new Thickness(0);
            element.HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
            portraitClosed!.Children.Add(element);
        }
    }

    private void ConfigurePeekMediaLayout()
    {
        bool portrait = PortraitDock;
        if (peekMediaPortrait == portrait) return;
        peekMediaPortrait = portrait;
        peekMedia.RowDefinitions.Clear();
        double[] heights = portrait ? [74, 48, 40, 16, 18, 30] : [56, 18, 18, 32];
        foreach (double height in heights)
            peekMedia.RowDefinitions.Add(new() { Height = new GridLength(height) });

        Place(peekCoverFrame, 0, 0, 1);
        Place(peekMediaInfo, portrait ? 1 : 0, portrait ? 0 : 1, 1);
        Place(peekPlay!, portrait ? 2 : 0, portrait ? 0 : 2, 1);
        Place(peekProgress, portrait ? 3 : 1, 0, 3);
        Place(peekMediaTimes, portrait ? 4 : 2, 0, 3);
        Place(peekWave, portrait ? 4 : 2, 1, 1, spanOnSide: true);
        Place(peekMediaControls, portrait ? 5 : 3, 0, 3);

        double size = portrait ? 72 : 56;
        peekCoverFrame.Width = peekCoverFrame.Height = peekArt.Width = peekArt.Height = size;
        peekCoverFrame.Clip = new RectangleGeometry(new Rect(0, 0, size, size), 14, 14);
        peekCoverFrame.HorizontalAlignment = portrait ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        peekCoverFrame.Margin = portrait ? new Thickness(0) : new Thickness(0, 0, 16, 0);
        peekPlay!.HorizontalAlignment = portrait ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        peekPlay.Margin = portrait ? new Thickness(0) : new Thickness(16, 0, 0, 0);
        peekTitle.TextAlignment = peekSubtitle.TextAlignment = portrait ? TextAlignment.Center : TextAlignment.Left;
        peekDetach.Width = portrait ? 28 : 52;
        peekDetach.Content = portrait ? Icons.Create("open-lopata", 14) : Localization.Text("Detach");

        void Place(UIElement element, int row, int column, int span, bool spanOnSide = false)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, portrait && spanOnSide ? 0 : column);
            Grid.SetColumnSpan(element, portrait ? 3 : span);
        }
    }
}
