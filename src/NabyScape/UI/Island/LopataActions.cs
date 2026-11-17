using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    private void AddLopataActions(Panel panel)
    {
        var actions = new System.Windows.Controls.Primitives.UniformGrid
        {
            Rows = 1,
            Margin = new Thickness(0, 0, 0, 10)
        };
        void Add(string caption, System.Action action)
        {
            var button = UI.CaptionButton(caption, action);
            button.FontSize = 11;
            button.MinWidth = 0;
            button.Padding = new Thickness(4, 5, 4, 5);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            actions.Children.Add(button);
        }
        Add(LopataEdition.OpenCaption, LopataEdition.OpenHost);
        Add(LopataEdition.LayerCaption, app.ToggleLopataLayer);
        Add(LopataEdition.CloseCaption, app.Quit);
        panel.Children.Add(actions);
        AddLopataWork(panel);
    }
}
