using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lopata.Inspector;

namespace NabyScape;

public sealed partial class OverlayWindow
{
    internal void OpenInspectorAdvice()
    {
        if (commandOpen) CloseCommands();
        OpenHoverPanel(InspectorAdvicePage.WidgetId, activate: !DiagnosticRun);
    }

    private FrameworkElement BuildInspectorAdvicePage()
    {
        string profileRoot = InspectorProfileRoot();
        return new InspectorAdvicePage(new InspectorStore(profileRoot), app.Settings.PanelMaxHeight, app.Store.Log);
    }

    private string InspectorProfileRoot()
    {
        if (app.DiagnosticMode) return Path.Combine(app.Store.DirectoryPath, "inspector-host-profile");
#if UPDATE_STAND
        return Path.GetFullPath(Environment.GetEnvironmentVariable("LOPATA_UPDATE_STAND_ROOT")
            ?? throw new InvalidOperationException("An isolated update stand root is required by this test build."));
#else
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI_HUB");
#endif
    }
}

internal sealed class InspectorAdvicePage : StackPanel
{
    internal const string WidgetId = "inspector";
    internal const string Title = "Inspector advice for the past 24 hours";
    private const int PageSize = 20;
    private readonly InspectorStore store;
    private readonly Action<Exception> log;
    private readonly TextBlock summary = UI.Caption("Reading inspector advice…", 12, UI.Muted);
    private readonly TextBlock feedback = UI.Text("", 11, UI.Muted);
    private readonly StackPanel cards = new();
    private readonly ScrollViewer viewport;
    private readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromSeconds(8) };
    private InspectorNotice[] shown = [];
    private int visibleCount = PageSize;
    private int revision;
    private bool reading;
    private bool rating;

    internal InspectorAdvicePage(InspectorStore store, double panelHeight, Action<Exception> log)
    {
        this.store = store;
        this.log = log;
        AutomationProperties.SetAutomationId(this, "Lopata.Inspector.AdvicePage");
        AutomationProperties.SetName(this, Localization.Text(Title));
        var heading = UI.Heading(Localization.Text(Title), 17);
        heading.TextWrapping = TextWrapping.Wrap;
        Children.Add(heading);
        summary.Margin = new(0, 8, 0, 0);
        Children.Add(summary);
        feedback.TextWrapping = TextWrapping.Wrap;
        feedback.Visibility = Visibility.Collapsed;
        Children.Add(feedback);
        viewport = UI.Scroll(cards);
        viewport.Tag = "panel-content-scroll";
        viewport.Margin = new(0, 8, 0, 0);
        viewport.MaxHeight = Math.Clamp(panelHeight - 140, 105, 340);
        viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Children.Add(viewport);
        poll.Tick += (_, _) => _ = RefreshAsync();
        Loaded += (_, _) => UpdatePolling();
        IsVisibleChanged += (_, _) => UpdatePolling();
        Unloaded += (_, _) => poll.Stop();
    }

    private void UpdatePolling()
    {
        if (!IsLoaded || !IsVisible)
        {
            poll.Stop();
            return;
        }
        poll.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (reading || rating || !IsVisible) return;
        int requested = revision;
        reading = true;
        try
        {
            var notices = await Task.Run(store.ReadNotices);
            if (requested == revision && IsVisible)
            {
                ShowSummary(notices.Length == 0 ? "No advice in the past 24 hours" : "");
                if (!shown.SequenceEqual(notices))
                {
                    shown = notices;
                    RenderCards();
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or TimeoutException)
        {
            log(error);
            if (requested == revision && IsVisible) ShowSummary("Inspector advice is temporarily unavailable");
        }
        finally { reading = false; }
    }

    private async void Rate(InspectorNotice notice, bool helpful)
    {
        if (rating) return;
        rating = true;
        revision++;
        ShowFeedback("");
        try
        {
            var (saved, latest) = await Task.Run(() => (store.Rate(notice.Id, helpful), store.ReadNotices()));
            if (!IsLoaded) return;
            shown = latest;
            ShowSummary(latest.Length == 0 ? "No advice in the past 24 hours" : "");
            RenderCards();
            if (!saved)
                ShowFeedback(latest.Any(item => item.Id == notice.Id)
                    ? "Your rating was not saved. Try again."
                    : "This advice is no longer available for rating.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or TimeoutException)
        {
            log(error);
            if (IsLoaded) ShowFeedback("Your rating was not saved. Try again.");
        }
        finally { rating = false; }
    }

    private void ShowSummary(string message)
    {
        summary.Text = Localization.Text(message);
        summary.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowFeedback(string message)
    {
        feedback.Text = Localization.Text(message);
        feedback.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderCards()
    {
        string? focusedId = IsKeyboardFocusWithin && Keyboard.FocusedElement is Button focused
            ? AutomationProperties.GetAutomationId(focused) : null;
        double previousOffset = viewport.VerticalOffset;
        Button? nextFocus = null;
        cards.Children.Clear();
        foreach (var notice in shown.Take(visibleCount))
        {
            var text = UI.Text(notice.Text, 13);
            text.TextWrapping = TextWrapping.Wrap;
            var outcome = UI.Caption(OutcomeText(notice.Outcome), 11, UI.Muted);
            outcome.Margin = new(0, 4, 0, 7);
            var positive = RatingButton(notice, true);
            var negative = RatingButton(notice, false);
            var actions = new WrapPanel { Children = { positive, negative } };
            var time = notice.TimestampUtc.ToLocalTime().ToString("g", Localization.Culture);
            var card = UI.Card(time, text, outcome, actions);
            AutomationProperties.SetAutomationId(card, "Lopata.Inspector.Advice." + notice.Id);
            cards.Children.Add(card);
            foreach (var button in new[] { positive, negative })
                if (AutomationProperties.GetAutomationId(button) == focusedId) nextFocus = button;
        }
        if (shown.Length > visibleCount)
            cards.Children.Add(UI.CaptionButton("Show earlier advice", () =>
            {
                visibleCount += PageSize;
                RenderCards();
            }));
        viewport.ScrollToVerticalOffset(previousOffset);
        if (nextFocus is { } matching)
            Dispatcher.InvokeAsync(() => { if (matching.IsVisible) matching.Focus(); }, DispatcherPriority.Loaded);
    }

    private Button RatingButton(InspectorNotice notice, bool helpful)
    {
        bool selected = notice.Helpful == helpful;
        var button = UI.CaptionButton(helpful ? "Helpful" : "Not helpful", () => Rate(notice, helpful), primary: selected);
        button.Margin = new(0, 0, 5, 3);
        AutomationProperties.SetAutomationId(button, "Lopata.Inspector." + (helpful ? "Helpful." : "NotHelpful.") + notice.Id);
        AutomationProperties.SetItemStatus(button, selected ? Localization.Text("Selected") : "");
        return button;
    }

    internal static string OutcomeText(string? outcome) => outcome switch
    {
        null => "Inspector result pending",
        nameof(InspectorOutcome.Success) => "Inspector task finished",
        nameof(InspectorOutcome.Cancelled) => "Inspector task cancelled",
        nameof(InspectorOutcome.Failure) => "Inspector task failed",
        nameof(InspectorOutcome.OutOfVideoMemory) => "Inspector ran out of video memory",
        nameof(InspectorOutcome.OutOfMemory) => "Inspector ran out of system memory",
        nameof(InspectorOutcome.SaveFailure) => "Inspector failed to save the result",
        nameof(InspectorOutcome.Disconnected) => "Inspector lost contact with the engine",
        nameof(InspectorOutcome.CpuRetry) => "Inspector retry on CPU",
        _ => "Inspector result unknown"
    };
}
