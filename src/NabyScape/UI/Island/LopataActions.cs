using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NabyScape;

internal static class LopataActions
{
    internal static Button Button(string caption, string icon, Action action, double size = 36)
    {
        var button = UI.CaptionButton(caption, action);
        button.Width = button.Height = size;
        button.MinHeight = 0;
        button.Margin = new(0, 0, 4, 0);
        button.Padding = new(0);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.ToolTip = Localization.Text(caption);
        var glyph = Icons.Create(icon, 19, foregroundResource: "ControlText");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        button.Content = new Grid { Height = size - 2, IsHitTestVisible = false, Children = { glyph } };
        return button;
    }

    internal static Border AddBadge(Button button)
    {
        var label = new TextBlock { FontSize = 9, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        var badge = new Border
        {
            MinWidth = 8, MinHeight = 8, CornerRadius = new(7), Padding = new(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed, Child = label
        };
        badge.SetResourceReference(Border.BackgroundProperty, "Accent");
        ((Grid)button.Content).Children.Add(badge);
        return badge;
    }

    internal static void SetCount(Button button, Border badge, int count, bool singleAsDot = false)
    {
        var label = (TextBlock)badge.Child;
        label.Text = count > 99 ? "99+" : count.ToString(Localization.Culture);
        label.Visibility = count == 1 && singleAsDot ? Visibility.Collapsed : Visibility.Visible;
        badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var status = count > 0 ? count.ToString(Localization.Culture) : "";
        AutomationProperties.SetItemStatus(button, status);
        var name = AutomationProperties.GetName(button);
        button.ToolTip = count > 0 ? name + " · " + status : name;
    }

    internal static void SetLayer(Button button, bool above)
    {
        button.BorderThickness = new Thickness(above ? 1 : 0);
        button.SetResourceReference(Control.BorderBrushProperty, "Accent");
        var status = Localization.Text(above ? "On" : "Off");
        AutomationProperties.SetItemStatus(button, status);
        button.ToolTip = Localization.Text(LopataEdition.LayerCaption) + " · " + status;
    }
}
