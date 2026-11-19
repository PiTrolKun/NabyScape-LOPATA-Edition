using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private readonly Ellipse islandVoiceIndicator = new()
    {
        Width = 8, Height = 8, Fill = UI.Brush("#FF565F"), Margin = new(6, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed, IsHitTestVisible = false
    };

    private FrameworkElement WithIslandVoiceButton(FrameworkElement heading)
    {
        if (!app.UsesLopataEdition) return heading;
        var button = UI.Button("", app.ToggleIslandVoice);
        button.Width = 26;
        button.Height = 22;
        button.MinHeight = button.MinWidth = 0;
        button.Margin = new(3, 0, 0, 0);
        button.Padding = new(0);
        button.BorderThickness = new(0);
        button.Background = Brushes.Transparent;
        AutomationProperties.SetAutomationId(button, "Lopata.IslandVoice.Toggle");
        void Refresh()
        {
            bool on = app.IslandVoiceEnabled;
            button.IsEnabled = !app.IslandVoiceStopping;
            string caption = app.IslandVoiceStarting ? "Cancel microphone preparation"
                : on ? "Turn off voice control" : "Turn on voice control";
            button.Content = Icons.Create(on ? "mic" : "mic-off", 15, on ? UI.Brush("#FF565F") : UI.Muted);
            button.ToolTip = Localization.Text(caption);
            UI.Named(button, Localization.Text(caption));
        }
        button.Loaded += (_, _) => { app.IslandVoiceChanged += Refresh; Refresh(); };
        button.Unloaded += (_, _) => app.IslandVoiceChanged -= Refresh;
        Refresh();

        var row = new DockPanel { LastChildFill = false, HorizontalAlignment = HorizontalAlignment.Left };
        row.Children.Add(heading);
        row.Children.Add(button);
        return row;
    }

    private void InitializeIslandVoiceIndicator(Grid header)
    {
        int mascot = Grid.GetColumn(mascotHeaderSpace);
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        Grid.SetColumn(islandVoiceIndicator, mascot);
        Grid.SetColumn(mascotHeaderSpace, header.ColumnDefinitions.Count - 1);
        header.Children.Add(islandVoiceIndicator);
        AutomationProperties.SetAutomationId(islandVoiceIndicator, "Lopata.IslandVoice.Indicator");
        void Refresh()
        {
            UI.Named(islandVoiceIndicator, Localization.Text("Voice control microphone is listening"));
            islandVoiceIndicator.Visibility = app.IslandVoiceEnabled ? Visibility.Visible : Visibility.Collapsed;
            islandVoiceIndicator.BeginAnimation(OpacityProperty,
                app.IslandVoiceEnabled && !app.Settings.ReduceMotion
                    ? new DoubleAnimation(1, .5, TimeSpan.FromMilliseconds(850))
                    { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                    : null);
        }
        app.IslandVoiceChanged += Refresh;
        app.SettingsChanged += Refresh;
        Closed += (_, _) => { app.IslandVoiceChanged -= Refresh; app.SettingsChanged -= Refresh; };
        Refresh();
    }
}
