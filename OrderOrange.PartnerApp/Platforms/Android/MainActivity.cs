using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui;

namespace OrderOrange.PartnerApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Android 15 draws every app edge to edge, so the status bar sat on top of the portal's
        // menu bar (Majed 2026-10-20: "top menu is over"). Keep the page below the system bars
        // and paint the strip behind the status bar in the portal's orange, white behind the
        // navigation bar; the clock and icons go white on the orange.
        if (FindViewById(Android.Resource.Id.Content) is ViewGroup content)
            ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarsPadding());
        if (Window is { } window)
        {
            var controller = WindowCompat.GetInsetsController(window, window.DecorView);
            controller.AppearanceLightStatusBars = false;
            controller.AppearanceLightNavigationBars = true;
        }
    }

    /// <summary>Pads the activity's content by the system bars and colours the two strips.</summary>
    private sealed class SystemBarsPadding : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        private static readonly Android.Graphics.Color Orange = Android.Graphics.Color.ParseColor("#FF5A00");

        public WindowInsetsCompat OnApplyWindowInsets(View v, WindowInsetsCompat insets)
        {
            var bars = insets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
            v.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            // Orange underneath everything; white laid over it from the top inset down, so only
            // the status-bar strip shows orange.
            var layers = new LayerDrawable([new ColorDrawable(Orange), new ColorDrawable(Android.Graphics.Color.White)]);
            layers.SetLayerInset(1, 0, bars.Top, 0, 0);
            v.Background = layers;
            return WindowInsetsCompat.Consumed;
        }
    }

    /// <summary>Starts (or pokes) the foreground service that keeps the printers working while the app is in the background.</summary>
    public static void StartTillService()
    {
        try
        {
            var ctx = Android.App.Application.Context;
            var intent = new Intent(ctx, typeof(TillForegroundService));
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) ctx.StartForegroundService(intent);
            else ctx.StartService(intent);
        }
        // Android 12+ refuses a background start in some states: log it, never close the app.
        catch (Exception ex) { Till.Services.TillLog.Write($"till service not started: {ex.Message}"); }
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
    /// Asks Android to leave the app out of battery optimisation. Without it, Doze cuts the
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
