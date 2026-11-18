using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private ComboBox? homeWidgetChoice;
    private bool refreshingHomeWidgetChoice;

    private ComboBox BuildHomeWidgetChoice()
    {
        var choice = new ComboBox { SelectedValuePath = nameof(FrameworkElement.Tag),
            ToolTip = Localization.Text(LopataHomeWidget.Help) };
        UI.LocalizedName(choice, LopataHomeWidget.Caption);
        homeWidgetChoice = choice;
        RefreshHomeWidgetChoice();
        choice.SelectionChanged += (_, _) =>
        {
            if (!refreshingHomeWidgetChoice && choice.SelectedValue is string page)
                LopataHomeWidget.Select(app, page);
        };
        return choice;
    }

    private void RefreshHomeWidgetChoice()
    {
        if (homeWidgetChoice is not { } choice) return;
        var pages = LopataHomeWidget.Choices(app);
        refreshingHomeWidgetChoice = true;
        try
        {
            if (!choice.Items.OfType<ComboBoxItem>().Select(item => item.Tag).SequenceEqual(pages))
            {
                choice.Items.Clear();
                foreach (string page in pages)
                    choice.Items.Add(new ComboBoxItem { Tag = page, Content = OverlayWindow.PageName(app, page) });
            }
            choice.IsEnabled = pages.Length > 0;
            choice.SelectedValue = LopataHomeWidget.Current(app, pages);
        }
        finally { refreshingHomeWidgetChoice = false; }
    }
}
