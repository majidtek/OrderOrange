using Android.App;
using Android.Content;

namespace OrderOrange.PartnerApp;

/// <summary>"OrderOrange Partner 1.2.3 is ready" in the notification shade; tapping it opens the app, where the update bar waits.</summary>
public static class UpdateNotifier
{
    private const string ChannelId = "oo_updates";
    private const int NotificationId = 2001;

    public static void Show(string version, string? notes)
    {
        try
        {
            var ctx = Android.App.Application.Context;
            if (ctx.GetSystemService(Context.NotificationService) is not NotificationManager nm) return;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                nm.CreateNotificationChannel(new NotificationChannel(ChannelId, "App updates", NotificationImportance.Default)
                {
                    Description = "A new version of OrderOrange Partner is ready to install."
                });
            }

            var launch = ctx.PackageManager?.GetLaunchIntentForPackage(ctx.PackageName ?? "");
            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(23)) flags |= PendingIntentFlags.Immutable;
            var open = launch is null ? null : PendingIntent.GetActivity(ctx, 1, launch, flags);

            var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? new Notification.Builder(ctx, ChannelId)
                : new Notification.Builder(ctx);
            builder.SetContentTitle($"OrderOrange Partner {version} is ready")
                   .SetContentText(string.IsNullOrWhiteSpace(notes) ? "Open the app and tap Update now." : notes)
                   .SetSmallIcon(OrderOrange.RestaurantWeb.Resource.Drawable.tillnotif)
                   .SetAutoCancel(true);
            if (!string.IsNullOrWhiteSpace(notes)) builder.SetStyle(new Notification.BigTextStyle().BigText(notes));
            if (open is not null) builder.SetContentIntent(open);
            nm.Notify(NotificationId, builder.Build());
        }
        catch (Exception ex)
        {
            Till.Services.TillLog.Write($"update notification: {ex.Message}");
        }
    }
}
