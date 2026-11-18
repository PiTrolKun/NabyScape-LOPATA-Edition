using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private Button? playbackOrderButton;
    private LocalPlaybackOrder? displayedOrder;

    private Button BuildPlaybackOrderButton()
    {
        displayedOrder = null;
        var button = PlaybackSideButton("play-in-order", "Playback order", () =>
        {
            app.Settings.PlaybackOrder = (LocalPlaybackOrder)(((int)app.Settings.PlaybackOrder + 1) % 3);
            app.ChangeSettings();
            SyncPlaybackOrderButton();
        });
        ToolTipService.SetShowOnDisabled(button, true);
        playbackOrderButton = button;
        SyncPlaybackOrderButton();
        return button;
    }

    private void SyncPlaybackOrderButton()
    {
        if (playbackOrderButton is not { } button) return;
        button.IsEnabled = app.Media.Selected is null || app.Media.LocalTrack is not null;
        var order = app.Settings.PlaybackOrder;
        var (icon, label) = order switch
        {
            LocalPlaybackOrder.Shuffle => ("play-shuffled", "Shuffle"),
            LocalPlaybackOrder.RepeatTrack => ("repeat-one", "Repeat track"),
            _ => ("play-in-order", "Play in order")
        };
        var name = Localization.Text("Playback order") + " · " + Localization.Text(label);
        button.ToolTip = button.IsEnabled ? name : Localization.Text("The external player controls playback order.");
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetItemStatus(button, Localization.Text(label));
        if (displayedOrder == order) return;
        displayedOrder = order;
        button.Content = Icons.Create(icon, 17);
    }
}
