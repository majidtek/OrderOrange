using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Team direct messages: private one-to-one threads between the people of a single
/// business. The contact list is the store's own team; each thread is scoped to the
/// caller's restaurant and to the two people in it, and nobody else can read it.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
public class CommunityChatController(AppDbContext db, CommunityChatStore chat) : ApiControllerBase
{
    private static CommunityChatMessageDto ToDto(CommunityChatDoc m) => new(
        m.Id, m.FromUserId, m.FromName, m.Text, m.At, m.Audio, m.Image, m.Deleted,
        m.EditedAt, m.ReplyPreview, m.File, m.FileName);

    /// <summary>The badge: how many direct messages across all my threads await me.</summary>
    [HttpGet("unread")]
    public async Task<ActionResult<int>> Unread()
    {
        if (CurrentRestaurantId == 0) return Ok(0);
        return Ok(await chat.TotalUnreadAsync(CurrentRestaurantId, CurrentUserId));
    }

    /// <summary>The people I can message: everyone on this store's team but me.</summary>
    [HttpGet("contacts")]
    public async Task<ActionResult<List<TeamContactDto>>> Contacts()
    {
        if (CurrentRestaurantId == 0) return Forbid();

        var owner = await db.Restaurants.Where(r => r.Id == CurrentRestaurantId)
            .Select(r => r.Owner).FirstAsync();
        var members = await db.StoreMembers
            .Where(m => m.RestaurantId == CurrentRestaurantId && m.IsActive)
            .Include(m => m.User)
            .ToListAsync();

        // The owner plus each member — one entry per person, me removed.
        var people = new List<(int Id, string Name, string Role, bool IsOwner)>
        {
            (owner.Id, owner.FullName, "owner", true),
        };
        people.AddRange(members.Select(m => (m.UserId, m.User.FullName, m.Role.ToString().ToLowerInvariant(), false)));

        var contacts = new List<TeamContactDto>();
        foreach (var p in people.Where(p => p.Id != CurrentUserId).DistinctBy(p => p.Id))
        {
            var last = await chat.LastBetweenAsync(CurrentRestaurantId, CurrentUserId, p.Id);
            var unread = await chat.UnreadFromAsync(CurrentRestaurantId, CurrentUserId, p.Id);
            var preview = last is null ? ""
                : last.Deleted ? "🚫"
                : !string.IsNullOrWhiteSpace(last.Text) ? last.Text
                : last.Audio is not null ? "🎤" : last.Image is not null ? "📷" : last.File is not null ? "📄" : "";
            contacts.Add(new TeamContactDto(
                p.Id, p.Name, p.Role, p.IsOwner,
                preview,
                last is null ? "" : last.FromUserId == CurrentUserId ? "me" : "them",
                last?.At, unread));
        }

        // Anyone I've talked to floats up by recency; the rest follow by name.
        return Ok(contacts
            .OrderByDescending(c => c.LastAt ?? DateTime.MinValue)
            .ThenBy(c => c.Name)
            .ToList());
    }

    /// <summary>Guard: the other person must really be on this store's team.</summary>
    private async Task<bool> IsTeammateAsync(int otherId) =>
        await db.Restaurants.AnyAsync(r => r.Id == CurrentRestaurantId && r.OwnerUserId == otherId) ||
        await db.StoreMembers.AnyAsync(m => m.RestaurantId == CurrentRestaurantId && m.UserId == otherId && m.IsActive);

    /// <summary>One poll answers everything for a thread: new words and changed old ones.</summary>
    [HttpGet("thread/{otherId:int}/sync")]
    public async Task<ActionResult<CommunityChatSyncDto>> Sync(int otherId, int afterId = 0, long stamp = 0)
    {
        if (CurrentRestaurantId == 0 || !await IsTeammateAsync(otherId)) return Forbid();
        var now = DateTime.Now;
        var fresh = await chat.ThreadAsync(CurrentRestaurantId, CurrentUserId, otherId, afterId);
        var changed = stamp > 0 ? await chat.ChangedAsync(CurrentRestaurantId, CurrentUserId, otherId, new DateTime(stamp), afterId) : [];
        await chat.MarkReadAsync(CurrentRestaurantId, CurrentUserId, otherId);
        return Ok(new CommunityChatSyncDto(
            fresh.Select(ToDto).ToList(),
            changed.Select(ToDto).ToList(),
            now.Ticks));
    }

    [HttpPost("thread/{otherId:int}")]
    public async Task<ActionResult<CommunityChatMessageDto>> Send(int otherId, SendTableChatRequest req)
    {
        if (CurrentRestaurantId == 0 || !await IsTeammateAsync(otherId)) return Forbid();
        var problem = ReservationsController.ValidateMessage(req);
        if (problem is not null) return BadRequest(new { message = problem });

        var message = await chat.AddAsync(CurrentRestaurantId, CurrentUserId, CurrentUserName, otherId, (req.Text ?? "").Trim(),
            req.Audio, req.Image, req.ReplyToId, req.File, ReservationsController.SafeFileName(req.FileName));
        return Ok(ToDto(message));
    }

    [HttpPut("messages/{id:int}")]
    public async Task<ActionResult<CommunityChatMessageDto>> Edit(int id, EditTableChatRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Trim().Length > 500)
            return BadRequest(new { message = "Keep messages under 500 characters." });
        var message = await chat.EditAsync(CurrentRestaurantId, id, CurrentUserId, req.Text.Trim());
        return message is null ? NotFound() : Ok(ToDto(message));
    }

    [HttpDelete("messages/{id:int}")]
    public async Task<ActionResult<CommunityChatMessageDto>> Delete(int id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var message = await chat.DeleteAsync(CurrentRestaurantId, id, CurrentUserId);
        return message is null ? NotFound() : Ok(ToDto(message));
    }
}
