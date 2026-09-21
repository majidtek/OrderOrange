using System.Threading;
using System.Windows;

namespace LocalHandler;

/// <summary>
/// Application entry. One PC may run one window per partner: launch with
/// <c>--profile &lt;name&gt;</c> (or <c>--profile=&lt;name&gt;</c>) and that window keeps
/// its own login, printers and routing under that name. No argument = the default profile.
/// <c>--tray</c> starts hidden in the notification area (what the watchdog and the
/// sign-in entry use); <c>--quiet</c> exits silently when this profile already runs.
/// </summary>
public partial class App : Application
{
    // Held for the life of the process: a second copy of the same profile (say the old
    // exe left running after a manual unzip, or the watchdog's two-minute launch)
    // would print every ticket twice.
    private static Mutex? _single;

    /// <summary>Start minimised to the tray instead of showing the window.</summary>
    public static bool StartInTray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var profile = "";
        var quiet = false;
        for (var i = 0; i < e.Args.Length; i++)
        {
            var arg = e.Args[i];
            if (arg.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase)) profile = arg["--profile=".Length..].Trim('"');
            else if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length) profile = e.Args[++i].Trim('"');
            else if (arg.Equals("--tray", StringComparison.OrdinalIgnoreCase)) StartInTray = true;
            else if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase)) quiet = true;
        }

        _single = new Mutex(true, $"Local\\OrderOrange.Till.{(profile.Length == 0 ? "default" : profile)}", out var first);
        if (!first)
        {
            if (!quiet)
                MessageBox.Show(profile.Length == 0
                        ? "The OrderOrange till is already running on this PC (look in the tray, next to the clock)."
                        : $"The OrderOrange till for “{profile}” is already running on this PC (look in the tray, next to the clock).",
                    "Local Handler", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var window = new MainWindow(profile);
        if (StartInTray) window.ShowInTray();
        else window.Show();
    }
}
