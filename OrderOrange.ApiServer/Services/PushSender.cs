using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using WebPush;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Real Web Push to the partner's devices: the browser subscribed once, and from then
/// on a new order rings the tablet even with the portal CLOSED — the polling bell only
/// works while the page is open, which is exactly when nobody needs reminding.
/// Fire-and-forget by design: an unreachable push service must never slow an order down.
/// </summary>
public sealed class PushSender(IServiceScopeFactory scopes, IConfiguration config, ILogger<PushSender> logger)
{
    private string? PublicKey => config["Push:PublicKey"];
    private string? PrivateKey => config["Push:PrivateKey"];
    private string Subject => config["Push:Subject"] ?? "mailto:info@orderorange.com";

    public bool IsConfigured => !string.IsNullOrEmpty(PublicKey) && !string.IsNullOrEmpty(PrivateKey);

    /// <summary>Fire-and-forget: pushes to every device the store registered.</summary>
    public void SendToStore(int restaurantId, string title, string body, string url)
    {
        Trail($"queue store {restaurantId} configured={IsConfigured}");
        if (!IsConfigured) return;
        _ = Task.Run(() => SendCoreAsync(s => s.RestaurantId == restaurantId && s.UserId == null,
            $"store {restaurantId}", title, body, url));
    }

    /// <summary>Fire-and-forget: rings the CUSTOMER's own devices (order status moves).</summary>
    public void SendToUser(int userId, string title, string body, string url)
    {
        Trail($"queue user {userId} configured={IsConfigured}");
        if (!IsConfigured || userId <= 0) return;
        _ = Task.Run(() => SendCoreAsync(s => s.UserId == userId,
            $"user {userId}", title, body, url));
    }

    /// <summary>A tiny on-disk trail — stdout is invisible for the self-hosted exe.</summary>
    private static void Trail(string line)
    {
        try
        {
            var entry = $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}";
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "push.log"), entry); }
            catch { File.AppendAllText(Path.Combine(Path.GetTempPath(), "oo-push.log"), entry); }
        }
        catch { }
    }

    private async Task SendCoreAsync(System.Linq.Expressions.Expression<Func<Models.PushSubscriptionRow, bool>> filter,
        string who, string title, string body, string url)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subs = await db.PushSubscriptions.Where(filter).ToListAsync();
            Trail($"{who} '{title}' -> {subs.Count} device(s)");
            if (subs.Count == 0) return;

            var vapid = new VapidDetails(Subject, PublicKey, PrivateKey);
            var payload = JsonSerializer.Serialize(new { title, body, url });
            using var client = new WebPushClient();

            foreach (var sub in subs)
            {
                try
                {
                    await client.SendNotificationAsync(
                        new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth), payload, vapid);
                    Trail("sent ok");
                }
                catch (WebPushException ex) when (
                    ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
                {
                    Trail($"dead endpoint removed ({(int)ex.StatusCode})");
                    db.PushSubscriptions.Remove(sub);
                }
                catch (Exception ex)
                {
                    Trail($"FAIL {ex.GetType().Name}: {ex.Message}");
                    logger.LogWarning(ex, "Push to {Who} failed for one endpoint.", who);
                }
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Trail($"OUTER FAIL {ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "Push fan-out for {Who} failed.", who);
        }
    }
}
