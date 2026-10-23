using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The store's side of the chat with the OrderOrange team: text, photos, files and voice
/// notes (Majed 2026-10-14). Everyone on the store's team sees the same one thread.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
public class AdminChatController(AdminChatStore chat, PushSender push, AdminAlerts alerts, AppDbContext db) : ApiControllerBase
{
    internal static CommunityChatMessageDto ToDto(AdminChatDoc m) => new(
        m.Id, m.FromUserId, m.FromName, m.Text, m.At, m.Audio, m.Image, m.Deleted,
        m.EditedAt, m.ReplyPreview, m.File, m.FileName, m.FromAdmin);

    [HttpGet("unread")]
    public async Task<ActionResult<int>> Unread() =>
        CurrentRestaurantId == 0 ? 0 : await chat.UnreadForStoreAsync(CurrentRestaurantId);

    [HttpGet("sync")]
    public async Task<ActionResult<CommunityChatSyncDto>> Sync(int afterId = 0, long stamp = 0)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var now = DateTime.Now;
        var fresh = await chat.ThreadAsync(CurrentRestaurantId, afterId);
        var changed = stamp > 0 ? await chat.ChangedAsync(CurrentRestaurantId, new DateTime(stamp), afterId) : [];
        await chat.MarkReadAsync(CurrentRestaurantId, readerIsAdmin: false);
        return Ok(new CommunityChatSyncDto(fresh.Select(ToDto).ToList(), changed.Select(ToDto).ToList(), now.Ticks));
    }

    [HttpPost]
    public async Task<ActionResult<CommunityChatMessageDto>> Send(SendTableChatRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var problem = ReservationsController.ValidateMessage(req);
        if (problem is not null) return BadRequest(new { message = problem });

        var message = await chat.AddAsync(CurrentRestaurantId, false, CurrentUserId, CurrentUserName, (req.Text ?? "").Trim(),
            req.Audio, req.Image, req.ReplyToId, req.File, ReservationsController.SafeFileName(req.FileName));
        var storeName = await db.Restaurants.Where(r => r.Id == CurrentRestaurantId).Select(r => r.Name).FirstOrDefaultAsync() ?? "";
        alerts.ChatMessage(CurrentRestaurantId, storeName, $"{CurrentUserName}: {AdminChatStore.Preview(message)}");
        return Ok(ToDto(message));
    }

    [HttpPut("messages/{id:int}")]
    public async Task<ActionResult<CommunityChatMessageDto>> Edit(int id, EditTableChatRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Trim().Length > 4000)
            return BadRequest(new { message = "Keep messages under 500 characters." });
        var message = await chat.EditAsync(CurrentRestaurantId, id, false, CurrentUserId, req.Text.Trim());
        return message is null ? NotFound() : Ok(ToDto(message));
    }

    [HttpDelete("messages/{id:int}")]
    public async Task<ActionResult<CommunityChatMessageDto>> Delete(int id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var message = await chat.DeleteAsync(CurrentRestaurantId, id, false, CurrentUserId);
        return message is null ? NotFound() : Ok(ToDto(message));
    }
}

/// <summary>
/// The OrderOrange team's side: an inbox of every store that has written, a thread per
/// store, and a way to start a conversation with any store.
/// </summary>
[Authorize(Roles = "Administrator")]
public class AdminPartnerChatController(AdminChatStore chat, PushSender push, AppDbContext db) : ApiControllerBase
{
    [HttpGet("~/api/admin/chat/unread")]
    public async Task<ActionResult<int>> Unread() => await chat.UnreadForAdminAsync();

    /// <summary>Stores with a conversation, newest first.</summary>
    [HttpGet("~/api/admin/chat/threads")]
    public async Task<ActionResult<List<PartnerChatThreadDto>>> Threads()
    {
        var inbox = await chat.InboxAsync();
        var ids = inbox.Select(t => t.RestaurantId).ToList();
        var stores = await db.Restaurants.Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Name, r.LogoEmoji, OwnerName = r.Owner.FullName })
            .ToDictionaryAsync(r => r.Id);
        return inbox.Select(t =>
        {
            var s = stores.GetValueOrDefault(t.RestaurantId);
            return new PartnerChatThreadDto(t.RestaurantId, s?.Name ?? $"#{t.RestaurantId}", s?.LogoEmoji ?? "🏪",
                s?.OwnerName ?? "", AdminChatStore.Preview(t.Last), t.Last.FromAdmin ? "me" : "them", t.Last.At, t.Unread);
        }).ToList();
    }

    /// <summary>Find any store to start a conversation with.</summary>
    [HttpGet("~/api/admin/chat/stores")]
    public async Task<ActionResult<List<PartnerChatThreadDto>>> Stores(string? search = null)
    {
        var q = db.Restaurants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = int.TryParse(s, out var id) ? q.Where(r => r.Id == id) : q.Where(r => r.Name.Contains(s) || r.Owner.FullName.Contains(s));
        }
        return await q.OrderBy(r => r.Name).Take(20)
            .Select(r => new PartnerChatThreadDto(r.Id, r.Name, r.LogoEmoji, r.Owner.FullName, "", "", null, 0))
            .ToListAsync();
    }

    [HttpGet("~/api/admin/chat/{restaurantId:int}/sync")]
    public async Task<ActionResult<CommunityChatSyncDto>> Sync(int restaurantId, int afterId = 0, long stamp = 0)
    {
        var now = DateTime.Now;
        var fresh = await chat.ThreadAsync(restaurantId, afterId);
        var changed = stamp > 0 ? await chat.ChangedAsync(restaurantId, new DateTime(stamp), afterId) : [];
        await chat.MarkReadAsync(restaurantId, readerIsAdmin: true);
        return Ok(new CommunityChatSyncDto(fresh.Select(AdminChatController.ToDto).ToList(),
            changed.Select(AdminChatController.ToDto).ToList(), now.Ticks));
    }

    [HttpPost("~/api/admin/chat/{restaurantId:int}")]
    public async Task<ActionResult<CommunityChatMessageDto>> Send(int restaurantId, SendTableChatRequest req)
    {
        if (!await db.Restaurants.AnyAsync(r => r.Id == restaurantId)) return NotFound();
        var problem = ReservationsController.ValidateMessage(req);
        if (problem is not null) return BadRequest(new { message = problem });

        // The store sees only "OrderOrange" — never which person answered (Majed 2026-10-15:
        // "do not write Majed in admin when I send a message to a user").
        var message = await chat.AddAsync(restaurantId, true, CurrentUserId, "OrderOrange", (req.Text ?? "").Trim(),
            req.Audio, req.Image, req.ReplyToId, req.File, ReservationsController.SafeFileName(req.FileName));
        push.SendToStore(restaurantId, "💬 OrderOrange", AdminChatStore.Preview(message), "/admin-chat");
        return Ok(AdminChatController.ToDto(message));
    }

    [HttpPut("~/api/admin/chat/{restaurantId:int}/messages/{id:int}")]
    public async Task<ActionResult<CommunityChatMessageDto>> Edit(int restaurantId, int id, EditTableChatRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Trim().Length > 4000)
            return BadRequest(new { message = "Keep messages under 500 characters." });
        var message = await chat.EditAsync(restaurantId, id, true, CurrentUserId, req.Text.Trim());
        return message is null ? NotFound() : Ok(AdminChatController.ToDto(message));
    }

    [HttpDelete("~/api/admin/chat/{restaurantId:int}/messages/{id:int}")]
    public async Task<ActionResult<CommunityChatMessageDto>> Delete(int restaurantId, int id)
    {
        var message = await chat.DeleteAsync(restaurantId, id, true, CurrentUserId);
        return message is null ? NotFound() : Ok(AdminChatController.ToDto(message));
    }
}
