using Android.App;
using Android.Content;
using OrderOrange.PartnerApp.Till.Services;

namespace OrderOrange.PartnerApp;

/// <summary>
/// Brings the till back by itself after the terminal reboots or the app is updated, so
/// nobody has to remember to open it before the first order of the day (Majed 2026-10-18:
/// "we need make mobile app working in background"). Only when a session was saved — a
/// terminal that was never signed in has nothing to poll.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true, DirectBootAware = false)]
[IntentFilter(new[] { Intent.ActionBootCompleted, "android.intent.action.QUICKBOOT_POWERON", Intent.ActionMyPackageReplaced })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        try
        {
            if (!TillEngine.Instance.HasSavedSession) return;
            TillLog.Write($"boot receiver: {intent?.Action} → starting the till service");
            MainActivity.StartTillService();
        }
        catch (Exception ex) { TillLog.Write($"boot receiver failed: {ex.Message}"); }
    }
}
