using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The store's side of the table chat: an inbox of tables that spoke, the thread per
/// table, and replies — text or voice — that land on the guest's phone via the QR page.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
public class TableChatsController(AppDbContext db, TableChatStore chat) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TableChatThreadDto>>> Threads()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var latest = await chat.LatestPerTableAsync(CurrentRestaurantId);
        var tableIds = latest.Select(m => m.TableId).Distinct().ToList();
        var names = await db.StoreTables.Where(t => tableIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);
        var threads = new List<TableChatThreadDto>();
        foreach (var m in latest)
        {
            threads.Add(new TableChatThreadDto(
                m.TableId,
                names.GetValueOrDefault(m.TableId, "—"),
                string.IsNullOrWhiteSpace(m.Text) ? (m.Audio is not null ? "🎤" : m.Image is not null ? "📷" : m.File is not null ? "📄" : m.Text) : m.Text,
                m.From,
                m.At,
                await chat.UnreadOfTableAsync(CurrentRestaurantId, m.TableId)));
        }
        return Ok(threads);
    }

    /// <summary>
    /// The shelf of finished conversations: every time an invoice closes, that
    /// table's chat moves here — one row per session, newest first.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<ChatArchiveSessionDto>>> History()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var sessions = await chat.ArchiveSessionsAsync(CurrentRestaurantId);
        var tableIds = sessions.Select(m => m.TableId).Distinct().ToList();
        var names = await db.StoreTables.Where(t => tableIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name);
        var result = new List<ChatArchiveSessionDto>();
        foreach (var s in sessions.Where(s => s.ArchiveId is not null))
        {
            var count = await chat.ArchiveCountAsync(CurrentRestaurantId, s.ArchiveId!);
            result.Add(new ChatArchiveSessionDto(
                s.ArchiveId!, s.TableId, names.GetValueOrDefault(s.TableId, "—"),
                s.ArchiveRef, s.ArchivedAt ?? s.At, (int)count));
        }
        return Ok(result.OrderByDescending(s => s.ClosedAt).ToList());
    }

    [HttpGet("history/{archiveId}")]
    public async Task<ActionResult<List<TableChatMessageDto>>> HistoryMessages(string archiveId)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var messages = await chat.ArchiveMessagesAsync(CurrentRestaurantId, archiveId);
        return Ok(messages.Select(ReservationsController.ToChatDto).ToList());
    }

    /// <summary>The badge on the app bar: how many guest messages await eyes.</summary>
    [HttpGet("unread")]
    public async Task<ActionResult<TableChatUnreadDto>> Unread()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var seen = await db.Restaurants.Where(r => r.Id == CurrentRestaurantId)
            .Select(r => r.LastSeenTableChatId).FirstAsync();
        var (count, maxId) = await chat.UnreadAsync(CurrentRestaurantId, seen);
        return Ok(new TableChatUnreadDto(count, maxId));
    }

    /// <summary>The inbox was opened — everything up to now counts as seen.</summary>
    [HttpPost("seen")]
    public async Task<IActionResult> Seen()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var (_, maxId) = await chat.UnreadAsync(CurrentRestaurantId, 0);
        var restaurant = await db.Restaurants.FirstAsync(r => r.Id == CurrentRestaurantId);
        if (maxId > restaurant.LastSeenTableChatId) restaurant.LastSeenTableChatId = maxId;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{tableId:int}")]
    public async Task<ActionResult<List<TableChatMessageDto>>> Messages(int tableId, int afterId = 0)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var messages = await chat.MessagesAsync(CurrentRestaurantId, tableId, afterId);
        return Ok(messages.Select(ReservationsController.ToChatDto).ToList());
    }

    /// <summary>One poll: new words, changed old ones, and how far the guest has read.</summary>
    [HttpGet("{tableId:int}/sync")]
    public async Task<ActionResult<TableChatSyncDto>> Sync(int tableId, int afterId = 0, long stamp = 0)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var now = DateTime.Now;
        var fresh = await chat.MessagesAsync(CurrentRestaurantId, tableId, afterId);
        var changed = stamp > 0
            ? await chat.ChangedAsync(CurrentRestaurantId, tableId, new DateTime(stamp), afterId)
            : [];
        var (readId, readAt) = await chat.ReadOfAsync(CurrentRestaurantId, tableId, "guest");
        return Ok(new TableChatSyncDto(
            fresh.Select(ReservationsController.ToChatDto).ToList(),
            changed.Select(ReservationsController.ToChatDto).ToList(),
            now.Ticks, readId, readAt));
    }

    [HttpPut("{tableId:int}/messages/{id:int}")]
    public async Task<ActionResult<TableChatMessageDto>> Edit(int tableId, int id, EditTableChatRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Trim().Length > 500)
            return BadRequest(new { message = "Keep messages under 500 characters." });
        var message = await chat.EditAsync(CurrentRestaurantId, tableId, id, "store", req.Text.Trim());
        return message is null ? NotFound() : Ok(ReservationsController.ToChatDto(message));
    }

    [HttpDelete("{tableId:int}/messages/{id:int}")]
    public async Task<ActionResult<TableChatMessageDto>> Delete(int tableId, int id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var message = await chat.DeleteAsync(CurrentRestaurantId, tableId, id, "store");
        return message is null ? NotFound() : Ok(ReservationsController.ToChatDto(message));
    }

    [HttpPost("{tableId:int}/read")]
    public async Task<IActionResult> MarkRead(int tableId, MarkChatReadRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        await chat.MarkReadAsync(CurrentRestaurantId, tableId, "store", req.LastId);
        return NoContent();
    }

    [HttpPost("{tableId:int}")]
    public async Task<ActionResult<TableChatMessageDto>> Send(int tableId, SendTableChatRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (await db.StoreTables.CountAsync(t => t.Id == tableId && t.RestaurantId == CurrentRestaurantId) == 0)
            return NotFound();

        var problem = ReservationsController.ValidateMessage(req);
        if (problem is not null) return BadRequest(new { message = problem });

        var message = await chat.AddAsync(CurrentRestaurantId, tableId, "store", (req.Text ?? "").Trim(), req.Audio, req.Image, req.ReplyToId, req.File, ReservationsController.SafeFileName(req.FileName));
        return Ok(ReservationsController.ToChatDto(message));
    }
}
