using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using OrderOrange.AndroidTill.Services;

namespace OrderOrange.AndroidTill;

/// <summary>
/// Keeps the till alive: a foreground service with a permanent notification, a partial
/// wake lock and a Wi-Fi lock, so Android neither kills the poll loop nor lets the radio
/// doze while a kitchen printer is waiting for the next ticket.
/// </summary>
// Both types declared: "connected device" first, "data sync" as the fallback when Android refuses it.
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeConnectedDevice | ForegroundService.TypeDataSync)]
public class TillForegroundService : Service
{
    private const int NotificationId = 1001;
    private const string ChannelId = "till";
    private PowerManager.WakeLock? _wake;
    private WifiManager.WifiLock? _wifi;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = BuildNotification();
        if (!GoForeground(notification))
        {
            // Android would not let the service run: the app itself stays open (it polls while
            // on screen); only the background printing is off until the next start.
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        try
        {
            if (_wake is null && GetSystemService(PowerService) is PowerManager pm)
            {
                _wake = pm.NewWakeLock(WakeLockFlags.Partial, "orderorange:till");
                _wake?.Acquire();
            }
            if (_wifi is null && ApplicationContext?.GetSystemService(WifiService) is WifiManager wm)
            {
                _wifi = wm.CreateWifiLock(OperatingSystem.IsAndroidVersionAtLeast(29) ? Android.Net.WifiMode.FullLowLatency : Android.Net.WifiMode.FullHighPerf, "orderorange:till");
                _wifi?.Acquire();
            }
        }
        catch (Exception ex) { TillLog.Write($"service locks: {ex.Message}"); }

        TillEngine.Instance.Start();
        return StartCommandResult.Sticky;
    }

    /// <summary>
    /// "Connected device" (printers over Bluetooth/USB/LAN) first: unlike dataSync it is not cut
    /// off after 6 hours on Android 14+, so the till can run all day. If Android refuses it,
    /// "data sync" instead. A refusal used to be an uncaught exception, which closed the whole
    /// app right after sign-in on Android 14+ (Majed 2026-10-22: "app close auto").
    /// </summary>
    private bool GoForeground(Notification notification)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            try { StartForeground(NotificationId, notification); return true; }
            catch (Exception ex) { TillLog.Write($"foreground refused: {ex.Message}"); return false; }
        }
        foreach (var type in new[] { ForegroundService.TypeConnectedDevice, ForegroundService.TypeDataSync })
        {
            try
            {
                StartForeground(NotificationId, notification, type);
                return true;
            }
            catch (Exception ex) { TillLog.Write($"foreground as {type} refused: {ex.Message}"); }
        }
        return false;
    }

    /// <summary>The app was swiped out of Recents: some ROMs kill the process with it — come back in a few seconds.</summary>
    public override void OnTaskRemoved(Intent? rootIntent)
    {
        try
        {
            TillLog.Write("task removed — scheduling the till service to come back");
            ScheduleRestart(this, 3000);
        }
        catch (Exception ex) { TillLog.Write($"restart schedule failed: {ex.Message}"); }
        base.OnTaskRemoved(rootIntent);
    }

    public static void ScheduleRestart(Context ctx, int delayMs)
    {
        var intent = new Intent(ctx, typeof(TillForegroundService));
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= PendingIntentFlags.Immutable;
        var pi = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? PendingIntent.GetForegroundService(ctx, 7, intent, flags)
            : PendingIntent.GetService(ctx, 7, intent, flags);
        if (pi is null || ctx.GetSystemService(AlarmService) is not AlarmManager am) return;
        var at = SystemClock.ElapsedRealtime() + delayMs;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) am.SetAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, at, pi);
        else am.Set(AlarmType.ElapsedRealtimeWakeup, at, pi);
    }

    public override void OnDestroy()
    {
        try { if (_wake?.IsHeld == true) _wake.Release(); } catch { }
        try { if (_wifi?.IsHeld == true) _wifi.Release(); } catch { }
        _wake = null; _wifi = null;
        base.OnDestroy();
    }

    private Notification BuildNotification()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && GetSystemService(NotificationService) is NotificationManager nm)
        {
            var channel = new NotificationChannel(ChannelId, "OrderOrange Till", NotificationImportance.Low)
            {
                Description = "Keeps the till printing while the app is in the background."
            };
            nm.CreateNotificationChannel(channel);
        }

        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName ?? "");
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= PendingIntentFlags.Immutable;
        var open = launch is null ? null : PendingIntent.GetActivity(this, 0, launch, flags);

        var store = TillEngine.Instance.Api.StoreName ?? "OrderOrange";
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ChannelId)
            : new Notification.Builder(this);
        builder.SetContentTitle("OrderOrange Till")
               .SetContentText($"{store} — watching for orders")
               .SetSmallIcon(Resource.Drawable.tillnotif)   // monochrome glyph: Android tints status-bar icons white
               .SetOngoing(true);
        if (open is not null) builder.SetContentIntent(open);
        return builder.Build();
    }
}
