using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace OrderOrange.AndroidTill;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // A till is left on all day; the screen must not sleep on top of a live order board.
        Window?.AddFlags(Android.Views.WindowManagerFlags.KeepScreenOn);
    }

    /// <summary>Starts (or pokes) the foreground service that keeps polling while the app is in the background.</summary>
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
}
