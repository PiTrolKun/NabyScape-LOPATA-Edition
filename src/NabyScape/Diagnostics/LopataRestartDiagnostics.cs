using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace NabyScape;

public sealed partial class App
{
    private string[]? restartDiagnosticArguments;

    private async Task RunIslandRestartDiagnostics(OverlayWindow island, string directory, string[] args)
    {
        var report = Path.Combine(directory, "island-restart-check.txt");
        try
        {
            island.DiagnosticRun = true;
            if (args.Contains("--restart-replacement"))
            {
                int firstPid = int.Parse(File.ReadAllText(Path.Combine(directory, "island-restart-parent.txt")));
                bool alive;
                try { using var previous = Process.GetProcessById(firstPid); alive = !previous.HasExited; }
                catch (ArgumentException) { alive = false; }
                var saved = Store.Load<Settings>("settings");
                if (alive || saved.HomeWidget != "notes" || saved.PlaybackOrder != LocalPlaybackOrder.RepeatTrack)
                    throw new InvalidOperationException("The replacement did not wait or lost the saved settings.");
                File.WriteAllText(report, "PASS Replacement started after the original exited, with the same saved profile.");
                Quit();
                return;
            }

            Settings.HomeWidget = "notes";
            Settings.PlaybackOrder = LocalPlaybackOrder.RepeatTrack;
            ChangeSettings();
            File.WriteAllText(Path.Combine(directory, "island-restart-parent.txt"), Environment.ProcessId.ToString());
            restartDiagnosticArguments = ["--isolated-diagnostics", "--diagnostics", directory, "--lopata-restart-only", "--restart-replacement"];
            if (args.Contains("--via-tray"))
            {
                var item = tray!.ContextMenuStrip!.Items.OfType<Forms.ToolStripMenuItem>()
                    .Single(entry => entry.Text == Localization.Text(LopataEdition.RestartCaption));
                item.PerformClick();
            }
            else
            {
                island.Reveal(false);
                await Task.Delay(350);
                var exit = Diagnostics.Descendants(island).OfType<Button>()
                    .Single(button => button.IsVisible && UI.Identity(button) == "Island exit options");
                exit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var item = exit.ContextMenu.Items.OfType<MenuItem>()
                    .Single(entry => Equals(entry.Header, Localization.Text(LopataEdition.RestartCaption)));
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            }
            if (!Quitting) throw new InvalidOperationException("Restart action did not shut down the first process.");
        }
        catch (Exception error)
        {
            File.WriteAllText(report, error.ToString());
            Environment.ExitCode = 1;
            Quit();
        }
    }
}
