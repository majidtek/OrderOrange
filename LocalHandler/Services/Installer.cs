using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace LocalHandler.Services;

/// <summary>
/// Makes the Windows till installable (Majed 2026-10-22: "make windows app installable").
///
/// The download is ONE self-contained exe. Run from wherever the browser saved it, it offers
/// to install itself: it copies itself to <c>%LOCALAPPDATA%\Programs\OrderOrange Till</c>, adds
/// Start menu and desktop shortcuts, registers in Settings › Apps (with Uninstall), and starts
/// the installed copy. Everything is per user, so no administrator rights are ever asked for,
/// and the self-update (<see cref="UpdateService"/>) can keep writing into the same folder.
///
/// A till that was unzipped by hand (the older download, a folder of files) is left exactly
/// as it is: it never asks, and keeps updating in place.
/// </summary>
public static class Installer
{
    public const string AppName = "OrderOrange Till";
    private const string Publisher = "OrderOrange";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OrderOrangeTill";
    private const string PrefsKey = @"Software\OrderOrange\Till";

    public static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    private static string InstalledExe => Path.Combine(InstallDir, "LocalHandler.exe");

    private static string CurrentExe =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LocalHandler.exe");

    private static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");

    private static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    /// <summary>A single-file build has no assembly path on disk: that is the downloaded installer.</summary>
    // IL3000 warns that Location is empty in a single-file app; that emptiness is exactly the test.
#pragma warning disable IL3000
    private static bool IsSingleFile => string.IsNullOrEmpty(typeof(Installer).Assembly.Location);
#pragma warning restore IL3000

    private static bool RunningInstalled =>
        string.Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), InstallDir.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs before anything else on start. Returns true when this process should exit: the
    /// installed copy was started in its place, the person cancelled, or an uninstall ran.
    /// </summary>
    public static bool HandleStartup(string[] args)
    {
        bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

        if (Has("--uninstall")) { Uninstall(); return true; }

        if (RunningInstalled)
        {
            // After a self-update the version in Settings › Apps must follow.
            Register();
            return false;
        }

        // Background starts never ask; neither does a hand-unzipped folder, nor a copy the
        // person already chose to run where it is.
        if (!IsSingleFile || Has("--tray") || Has("--quiet") || Has("--portable") || DeclinedHere()) return false;

        var answer = MessageBox.Show(
            "Install the OrderOrange till on this PC?\n\n" +
            "It is added to the Start menu and the desktop, starts with Windows and keeps itself up to date. " +
            "No administrator rights are needed.\n\n" +
            "Yes: install (recommended)\nNo: run it from here without installing",
            AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);

        if (answer == MessageBoxResult.Cancel) return true;
        if (answer == MessageBoxResult.No) { RememberDeclined(); return false; }

        try
        {
            Install();
        }
        catch (Exception ex)
        {
            TillLog.Write($"install failed: {ex.Message}");
            MessageBox.Show($"The till could not be installed:\n{ex.Message}\n\nIt will run from here instead.",
                AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = true, WorkingDirectory = InstallDir });
        return true;
    }

    private static void Install()
    {
        // A till already running from the install folder (an older version) holds the exe.
        StopOthersIn(InstallDir);
        Directory.CreateDirectory(InstallDir);
        CopyWithRetry(CurrentExe, InstalledExe);

        CreateShortcut(StartMenuShortcut);
        CreateShortcut(DesktopShortcut);
        Register();
        TillLog.Write($"installed v{UpdateService.Current} to {InstallDir}");
    }

    /// <summary>The entry in Settings › Apps (and the classic Programs and Features).</summary>
    private static void Register()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true);
            if (key is null) return;
            key.SetValue("DisplayName", AppName);
            key.SetValue("DisplayVersion", UpdateService.Current);
            key.SetValue("Publisher", Publisher);
            key.SetValue("DisplayIcon", $"\"{InstalledExe}\",0");
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
            key.SetValue("URLInfoAbout", "https://partner.orderorange.com");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            if (File.Exists(InstalledExe))
                key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }
        catch { /* a locked-down account: the till still runs, it is just not listed */ }
    }

    /// <summary>
    /// Settings › Apps › Uninstall lands here. Takes the till out of autostart for every
    /// profile, removes the shortcuts and the Apps entry, and deletes the program folder once
    /// this process has exited. Logins, printers and routing (in %LOCALAPPDATA%\OrderOrange)
    /// are kept, so reinstalling picks up where the shop left off.
    /// </summary>
    private static void Uninstall()
    {
        var sure = MessageBox.Show(
            "Remove the OrderOrange till from this PC?\n\nTickets and receipts will stop printing here. " +
            "Your sign-in, printers and routing are kept in case you install it again.",
            AppName, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (sure != MessageBoxResult.Yes) return;

        foreach (var profile in Models.Setup.KnownProfiles())
            Autostart.Apply(profile, enabled: false);
        StopOthersIn(InstallDir);

        TryDelete(StartMenuShortcut);
        TryDelete(DesktopShortcut);
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); } catch { }
        TillLog.Write("uninstalled");

        // The folder holds this very exe, so it goes after this process exits.
        var script = Path.Combine(Path.GetTempPath(), "orderorange-till-uninstall.cmd");
        File.WriteAllText(script, $"""
            @echo off
            :wait
            tasklist /FI "PID eq {Environment.ProcessId}" 2>nul | find "{Environment.ProcessId}" >nul
            if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)
            rmdir /s /q "{InstallDir}"
            del "%~f0"
            """);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath(),
        });

        MessageBox.Show("The OrderOrange till has been removed.", AppName, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Other till windows running from the install folder (other profiles, an older copy).</summary>
    private static void StopOthersIn(string dir)
    {
        foreach (var p in Process.GetProcessesByName("LocalHandler"))
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;
                var path = p.MainModule?.FileName;
                if (path is not null && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill();
                    p.WaitForExit(5000);
                }
            }
            catch { /* not ours to inspect, or already gone */ }
            finally { p.Dispose(); }
        }
    }

    private static void CopyWithRetry(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { File.Copy(from, to, overwrite: true); return; }
            catch (IOException) when (attempt < 10) { Thread.Sleep(500); }
        }
    }

    /// <summary>A .lnk through the Windows shell's own COM object, so no extra library is needed.</summary>
    private static void CreateShortcut(string path)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var link = shell.CreateShortcut(path);
            link.TargetPath = InstalledExe;
            link.WorkingDirectory = InstallDir;
            link.IconLocation = InstalledExe + ",0";
            link.Description = AppName;
            link.Save();
        }
        catch { /* no shell (a server core box): the Apps entry still works */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    // A person who chose "run it from here" is not asked again for that same file.
    private static bool DeclinedHere()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrefsKey);
            return string.Equals(key?.GetValue("RunWithoutInstall") as string, CurrentExe, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void RememberDeclined()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PrefsKey, writable: true);
            key?.SetValue("RunWithoutInstall", CurrentExe);
        }
        catch { }
    }
}
