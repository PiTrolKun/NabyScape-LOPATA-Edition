using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lopata.Island;

namespace NabyScape;

internal sealed class LopataChatWindow : Window
{
    private readonly LopataConnection link;
    private readonly List<IslandTurn> history = [];
    private readonly TextBox draft = UI.Input(Localization.Text("LOPATA chat message"), "");
    private readonly TextBlock status = UI.Text("", 12, UI.Muted);
    private readonly StackPanel conversation = new();
    private readonly ScrollViewer scroll;
    private readonly Button send;
    private readonly Button stop;
    private readonly StackPanel confirmation = new() { Visibility = Visibility.Collapsed };
    private CancellationTokenSource? running;
    private string? confirmedId;
    private string? draftId;

    internal LopataChatWindow(LopataConnection connection)
    {
        link = connection;
        Title = Localization.Text("LOPATA chat");
        Width = 420;
        Height = 460;
        MinWidth = 340;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "ControlFill");

        var layout = new DockPanel { Margin = new(16) };
        var composer = new StackPanel();
        DockPanel.SetDock(composer, Dock.Bottom);
        layout.Children.Add(composer);
        composer.Children.Add(status);
        composer.Children.Add(confirmation);
        UI.LocalizedName(draft, "LOPATA chat message");
        draft.MinHeight = 62;
        draft.MaxHeight = 120;
        draft.MaxLength = IslandLink.MaximumDraft;
        draft.AcceptsReturn = true;
        draft.TextWrapping = TextWrapping.Wrap;
        draft.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        composer.Children.Add(draft);
        send = UI.CaptionButton("Send", () => _ = SubmitAsync(), true);
        stop = UI.CaptionButton("Stop", () => running?.Cancel());
        composer.Children.Add(UI.Row(send, stop));

        scroll = UI.Scroll(conversation);
        layout.Children.Add(scroll);
        Content = layout;
        draft.TextChanged += (_, _) =>
        {
            draftId = null;
            DismissConfirmation();
            RefreshButtons();
        };
        draft.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control || !send.IsEnabled) return;
            e.Handled = true;
            _ = SubmitAsync();
        };
        Closing += (_, e) =>
        {
            running?.Cancel();
            if (Application.Current is App { Quitting: true }) return;
            e.Cancel = true;
            Hide();
        };
        RefreshButtons();
    }

    internal void FocusDraft() => draft.Focus();

    private async Task SubmitAsync(string? pause = null)
    {
        if (running is not null || string.IsNullOrWhiteSpace(draft.Text)) return;
        var text = draft.Text;
        var id = draftId ??= Guid.NewGuid().ToString("N");
        DismissConfirmation();
        using var operation = new CancellationTokenSource();
        running = operation;
        RefreshButtons();
        ShowStatus("Contacting LOPATA…");
        try
        {
            var response = await link.TransferAsync(id, text, pause, operation.Token);
            if (response.Result == "offline" && pause is null)
            {
                ShowStatus("Waiting for the local core…");
                response = await link.CoreAsync(text, history.TakeLast(16).ToArray(), operation.Token);
                if (response.Result == "host_opened")
                    response = await link.TransferAsync(id, text, null, operation.Token);
            }
            switch (response.Result)
            {
                case "transferred":
                    draft.Clear();
                    ShowStatus("Draft opened in Let it out");
                    Hide();
                    break;
                case "answer" when !string.IsNullOrWhiteSpace(response.Text):
                    AddTurn(new("user", text));
                    AddTurn(new("assistant", response.Text));
                    draft.Clear();
                    ShowStatus("");
                    break;
                case "busy" when response.BlockingId is { } blocking:
                    ConfirmPause(blocking, response.Jobs?.FirstOrDefault(job => job.Id == blocking)?.Title);
                    break;
                default:
                    ShowStatus(response.Result switch
                    {
                        "busy" => "LOPATA is busy. Your draft is still here.",
                        "changed" => "The task changed or did not confirm the pause. Your draft is still here.",
                        "offline" or "unavailable" or "host_opened" => "Cannot connect to the open LOPATA. Your draft is still here.",
                        "cancelled" => "Cancelled. Your draft is still here.",
                        "host_missing" => "LOPATA is not installed alongside the island.",
                        "core_missing" => "Install the LOPATA core in the main app to chat with it here.",
                        "engine_missing" => "Prepare the selected LOPATA engine in the main app.",
                        "preparation" => "Finish preparing Let it out in LOPATA, then try again.",
                        "draft_save_failed" => "LOPATA could not save the draft. Your draft is still here.",
                        _ => "The local core could not answer. Your draft is still here."
                    });
                    break;
            }
        }
        finally
        {
            running = null;
            RefreshButtons();
        }
    }

    private void ConfirmPause(string id, string? title)
    {
        confirmedId = id;
        ShowStatus("A model is working in LOPATA.");
        confirmation.Children.Clear();
        if (!string.IsNullOrWhiteSpace(title)) confirmation.Children.Add(UI.Text(title));
        confirmation.Children.Add(UI.Row(
            UI.CaptionButton("Pause task and continue", () => { if (confirmedId is { } task) _ = SubmitAsync(task); }, true),
            UI.CaptionButton("Cancel", () => { DismissConfirmation(); ShowStatus("Cancelled. Your draft is still here."); RefreshButtons(); })));
        confirmation.Visibility = Visibility.Visible;
    }

    private void DismissConfirmation()
    {
        confirmedId = null;
        confirmation.Visibility = Visibility.Collapsed;
    }

    private void AddTurn(IslandTurn turn)
    {
        history.Add(turn);
        if (history.Count > 16) history.RemoveAt(0);
        conversation.Children.Add(UI.Caption(turn.Role == "user" ? "You" : "LOPATA core", 11, UI.Muted));
        var text = new TextBox { Text = turn.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            BorderThickness = new(0), Margin = new(0, 2, 0, 10), Padding = new(1) };
        conversation.Children.Add(text);
        scroll.ScrollToEnd();
    }

    private void ShowStatus(string caption) => status.Text = Localization.Text(caption);

    private void RefreshButtons()
    {
        draft.IsReadOnly = running is not null;
        send.IsEnabled = running is null && confirmedId is null && !string.IsNullOrWhiteSpace(draft.Text);
        stop.Visibility = running is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
