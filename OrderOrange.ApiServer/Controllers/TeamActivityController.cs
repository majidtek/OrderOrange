using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Who is at work right now, and where they have been. The visit LOG already exists
/// (every portal navigation writes one); this adds presence — a heartbeat that keeps
/// a row warm — so someone reading one screen for ten minutes still counts as here.
/// Scoped to the caller's own store: an owner watches their team, nobody else's.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
public class TeamActivityController(AppDbContext db) : ApiControllerBase
{
    /// <summary>A heartbeat inside this window means "here now".</summary>
    private const int OnlineWindowMinutes = 3;

    /// <summary>A longer gap than this and the next beat starts a fresh sitting.</summary>
    private const int NewSessionGapMinutes = 20;

    /// <summary>
    /// Heartbeat from the portal. Cheap on purpose: one upserted row per person,
    /// never a new log line — the navigation log is written elsewhere.
    /// </summary>
    [HttpPost("beat")]
    public async Task<IActionResult> Beat(TeamBeatRequest req)
    {
        if (CurrentRestaurantId == 0) return NoContent();
        var now = DateTime.Now;

        var row = await db.TeamPresences
            .FirstOrDefaultAsync(p => p.RestaurantId == CurrentRestaurantId && p.UserId == CurrentUserId);

        var fresh = row is null || row.LastSeenAt is null
                    || (now - row.LastSeenAt.Value).TotalMinutes > NewSessionGapMinutes;

        if (row is null)
        {
            row = new TeamPresence { RestaurantId = CurrentRestaurantId, UserId = CurrentUserId };
            db.TeamPresences.Add(row);
        }

        if (fresh) row.LastLoginAt = now;
        row.LastSeenAt = now;
        if (!string.IsNullOrWhiteSpace(req.Page)) row.CurrentPage = req.Page.Trim()[..Math.Min(req.Page.Trim().Length, 400)];

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>The whole team, present or not, with the online ones first.</summary>
    [HttpGet]
    [RequirePerm(Perm.Team)]
    public async Task<ActionResult<List<TeamActivityDto>>> All()
    {
        if (CurrentRestaurantId == 0) return Ok(new List<TeamActivityDto>());
        var now = DateTime.Now;

        var store = await db.Restaurants
            .Where(r => r.Id == CurrentRestaurantId)
            .Select(r => new { r.OwnerUserId, Owner = r.Owner })
            .FirstAsync();

        var members = await db.StoreMembers
            .Where(m => m.RestaurantId == CurrentRestaurantId)
            .Include(m => m.User)
            .Include(m => m.RoleDef)
            .ToListAsync();

        var people = new List<(int Id, string Name, string Role, string? Photo, string? Icon, bool IsOwner)>
        {
            (store.Owner.Id, store.Owner.FullName, "owner", store.Owner.AvatarPhoto, store.Owner.AvatarIcon, true),
        };
        people.AddRange(members.Select(m => (
            m.UserId,
            m.User.FullName,
            m.RoleDef != null ? m.RoleDef.Name : m.Role.ToString().ToLowerInvariant(),
            m.User.AvatarPhoto,
            m.User.AvatarIcon,
            false)));

        var ids = people.Select(p => p.Id).ToList();
        var presence = await db.TeamPresences
            .Where(p => p.RestaurantId == CurrentRestaurantId && ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p);

        // How much each person has walked through today — a cheap sense of "busy".
        var since = DateTime.Today;
        var todayCounts = await db.ActivityLogs
            .Where(a => a.App == "Partner" && a.At >= since && ids.Contains(a.UserId))
            .GroupBy(a => a.UserId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N);

        var list = people.DistinctBy(p => p.Id).Select(p =>
        {
            presence.TryGetValue(p.Id, out var pr);
            var seen = pr?.LastSeenAt;
            return new TeamActivityDto(
                p.Id, p.Name, p.Role, p.IsOwner, p.Photo, p.Icon,
                pr?.LastLoginAt, seen, pr?.CurrentPage,
                seen.HasValue && (now - seen.Value).TotalMinutes <= OnlineWindowMinutes,
                todayCounts.TryGetValue(p.Id, out var n) ? n : 0);
        })
        .OrderByDescending(x => x.IsOnline)
        .ThenByDescending(x => x.LastSeenAt)
        .ThenBy(x => x.FullName)
        .ToList();

        return Ok(list);
    }

    /// <summary>
    /// One teammate's page history, newest first, filtered and capped in the database
    /// so a long-serving account never drags its whole life into memory.
    /// </summary>
    [HttpGet("{userId:int}/visits")]
    [RequirePerm(Perm.Team)]
    public async Task<ActionResult<List<TeamVisitDto>>> Visits(int userId, DateTime? from, DateTime? to, int take = 400)
    {
        if (CurrentRestaurantId == 0) return Ok(new List<TeamVisitDto>());

        // Only people who actually belong to this store may be inspected.
        var belongs = await db.Restaurants.AnyAsync(r => r.Id == CurrentRestaurantId && r.OwnerUserId == userId)
                   || await db.StoreMembers.AnyAsync(m => m.RestaurantId == CurrentRestaurantId && m.UserId == userId);
        if (!belongs) return Forbid();

        var q = db.ActivityLogs.AsNoTracking().Where(a => a.UserId == userId && a.App == "Partner");
        if (from is { } f) q = q.Where(a => a.At >= f.Date);
        if (to is { } t) q = q.Where(a => a.At < t.Date.AddDays(1));

        var rows = await q.OrderByDescending(a => a.At)
            .Take(Math.Clamp(take, 1, 2000))
            .Select(a => new TeamVisitDto(a.Page, a.At, a.Ip))
            .ToListAsync();
        return Ok(rows);
    }
}
