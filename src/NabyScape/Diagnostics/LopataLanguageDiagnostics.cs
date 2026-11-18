using System;
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
            settings.Close();

            var timerNotice = new InternalNotice("timer", "Timer finished", "Your timer has finished.");
            var externalNotice = new InternalNotice("external", "Timer finished", "Your timer has finished.");
            Require(timerNotice.DisplayTitle == "Таймер завершён" && externalNotice.DisplayTitle == "Timer finished",
                "Island notifications translate without altering messages from other apps");

            var custom = new System.Collections.Generic.Dictionary<string, string>
            {
                ["nothing playing"] = "ПЕРЕВОД ПОЛЬЗОВАТЕЛЯ",
                ["Overview"] = "МОЙ ОБЗОР"
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
}
