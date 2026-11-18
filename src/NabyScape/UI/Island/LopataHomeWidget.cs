using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NabyScape;

internal static class LopataHomeWidget
{
    internal const string Caption = "Main widget";
    internal const string Help = "Opens first when you expand the island. You can also choose it with the star on a widget.";
    internal const string ChooseCaption = "Make this the main widget";

    internal static string[] Choices(App app) => app.Settings.Widgets
        .Where(widget => widget.Enabled && app.SupportsWidget(widget.Id))
        .Select(widget => OverlayWindow.PageFor(widget.Id))
        .Distinct()
        .ToArray();

    internal static string? Current(App app, string[]? choices = null)
    {
        choices ??= Choices(app);
        return choices.Contains(app.Settings.HomeWidget) ? app.Settings.HomeWidget : choices.FirstOrDefault();
    }

    internal static void Select(App app, string page)
    {
        if (app.Settings.HomeWidget == page || !Choices(app).Contains(page)) return;
        app.Settings.HomeWidget = page;
        app.ChangeSettings();
    }
}

public sealed partial class OverlayWindow
{
    internal const double HomeWidgetStarHeight = 18;
    private const double FullMascotHeight = 62;
    private const double WidgetMascotHeight = 56;
    private const double HomeWidgetHeaderGrowth = WidgetMascotHeight + HomeWidgetStarHeight - FullMascotHeight;
    private Button? homeWidgetStar;
    private static readonly Geometry HomeStarShape =
        Geometry.Parse("M12,2 L15,8.5 22,9.5 17,14.5 18.2,21.5 12,18 5.8,21.5 7,14.5 2,9.5 9,8.5 Z");

    private Button CreateHomeWidgetStar(Func<string> page)
    {
        var button = UI.Button("", () => LopataHomeWidget.Select(app, page()));
        button.Content = new Path { Data = HomeStarShape, Stretch = Stretch.Uniform, Width = 13, Height = 13,
            StrokeThickness = 1.25, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        button.Background = Brushes.Transparent;
        button.BorderThickness = new(0);
        button.Height = HomeWidgetStarHeight;
        button.Width = 26;
        button.MinHeight = button.MinWidth = 0;
        button.Padding = button.Margin = new(0);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(button, "Lopata.HomeWidget");
        return button;
    }

    private void StyleHomeWidgetStar(Button button, string page)
    {
        var pages = LopataHomeWidget.Choices(app);
        bool available = pages.Contains(page);
        button.Visibility = available ? Visibility.Visible : Visibility.Hidden;
        bool selected = available && page == LopataHomeWidget.Current(app, pages);
        var glyph = (Path)button.Content;
        Brush ink = Accessibility.HighContrast ? SystemColors.WindowTextBrush :
            selected ? UI.Brush(ThemeInk.IsLight ? "#946000" : "#FFD166") : UI.Muted;
        glyph.Fill = selected ? ink : Brushes.Transparent;
        glyph.Stroke = ink;
        string caption = selected ? LopataHomeWidget.Caption : LopataHomeWidget.ChooseCaption;
        string label = Localization.Text(caption);
        AutomationProperties.SetName(button, label);
        button.ToolTip = label;
        AutomationProperties.SetItemStatus(button, selected ? Localization.Text("Selected") : "");
    }

    private void RefreshHomeWidgetStar()
    {
        if (homeWidgetStar is not { } button) return;
        StyleHomeWidgetStar(button, activePage);
        if (showLopataJobs) button.Visibility = Visibility.Hidden;
    }

    private void AddClassicHomeWidgetStar(Border card, string page)
    {
        var stack = (StackPanel)card.Child;
        var title = stack.Children[0];
        stack.Children.RemoveAt(0);
        var star = CreateHomeWidgetStar(() => page);
        StyleHomeWidgetStar(star, page);
        DockPanel.SetDock(star, Dock.Right);
        var heading = new DockPanel { Children = { star, title } };
        stack.Children.Insert(0, heading);
    }
}
