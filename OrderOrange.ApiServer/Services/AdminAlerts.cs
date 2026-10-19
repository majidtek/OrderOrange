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
    public void NewBusiness(int storeId, string storeName, string ownerName, string how)
    {
        Raise("business",
            "New business",
            $"{Clean(storeName)} — owner {Clean(ownerName)} ({how}). Live now; review it.",
            "/restaurants");
        // Every new partner gets a first line in the OrderOrange chat (Majed 2026-10-15:
        // "send message to any new partner in chat about help"). The text is a key, so the
        // partner app shows it in the owner's own language.
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var chat = scope.ServiceProvider.GetRequiredService<AdminChatStore>();
                await chat.AddAsync(storeId, fromAdmin: true, fromId: 0, fromName: "OrderOrange", text: "§achat.welcome");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Welcome chat line for store {Store} could not be written.", storeId);
            }
        });
    }

    /// <summary>A brand-new customer account.</summary>
    public void NewUser(int userId, string name, string how) =>
        Raise("user", "New user", $"{Clean(name)} signed up ({how}).", "/users");

    /// <summary>A driver applied. They stay inactive until an administrator says otherwise.</summary>
    public void NewDriver(int userId, string name, string how) =>
        Raise("driver", "New driver", $"{Clean(name)} applied to deliver ({how}).", "/drivers");

    /// <summary>New mail in the platform inbox — bell line plus push, links to the mailbox page.</summary>
    public void NewMail(string from, string subject) =>
        Raise("mail", "New email", $"{Clean(from)} — {Clean(subject)}", "/mailbox");

    /// <summary>
    /// A store wrote in the partner chat. Push only — no bell line per message; the chat
    /// page's own unread badge already counts them.
    /// </summary>
    public void ChatMessage(int storeId, string storeName, string preview) =>
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var admins = await db.Users
                    .Where(u => u.Role == UserRole.Administrator && u.IsActive)
                    .Select(u => u.Id).ToListAsync();
                foreach (var id in admins) push.SendToUser(id, $"💬 {Clean(storeName)}", Clean(preview), $"/partner-chat?store={storeId}");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Partner chat push for store {Store} failed.", storeId);
            }
        });

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
