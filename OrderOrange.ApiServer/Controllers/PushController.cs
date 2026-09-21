using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Web Push registration — partner devices (new orders ring the tablet) AND customer
/// devices (status moves ring the phone). One endpoint each way: an owner's browser
/// registers under the store, anyone else's under their own account.
/// </summary>
[Authorize]
public class PushController(AppDbContext db, PushSender push) : ApiControllerBase
{
    /// <summary>The public half of the VAPID pair — what the browser subscribes with.</summary>
    [HttpGet("vapid")]
    public ActionResult<PushVapidDto> Vapid([FromServices] IConfiguration config) =>
        config["Push:PublicKey"] is { Length: > 0 } key && push.IsConfigured
            ? new PushVapidDto(key)
            : NotFound();

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(PushSubscribeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Endpoint) || req.Endpoint.Length > 1000
            || string.IsNullOrWhiteSpace(req.P256dh) || string.IsNullOrWhiteSpace(req.Auth))
            return BadRequest();

        // Whose device is this? A store's tablet, or a customer's own phone.
        var storeId = CurrentRestaurantId;
        int? userId = storeId == 0 ? CurrentUserId : null;
        if (storeId == 0 && userId is null or 0) return Forbid();

        // One row per browser: re-subscribing refreshes, never duplicates.
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscriptionRow
            {
                RestaurantId = storeId,
                UserId = userId,
                Endpoint = req.Endpoint.Trim(),
                P256dh = req.P256dh.Trim(),
                Auth = req.Auth.Trim(),
                CreatedAt = DateTime.Now,
            });
        }
        else
        {
            existing.RestaurantId = storeId;
            existing.UserId = userId;
            existing.P256dh = req.P256dh.Trim();
            existing.Auth = req.Auth.Trim();
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Rings every device on this account/store — the "does it work?" button.</summary>
    [HttpPost("test")]
    public async Task<ActionResult<object>> Test()
    {
        if (CurrentRestaurantId != 0)
        {
            var count = await db.PushSubscriptions.CountAsync(s => s.RestaurantId == CurrentRestaurantId && s.UserId == null);
            push.SendToStore(CurrentRestaurantId, "OrderOrange ✅", "Notifications are working.", "/orders");
            return new { devices = count };
        }
        var mine = await db.PushSubscriptions.CountAsync(s => s.UserId == CurrentUserId);
        push.SendToUser(CurrentUserId, "OrderOrange ✅", "Notifications are working.", "/");
        return new { devices = mine };
    }
}
