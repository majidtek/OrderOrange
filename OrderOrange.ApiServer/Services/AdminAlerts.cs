using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Tells the administrators that somebody joined — a new shop, a new customer, a new driver.
/// Two ways at once: a line on the admin bell, and a Web Push to every administrator's
/// device so the news arrives with the admin site closed.
///
/// Every method is fire-and-forget. Signing up must never get slower, and must never fail,
/// because Mongo or a push service is having a bad minute.
/// </summary>
public sealed class AdminAlerts(IServiceScopeFactory scopes, PushSender push, ILogger<AdminAlerts> logger)
{
    /// <summary>A brand-new business applied. Links to the approval list, which is what to do next.</summary>
    public void NewBusiness(int storeId, string storeName, string ownerName, string how) =>
        Raise("business",
            "New business",
            $"{Clean(storeName)} — owner {Clean(ownerName)} ({how}). Waiting for approval.",
            "/restaurants");

    /// <summary>A brand-new customer account.</summary>
    public void NewUser(int userId, string name, string how) =>
        Raise("user", "New user", $"{Clean(name)} signed up ({how}).", "/users");

    /// <summary>A driver applied. They stay inactive until an administrator says otherwise.</summary>
    public void NewDriver(int userId, string name, string how) =>
        Raise("driver", "New driver", $"{Clean(name)} applied to deliver ({how}).", "/drivers");

    private void Raise(string kind, string title, string body, string url) =>
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<AdminAlertStore>();
                await store.AddAsync(kind, title, body, url);

                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var admins = await db.Users
                    .Where(u => u.Role == UserRole.Administrator && u.IsActive)
                    .Select(u => u.Id).ToListAsync();
                foreach (var id in admins) push.SendToUser(id, title, body, url);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Admin alert '{Title}' could not be recorded.", title);
            }
        });

    /// <summary>Keeps a stray newline or an over-long name out of a notification body.</summary>
    private static string Clean(string? text)
    {
        var s = (text ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (s.Length == 0) return "—";
        return s.Length <= 60 ? s : s[..57] + "…";
    }
}
