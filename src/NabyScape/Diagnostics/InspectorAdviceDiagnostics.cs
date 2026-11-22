using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Lopata.Inspector;

namespace NabyScape;

internal static class InspectorAdviceDiagnostics
{
    internal static async Task Run(App app, OverlayWindow island, string directory)
    {
        var results = new List<string>();
        int failures = 0;
        void Check(string name, bool passed)
        {
            results.Add((passed ? "PASS  " : "FAIL  ") + name);
            if (!passed) failures++;
        }

        try
        {
            var store = new InspectorStore(Path.Combine(app.Store.DirectoryPath, "inspector-host-profile"));
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var notices = Path.Combine(store.DirectoryPath, "notices");
            Directory.CreateDirectory(notices);
            var now = DateTimeOffset.UtcNow;
            var recent = new InspectorNotice(new string('a', 64), now.AddMinutes(-5), "job-recent",
                "Recent inspector advice", Outcome: nameof(InspectorOutcome.Success));
            var pending = new InspectorNotice(new string('b', 64), now.AddMinutes(-10), "job-pending",
                "Pending inspector advice");
            var expired = new InspectorNotice(new string('c', 64), now.AddHours(-24).AddMinutes(-1),
                "job-expired", "Expired inspector advice");
            var undelivered = new InspectorNotice(new string('d', 64), now.AddMinutes(-2), "job-hidden",
                "Undelivered inspector advice") { Shown = false };
            var unknown = new InspectorNotice(new string('e', 64), now.AddMinutes(-15), "job-unknown",
                "Advice with an unknown outcome", Outcome: nameof(InspectorOutcome.Unknown));
            foreach (var fixture in new[] { recent, pending, expired, undelivered, unknown }) WriteFixture(fixture);

            island.DiagnosticRun = true;
            island.Show();
            app.Settings.CompactLayout = true;
            app.ChangeSettings();
            island.OpenInspectorAdvice();
            await UntilAsync(() => Find("Lopata.Inspector.Advice." + recent.Id) is not null);
            Check("Compact island opens a separate inspector advice page",
                island.ActivePage == InspectorAdvicePage.WidgetId && Find("Lopata.Inspector.AdvicePage") is not null);
            Check("Page shows recent delivered advice and excludes expired or undelivered advice",
                Find("Lopata.Inspector.Advice." + recent.Id) is not null &&
                Find("Lopata.Inspector.Advice." + pending.Id) is not null &&
                Find("Lopata.Inspector.Advice." + expired.Id) is null &&
                Find("Lopata.Inspector.Advice." + undelivered.Id) is null);
            Check("Known, pending and unknown outcomes are visible with distinct labels",
                CardShows(recent.Id, "Inspector task finished") && CardShows(pending.Id, "Inspector result pending") &&
                CardShows(unknown.Id, "Inspector result unknown"));

            await EvaluateAsync("Helpful", true);
            Check("Helpful rating is stored for the corresponding card",
                store.ReadNotices().Single(item => item.Id == recent.Id).Helpful == true);
            await EvaluateAsync("NotHelpful", false);
            Check("Changing the rating replaces the verdict and leaves other advice unrated",
                store.ReadNotices().Single(item => item.Id == recent.Id).Helpful == false &&
                store.ReadNotices().Single(item => item.Id == pending.Id).Helpful is null);

            app.Settings.CompactLayout = false;
            app.ChangeSettings();
            island.OpenInspectorAdvice();
            await UntilAsync(() => Find("Lopata.Inspector.Advice." + recent.Id) is not null);
            Check("Classic island opens the same page and shows the saved rating",
                Find("Back to widgets") is Button &&
                Find("Lopata.Inspector.NotHelpful." + recent.Id) is Button selected &&
                AutomationProperties.GetItemStatus(selected) == Localization.Text("Selected"));

            ((Button)Find("Back to widgets")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var fixture in new[] { recent, pending, unknown }) WriteFixture(fixture with
            {
                TimestampUtc = now.AddHours(-25)
            });
            app.Settings.UiLanguage = "ru";
            Localization.Configure(app.Settings);
            app.ChangeSettings();
            island.OpenInspectorAdvice();
            await UntilAsync(() => Diagnostics.Descendants(island).OfType<TextBlock>().Any(text =>
                text.IsVisible && text.Text == "За последние 24 часа советов не было"));
            Check("Empty page is localized in Russian and offers no rating",
                Find("Lopata.Inspector.AdvicePage") is { } empty &&
                AutomationProperties.GetName(empty) == "Советы инспектора за сутки" &&
                Find("Lopata.Inspector.Helpful." + recent.Id) is null);

            void WriteFixture(InspectorNotice fixture) =>
                File.WriteAllText(Path.Combine(notices, fixture.Id + ".json"), JsonSerializer.Serialize(fixture, options));

            UIElement? Find(string id) => Diagnostics.Descendants(island).OfType<UIElement>()
                .FirstOrDefault(element => element.IsVisible && AutomationProperties.GetAutomationId(element) == id);

            bool CardShows(string id, string caption) => Find("Lopata.Inspector.Advice." + id) is { } card &&
                Diagnostics.Descendants(card).OfType<TextBlock>().Any(text => text.Text == Localization.Text(caption));

            async Task EvaluateAsync(string action, bool expected)
            {
                var id = "Lopata.Inspector." + action + "." + recent.Id;
                if (Find(id) is not Button button) throw new InvalidOperationException("Rating control not found.");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await UntilAsync(() => store.ReadNotices().Single(item => item.Id == recent.Id).Helpful == expected &&
                    Find(id) is Button current && AutomationProperties.GetItemStatus(current) == Localization.Text("Selected"));
            }
        }
        catch (Exception error)
        {
            results.Add("FAIL  " + error);
            failures++;
        }
        results.Add($"RESULT: {results.Count - failures} passed, {failures} failed");
        File.WriteAllLines(Path.Combine(directory, "inspector-advice-report.txt"), results);
        Environment.ExitCode = failures == 0 ? 0 : 1;
        app.Quit();
    }

    private static async Task UntilAsync(Func<bool> ready)
    {
        for (int retry = 0; !ready(); retry++)
        {
            if (retry == 80) throw new TimeoutException("Inspector advice UI did not reach the expected state.");
            await Task.Delay(50);
        }
    }
}
