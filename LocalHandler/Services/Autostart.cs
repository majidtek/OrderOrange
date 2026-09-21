using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LocalHandler.Services;

/// <summary>
/// Makes the till behave like a service without being one (a Windows service has no
/// desktop, and printing needs one): it starts with Windows, and a scheduled task
/// re-launches it every two minutes if it is not running — the single-instance lock in
/// <see cref="App"/> turns the extra launches into silent no-ops. One Run entry and one
/// task per partner profile, each with its own <c>--profile</c>. HKCU and a user-level
/// task need no admin rights; both are re-written on every start so an update or a
/// moved folder never leaves a stale path behind.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string EntryName(string profile) =>
        profile.Length == 0 ? "OrderOrange Till" : $"OrderOrange Till ({profile})";

    private static string Exe =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LocalHandler.exe");

    public static void Apply(string profile, bool enabled)
    {
        ApplyRunEntry(profile, enabled);
        ApplyWatchdog(profile, enabled);
    }

    /// <summary>Start at sign-in, window visible.</summary>
    private static void ApplyRunEntry(string profile, bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (run is null) return;
            var name = EntryName(profile);
            if (!enabled) { run.DeleteValue(name, throwOnMissingValue: false); return; }
            var command = profile.Length == 0 ? $"\"{Exe}\" --tray" : $"\"{Exe}\" --profile=\"{profile}\" --tray";
            run.SetValue(name, command, RegistryValueKind.String);
        }
        catch { /* a locked-down account simply starts the till by hand */ }
    }

    /// <summary>
    /// Every two minutes: launch the till. If it is already running the launch exits at
    /// once (mutex); if it crashed or was closed, it is back within two minutes, in the
    /// tray, printing. This is the "service" part.
    /// </summary>
    private static void ApplyWatchdog(string profile, bool enabled)
    {
        var task = EntryName(profile) + " watchdog";
        try
        {
            if (!enabled)
            {
                Run("schtasks", $"/Delete /F /TN \"{task}\"");
                return;
            }
            var args = profile.Length == 0 ? "--tray --quiet" : $"--profile=\\\"{profile}\\\" --tray --quiet";
            // /TR is one quoted string: the exe path in escaped quotes, then the switches.
            Run("schtasks", $"/Create /F /SC MINUTE /MO 2 /TN \"{task}\" /TR \"\\\"{Exe}\\\" {args}\"");
        }
        catch { /* no task scheduler access — the Run entry still starts it at sign-in */ }
    }

    private static void Run(string file, string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        });
        p?.WaitForExit(15000);
    }
}
