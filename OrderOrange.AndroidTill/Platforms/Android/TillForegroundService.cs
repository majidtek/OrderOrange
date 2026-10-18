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
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
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
        if (OperatingSystem.IsAndroidVersionAtLeast(29))
            StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
        else
            StartForeground(NotificationId, notification);

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
               .SetSmallIcon(Resource.Mipmap.appicon)
               .SetOngoing(true);
        if (open is not null) builder.SetContentIntent(open);
        return builder.Build();
    }
}
