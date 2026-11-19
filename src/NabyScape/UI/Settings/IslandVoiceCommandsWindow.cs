using System.Windows;
using System.Windows.Controls;
using Lopata.Island;

namespace NabyScape;

public sealed partial class SettingsWindow
{
    private void ShowIslandVoiceCommands()
    {
        bool russian = IslandVoicePreferences.IsRussian(Localization.LopataLanguage ?? Localization.Language);
        var body = new StackPanel { Margin = new(18) };
        var scope = UI.Text(Localization.Text(IslandVoiceCommands.ScopeDescription), 12, UI.Muted);
        scope.Margin = new(0, 0, 0, 10);
        body.Children.Add(scope);
        body.Children.Add(UI.Text(Localization.Format("Activation phrase: {0}", IslandVoicePreferences.Read().Wake(russian)), 14));
        body.Children.Add(UI.Text(Localization.Text("Change the activation phrase or reply in the Island utility in LOPATA."), 12, UI.Muted));
        foreach (var entry in IslandVoiceCommands.Entries)
        {
            var title = UI.Text(entry.Example(russian), 14);
            title.Margin = new(0, 10, 0, 2);
            title.FontWeight = FontWeights.SemiBold;
            body.Children.Add(title);
            body.Children.Add(UI.Text(Localization.Text(entry.Description), 12, UI.Muted));
        }
        var window = new Window
        {
            Title = Localization.Text("Voice commands"), Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 550, Height = 620, MinWidth = 330, MinHeight = 260,
            Content = UI.Scroll(body), Background = Background, Foreground = Foreground
        };
        window.ShowDialog();
    }
}
