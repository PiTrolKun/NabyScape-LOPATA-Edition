using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Lopata.Island;

namespace NabyScape;

internal sealed class IslandVoiceTranscriptWindow : Window, IDisposable
{
    internal const string EnvironmentFlag = "LOPATA_ISLAND_VOICE_TRACE";
    private readonly bool russian;
    private readonly TextBlock wake;
    private readonly TextBox commands;
    private readonly Queue<string> recent = new();

    internal IslandVoiceTranscriptWindow(bool russian)
    {
        this.russian = russian;
        Title = russian ? "Проверка распознавания острова" : "Island speech recognition test";
        Width = 460;
        Height = 320;
        MinWidth = 320;
        MinHeight = 240;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - Width - 24);
        Top = Math.Max(area.Top, area.Bottom - Height - 24);
        Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UI.Brush("#171C24");
        Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UI.Brush("#F0F3F7");

        var panel = new DockPanel { Margin = new(14) };
        wake = UI.Text("—", 14, Foreground);
        wake.TextWrapping = TextWrapping.Wrap;
        var header = new StackPanel();
        header.Children.Add(UI.Label(russian ? "Ожидание фразы активации" : "Waiting for the activation phrase"));
        header.Children.Add(wake);
        var label = UI.Label(russian ? "Последние команды" : "Recent commands");
        label.Margin = new Thickness(0, 16, 0, 6);
        header.Children.Add(label);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        commands = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Padding = new(8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetAutomationId(wake, "Lopata.IslandVoice.WakeTranscript");
        AutomationProperties.SetAutomationId(commands, "Lopata.IslandVoice.CommandTranscripts");
        panel.Children.Add(commands);
        Content = panel;
        Closed += (_, _) =>
        {
            recent.Clear();
            commands.Clear();
            wake.Text = "—";
        };
    }

    internal static IslandVoiceTranscriptWindow? OpenForTest(bool russian)
    {
        if (Environment.GetEnvironmentVariable(EnvironmentFlag) != "1") return null;
        var window = new IslandVoiceTranscriptWindow(russian);
        window.Show();
        return window;
    }

    internal void ShowWake(string? text)
    {
        if (IsVisible && !string.IsNullOrWhiteSpace(text) && wake.Text != text)
            wake.Text = text[..Math.Min(text.Length, 240)];
    }

    internal void ShowCommand(string? text, IslandVoiceCommand? match, int? previousVolume = null, int? volume = null)
    {
        var description = match is null
            ? (russian ? "Команда не выбрана" : "No matching command")
            : $"{ActionLabel(match)} · {match.Similarity}%";
        var heard = string.IsNullOrWhiteSpace(text)
            ? (russian ? "Распознаватель вернул пустой текст" : "Recognizer returned no text")
            : $"«{text[..Math.Min(text.Length, 350)]}»";
        string result = previousVolume is { } before && volume is { } after
            ? Environment.NewLine + VolumeResult(before, after) : "";
        Append($"{heard}{Environment.NewLine}{description}{result}");
    }

    internal void ShowNoSpeech() => Append(russian ? "Микрофон не уловил команду за время ожидания."
        : "No speech was detected in the command window.");

    private string ActionLabel(IslandVoiceCommand match) => match.Action switch
    {
        IslandVoiceAction.Volume => russian ? $"Громкость плеера {match.Value}%" : $"Player volume {match.Value}%",
        IslandVoiceAction.SeekAbsolute => russian ? $"Позиция {match.Value} с" : $"Seek to {match.Value} s",
        IslandVoiceAction.SeekRelative => russian ? $"Перемотка {match.Value:+#;-#;0} с" : $"Seek {match.Value:+#;-#;0} s",
        _ => IslandVoiceCommands.Entries.First(entry => entry.Action == match.Action).Example(russian)
    };

    private string VolumeResult(int before, int after)
    {
        if (before != after) return russian ? $"Громкость плеера {before}% → {after}%" : $"Player volume {before}% → {after}%";
        return russian ? $"Громкость плеера уже {after}%" : $"Player volume is already {after}%";
    }

    private void Append(string line)
    {
        if (!IsVisible) return;
        recent.Enqueue(line);
        if (recent.Count > 12) recent.Dequeue();
        commands.Text = string.Join(Environment.NewLine + Environment.NewLine, recent);
        commands.ScrollToEnd();
    }

    public void Dispose()
    {
        if (IsVisible) Close();
    }
}
