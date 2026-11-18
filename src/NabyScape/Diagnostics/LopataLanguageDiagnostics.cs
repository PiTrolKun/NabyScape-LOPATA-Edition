using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lopata.Island;

namespace NabyScape;

internal static class LopataLanguageDiagnostics
{
    internal static async Task Run(App app, string directory)
    {
        Directory.CreateDirectory(directory);
        var report = Path.Combine(directory, "localization-checks.txt");
        try
        {
            void Require(bool result, string check)
            {
                if (!result) throw new InvalidOperationException(check);
                File.AppendAllText(report, "PASS " + check + Environment.NewLine);
            }
            bool HasText(DependencyObject source, string caption) =>
                Diagnostics.Descendants(source).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == caption);

            var island = (OverlayWindow)app.MainWindow;
            island.DiagnosticRun = true;
            app.Media.DiagnosticFixture = false;
            app.Media.SetEnabled(false);
            await Task.Delay(1150);
            Require(Localization.Language == "ru" && Localization.Culture.TwoLetterISOLanguageName == "ru", "Russian language and date culture");
            var date = DateTime.Today;
            Capture(island, "folded-ru");
            var month = date.ToString("MMM", Localization.Culture).ToUpper(Localization.Culture);
            var day = date.ToString("ddd", Localization.Culture).ToUpper(Localization.Culture);
            var visibleLabels = Diagnostics.Descendants(island).OfType<TextBlock>().Where(label => label.IsVisible)
                .Select(label => string.Concat(label.Inlines.OfType<Run>().Select(run => run.Text))).ToArray();
            Require(visibleLabels.Any(label => label.Contains(day) && label.Contains(month)),
                "Russian date in the folded island. Visible labels: " + string.Join(" | ", visibleLabels));

            island.Reveal(false);
            await Task.Delay(650);
            Require(HasText(island, Localization.Text("Nothing playing")), "Russian default playback title");
            Require(Localization.Text("No lyrics yet") == "Текст песни пока недоступен" &&
                new LyricResult([], "Synced lyrics · LRCLIB").DisplayStatus == "Текст с таймингами · LRCLIB",
                "Lyrics placeholders translate while provider identity is preserved");
            Capture(island, "player-ru");

            island.OpenFeaturePage("system");
            await Task.Delay(650);
            Require(Diagnostics.Descendants(island).OfType<Button>().Any(button =>
                button.IsVisible && button.Content is string caption && caption == Localization.Text("Hardware")),
                "The system monitor shows translated tabs");
            Capture(island, "system-ru");
            island.OpenFeaturePage("media");

            app.ShowSettings("Appearance");
            await Task.Delay(350);
            var settings = app.Windows.Cast<Window>().OfType<SettingsWindow>().Single();
            Require(HasText(settings, Localization.Text("Preview")), "Russian settings preview");
            Require(HasText(settings, "Раздел «Вид и цвета»") && !HasText(settings, "Appearance overview"),
                "The settings preview explains the current section in Russian");
            Capture(settings, "settings-ru");
            settings.OpenCategory("Application");
            Require(Diagnostics.Descendants(settings).OfType<ComboBox>().Any(choice => !choice.IsEnabled &&
                System.Windows.Automation.AutomationProperties.GetName(choice) == Localization.Text("Interface language")),
                "Island settings respect the language selected in LOPATA");
            var previousDetailMode = app.Settings.SettingsShowAll;
            app.Settings.SettingsShowAll = true;
            foreach (var category in new[] { "Home", "Appearance", "Ambience", "Motion", "Layout", "Visibility", "Controls", "Content",
                "Clipboard", "Audio", "Weather", "System", "Launch", "AI", "Notifications", "Application" })
            {
                settings.OpenCategory(category);
                settings.UpdateLayout();
                File.WriteAllLines(Path.Combine(directory, "settings-" + category + ".txt"), SettingsCaptions(settings).Distinct());
                if (category == "Ambience")
                {
                    var captions = SettingsCaptions(settings).ToArray();
                    Require(captions.Contains("Где показывать подсветку") && captions.Contains("Все виджеты") &&
                        captions.Contains("Смешение цветов обложки") && captions.Contains("Добавляет мягкое цветное свечение за виджетами."),
                        "Native settings translate the label, inactive list options and help text");
                    var scope = SettingsElements(settings).OfType<ComboBox>().Single(choice => UI.Identity(choice) == "Ambient scope");
                    var previous = scope.SelectedValue;
                    scope.SelectedValue = "Selected widgets";
                    settings.UpdateLayout();
                    for (int i = 0; i < 20 && app.Store.Load<Settings>("settings").AmbientScope != "Selected widgets"; i++)
                        await Task.Delay(50);
                    Require(app.Settings.AmbientScope == "Selected widgets" && app.Store.Load<Settings>("settings").AmbientScope == "Selected widgets",
                        "Selecting a translated option saves its original setting value");
                    scope.SelectedValue = previous;
                    scope.BringIntoView();
                    settings.UpdateLayout();
                    Capture(settings, "ambience-ru");
                }
            }
            app.Settings.SettingsShowAll = previousDetailMode;
            settings.Close();

            var timerNotice = new InternalNotice("timer", "Timer finished", "Your timer has finished.");
            var externalNotice = new InternalNotice("external", "Timer finished", "Your timer has finished.");
            Require(timerNotice.DisplayTitle == "Таймер завершён" && externalNotice.DisplayTitle == "Timer finished",
                "Island notifications translate without altering messages from other apps");

            var custom = new System.Collections.Generic.Dictionary<string, string>
            {
                ["nothing playing"] = "ПЕРЕВОД ПОЛЬЗОВАТЕЛЯ",
                ["Overview"] = "МОЙ ОБЗОР",
                ["Where the light appears"] = "МОЯ ПОДПИСЬ ПОДСВЕТКИ",
                ["All widgets"] = "МОЙ СПИСОК ВИДЖЕТОВ"
            };
            for (int i = 0; i < 800; i++) custom["fixture" + i] = new string('Ж', 72);
            IslandPreferences.SaveJson("language.json", new IslandLanguage("ru-RU", custom));
            for (int i = 0; i < 20 && ReferenceEquals(island, app.MainWindow); i++) await Task.Delay(100);
            var translated = (OverlayWindow)app.MainWindow;
            translated.DiagnosticRun = true;
            translated.Reveal(false);
            await Task.Delay(650);
            Require(Localization.Language == "ru" && Localization.Culture.Name == "ru-RU" && Localization.Text("Overview") == custom["Overview"] &&
                Localization.Text("Nothing playing") == custom["nothing playing"], "Large user dictionary overrides island captions case-insensitively");
            Capture(translated, "custom-live");
            Require(!ReferenceEquals(island, translated) && HasText(translated, custom["nothing playing"]),
                "The running island shows the new title. Visible labels: " + string.Join(" | ",
                    Diagnostics.Descendants(translated).OfType<TextBlock>().Where(label => label.IsVisible).Select(label => label.Text)));
            app.ShowSettings("Ambience");
            var customSettings = app.Windows.Cast<Window>().OfType<SettingsWindow>().Single();
            var customCaptions = SettingsCaptions(customSettings).ToArray();
            Require(customCaptions.Contains(custom["Where the light appears"]) && customCaptions.Contains(custom["All widgets"]),
                "User translations override native settings and options from the shared dictionary");
            customSettings.Close();

            IslandPreferences.SaveJson("language.json", new IslandLanguage("en", new()));
            for (int i = 0; i < 20 && Localization.Language != "en"; i++) await Task.Delay(100);
            Require(Localization.Text("Nothing playing") == "Nothing playing" && Localization.Culture.TwoLetterISOLanguageName == "en",
                "Switching to English clears prior overrides");
        }
        catch (Exception error)
        {
            File.AppendAllText(report, error.ToString());
            Environment.ExitCode = 1;
        }
        finally { app.Quit(); }

        void Capture(FrameworkElement element, string name)
        {
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var image = new PngBitmapEncoder();
            image.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name + ".png"));
            image.Save(file);
        }
    }

    private static IEnumerable<DependencyObject> SettingsElements(DependencyObject node)
    {
        yield return node;
        if (node is not FrameworkElement and not FrameworkContentElement) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var item in SettingsElements(child)) yield return item;
    }

    private static IEnumerable<string> SettingsCaptions(DependencyObject root)
    {
        foreach (var node in SettingsElements(root))
        {
            if (node is TextBlock { Text.Length: > 0 } text) yield return text.Text;
            if (node is HeaderedContentControl { Header: string header }) yield return header;
            if (node is ContentControl { Content: string caption }) yield return caption;
            if (node is FrameworkElement { ToolTip: string tip }) yield return tip;
            if (node is ComboBox combo)
                foreach (var option in combo.Items.OfType<string>()) yield return option;
        }
    }
}
