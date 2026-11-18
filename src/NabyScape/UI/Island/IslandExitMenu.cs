using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private ContextMenu? islandExitMenu;

    private Button BuildIslandExitButton()
    {
        var menu = new ContextMenu { Placement = PlacementMode.Bottom };
        var button = LopataActions.Button("Island exit options", "x", () => menu.IsOpen = !menu.IsOpen, size: 28);
        menu.PlacementTarget = button;
        void Add(string label, Action command)
        {
            var item = new MenuItem { Header = Localization.Text(label) };
            item.Click += (_, _) => command();
            UI.Named(item, Localization.Text(label));
            menu.Items.Add(item);
        }
        Add(LopataEdition.CloseCaption, app.Quit);
        Add(LopataEdition.RestartCaption, app.RestartIsland);
        menu.Opened += (_, _) => AutomationProperties.SetItemStatus(button, "Expanded");
        menu.Closed += (_, _) => AutomationProperties.SetItemStatus(button, "");
        button.Unloaded += (_, _) =>
        {
            menu.IsOpen = false;
            if (ReferenceEquals(islandExitMenu, menu)) islandExitMenu = null;
        };
        button.Loaded += (_, _) => islandExitMenu = menu;
        button.ContextMenu = menu;
        islandExitMenu = menu;
        return button;
    }
}
