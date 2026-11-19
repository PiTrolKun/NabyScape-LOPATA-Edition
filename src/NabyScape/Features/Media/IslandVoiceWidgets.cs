using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lopata.Island;

namespace NabyScape;

public sealed partial class App
{
    private async Task<IslandVoiceOutcome> ExecuteVoiceWidgetAsync(IslandVoiceCommand command, CancellationToken token)
    {
        switch (command.Action)
        {
            case IslandVoiceAction.StartTimer:
                Session.Timer.Start(command.Value, DateTimeOffset.UtcNow);
                SaveSession();
                return Applied(Localization.Format("Timer set for {0}.", Countdown.FormatSeconds(command.Value)));
            case IslandVoiceAction.TimerRemaining:
                var seconds = Session.Timer.Remaining(DateTimeOffset.UtcNow);
                return seconds <= 0 ? Rejected("The timer has no time remaining.")
                    : Applied(Localization.Format(Session.Timer.Running ? "Time remaining: {0}." : "Timer paused with {0} remaining.",
                        Countdown.FormatSeconds(seconds)));
            case IslandVoiceAction.StartNote:
                return new(IslandVoiceDisposition.Clarification, Localization.Text("What should I write?"), IslandVoiceFollowUp.Note);
            case IslandVoiceAction.CancelNote:
                return Applied(Localization.Text("Note cancelled."));
            case IslandVoiceAction.AddNote:
                string previous = Productivity.Note;
                string note = string.IsNullOrEmpty(previous) ? command.Query : previous + Environment.NewLine + command.Query;
                if (note.Length > 50000) return Rejected("There is no space left in the island notes.");
                Productivity.Note = note;
                if (Productivity.Save()) return Applied(Localization.Text("Added to the island notes."));
                Productivity.Note = previous;
                return Rejected("Could not save the island note.");
            case IslandVoiceAction.ShowNotes:
                overlay?.OpenVoiceWidget("notes");
                return Applied();
            case IslandVoiceAction.ShowWeather:
                overlay?.OpenVoiceWidget("weather");
                return Applied();
            case IslandVoiceAction.RainForecast:
                return RainForecast();
            case IslandVoiceAction.ShowStatus:
                overlay?.OpenVoiceWidget("system");
                return Applied();
            case IslandVoiceAction.MemoryUsed:
                var sample = await Task.Run(() => SystemMonitor.Capture(false, false, false, processCounts: false, hardware: false), token);
                if (sample.Total is not { } total || sample.Available is not { } available || available > total || total == 0)
                    return Rejected("Memory usage is unavailable.");
                const double gib = 1024d * 1024d * 1024d;
                return Applied(Localization.Format("Memory used: {0:0.0} of {1:0.0} gigabytes.", (total - available) / gib, total / gib));
            case IslandVoiceAction.ShowTracks:
                overlay?.OpenVoiceTrackList();
                return Applied();
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private IslandVoiceOutcome RainForecast()
    {
        if (Weather.Saved(Settings) is not { } forecast) return Rejected("No forecast is available. Choose a city in the weather widget.");
        return Applied(DescribeRainForecast(forecast, DateTimeOffset.UtcNow));
    }

    internal static string DescribeRainForecast(WeatherSnapshot forecast, DateTimeOffset now)
    {
        var hours = WeatherSchedule.Upcoming(forecast, now, 6)
            .Where(hour => WeatherSchedule.LocalStamp(hour.Stamp) is not null && double.IsFinite(hour.RainChance)).ToArray();
        string updated = forecast.Updated.ToLocalTime().ToString("g", Localization.Culture);
        if (hours.Length == 0) return Localization.Format("No rain forecast for the upcoming hours. Last update: {0}.", updated);
        var probability = Math.Clamp(hours.Max(hour => hour.RainChance), 0, 100);
        return Localization.Format("Chance of rain from {0} through {1}: up to {2:0} percent. Forecast updated {3}.",
            WeatherSchedule.LocalStamp(hours[0].Stamp)!.Value.ToString("g", Localization.Culture),
            WeatherSchedule.LocalStamp(hours[^1].Stamp)!.Value.ToString("g", Localization.Culture), probability, updated);
    }
}

public sealed partial class OverlayWindow
{
    internal void OpenVoiceWidget(string id)
    {
        if (commandOpen) CloseCommands();
        if (showLopataJobs) lopataWork?.ShowDetails(false);
        if (id is "notes" or "weather")
        {
            toolsMode = id;
            calendarEditing = false;
        }
        OpenHoverPanel(id);
        if (id is "notes" or "weather")
        {
            toolsMode = id;
            RefreshTools();
        }
    }

    internal void OpenVoiceTrackList()
    {
        OpenVoiceWidget("media");
        OpenLocalMusic(app.MusicLists.Selected);
    }

    internal void OpenVoiceWorkPanel()
    {
        if (commandOpen) CloseCommands();
        Reveal();
        if (app.Settings.CompactLayout && lopataWork is not null) lopataWork.ShowDetails(true);
        else
        {
            var panel = new LopataWorkPanel(app, new System.Windows.Controls.DockPanel(), true, _ => { });
            var window = new System.Windows.Window
            {
                Owner = this, Topmost = Topmost, Width = 440, SizeToContent = System.Windows.SizeToContent.Height,
                Title = Localization.Text("LOPATA tasks"), Content = panel, Background = UI.Brush("#151B25"),
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, ShowInTaskbar = false
            };
            void Refresh() => panel.Refresh(app.LopataLink.State);
            app.LopataLink.Changed += Refresh;
            window.Closed += (_, _) => app.LopataLink.Changed -= Refresh;
            Refresh();
            window.Show();
        }
        scroll.ScrollToTop();
    }
}
