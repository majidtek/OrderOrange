using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui;

namespace OrderOrange.PartnerApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>Starts (or pokes) the foreground service that keeps the printers working while the app is in the background.</summary>
    public static void StartTillService()
    {
        var ctx = Android.App.Application.Context;
        var intent = new Intent(ctx, typeof(TillForegroundService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) ctx.StartForegroundService(intent);
        else ctx.StartService(intent);
    }

    public static void StopTillService()
    {
        var ctx = Android.App.Application.Context;
        ctx.StopService(new Intent(ctx, typeof(TillForegroundService)));
    }

    /// <summary>True when Android has agreed not to doze this app (battery optimisation off for it).</summary>
    public static bool BackgroundAllowed()
    {
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(23)) return true;
            var ctx = Android.App.Application.Context;
            return ctx.GetSystemService(Context.PowerService) is PowerManager pm && pm.IsIgnoringBatteryOptimizations(ctx.PackageName);
        }
        catch { return true; }
    }

    /// <summary>
    /// Asks Android to leave the till out of battery optimisation. Without it, Doze cuts the
    /// network a while after the screen goes dark and the poll loop silently stops seeing
    /// orders — a foreground service and a wake lock do not override Doze; this does.
    /// </summary>
    public static void RequestBackgroundAllowed()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23) || BackgroundAllowed()) return;
        var ctx = Android.App.Application.Context;
        try
        {
            var intent = new Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations)
                .SetData(Android.Net.Uri.Parse($"package:{ctx.PackageName}"))
                .AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
        }
        catch
        {
            // Some ROMs refuse the direct request: open the list instead so the user can find the app.
            try { ctx.StartActivity(new Intent(Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings).AddFlags(ActivityFlags.NewTask)); } catch { }
        }
    }
}
