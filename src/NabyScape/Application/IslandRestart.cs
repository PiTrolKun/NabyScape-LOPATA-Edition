using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace NabyScape;

internal static class IslandRestart
{
    private const string WaitArgument = "--restart-after-island";

    internal static Process StartReplacement(string executable, Process current, string[]? arguments = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add(WaitArgument);
        start.ArgumentList.Add(current.Id.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        if (arguments is not null)
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("The replacement island process did not start.");
    }

    internal static bool WaitForPrevious(string[] arguments)
    {
        int index = Array.IndexOf(arguments, WaitArgument);
        if (index < 0) return true;
        if (index + 2 >= arguments.Length
            || !int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid)
            || !long.TryParse(arguments[index + 2], NumberStyles.None, CultureInfo.InvariantCulture, out long startTicks)
            || pid <= 0 || pid == Environment.ProcessId)
            return false;
        try
        {
            using var previous = Process.GetProcessById(pid);
            if (previous.HasExited || previous.StartTime.ToUniversalTime().Ticks != startTicks) return true;
            if (!string.Equals(previous.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                return false;
            return previous.WaitForExit(30_000);
        }
        catch (ArgumentException) { return true; }
        catch (InvalidOperationException) { return true; }
        catch (Win32Exception) { return false; }
    }
}
