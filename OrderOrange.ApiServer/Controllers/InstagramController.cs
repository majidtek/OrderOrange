using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// A shop posting to its OWN Instagram. The owner connects the account once by pasting a
/// long-lived Instagram token; it is kept for that store and never shown again.
///
/// Instagram will not accept a picture from our hands — it fetches one from a public
/// address itself. Everything postable here therefore already has a public URL on
/// api.orderorange.com: a dish photo, a store photo, or a picture the owner uploads now
/// (which is filed as a store photo first, so it has an address to be fetched from).
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
public class InstagramController(
    AppDbContext db,
    InstagramStore store,
    InstagramQueueStore queue,
    InstagramGraph graph,
    CatalogStore catalog,
    IConfiguration config,
    ILogger<InstagramController> log) : ApiControllerBase
{
    // ---------- connection ----------

    [HttpGet]
    [RequirePerm(Perm.Settings)]
    public async Task<ActionResult<InstagramStatusDto>> Status()
    {
        var doc = await store.GetAsync(CurrentRestaurantId);
        if (doc is null) return new InstagramStatusDto(false);

        // A token close to its end is swapped for a fresh 60-day one while we are here,
        // so a store that keeps posting never has to paste a token twice.
        doc = await RefreshIfNeededAsync(doc);

        try
        {
            var me = await graph.MeAsync(doc.IgToken);
            var (used, total) = await graph.QuotaAsync(doc.IgUserId, doc.IgToken);
            return Status(doc, me.Username, used, total, null);
        }
        catch (InstagramException ex)
        {
            // Connected but unhappy: expired token, account switched to personal, access
            // removed. Say so on the page instead of failing the whole call.
            return Status(doc, doc.IgUsername, 0, 0, ex.Message);
        }
    }

    private InstagramStatusDto Status(StoreSocialDoc doc, string username, int used, int total, string? problem) =>
        new(true, username, doc.IgUserId, doc.IgExpiresAt,
            doc.IgExpiresAt is { } e ? Math.Max(0, (int)(e - DateTime.UtcNow).TotalDays) : 0,
            used, total, doc.LastPostUrl, doc.LastPostAt?.ToLocalTime(), problem);

    /// <summary>Paste a long-lived Instagram token; we work out whose account it is.</summary>
    [HttpPost("connect")]
    [RequirePerm(Perm.Settings)]
    public async Task<ActionResult<InstagramStatusDto>> Connect(ConnectInstagramRequest req)
    {
        var token = (req.Token ?? "").Trim();
        if (token.Length < 20) return BadRequest(new { code = "ig.token", message = "Paste the access token from Instagram." });

        InstagramGraph.Me me;
        try { me = await graph.MeAsync(token); }
        catch (InstagramException ex) { return BadRequest(new { code = "ig.bad", message = ex.Message }); }

        if (me.AccountType is not ("BUSINESS" or "MEDIA_CREATOR" or "CREATOR"))
            return BadRequest(new { code = "ig.personal", message = "That account is personal. Switch it to a Business or Creator account in the Instagram app, then connect again." });

        // A token pasted today may not be refreshable yet; ask Instagram for its life where
        // we can, otherwise assume the 60 days a long-lived token is born with.
        DateTime expires;
        try { (_, expires) = await graph.RefreshAsync(token); }
        catch { expires = DateTime.UtcNow.AddDays(60); }

        var doc = await store.GetAsync(CurrentRestaurantId) ?? new StoreSocialDoc { Id = CurrentRestaurantId, ConnectedAt = DateTime.UtcNow };
        doc.IgUserId = me.UserId;
        doc.IgUsername = me.Username;
        doc.IgToken = token;
        doc.IgExpiresAt = expires;
        await store.SaveAsync(doc);
        log.LogInformation("Store {Store} connected Instagram @{User}", CurrentRestaurantId, me.Username);

        var (used, total) = await graph.QuotaAsync(me.UserId, token);
        return Status(doc, me.Username, used, total, null);
    }

    [HttpDelete]
    [RequirePerm(Perm.Settings)]
    public async Task<IActionResult> Disconnect()
    {
        await store.DeleteAsync(CurrentRestaurantId);
        return NoContent();
    }

    // ---------- posting ----------

    /// <summary>The account's own recent posts.</summary>
    [HttpGet("posts")]
    [RequirePerm(Perm.Settings)]
    public async Task<ActionResult<List<InstagramMediaDto>>> Recent()
    {
        var doc = await store.GetAsync(CurrentRestaurantId);
        if (doc is null) return new List<InstagramMediaDto>();
        return await graph.RecentAsync(doc.IgUserId, doc.IgToken);
    }

    [HttpPost("post")]
    [RequirePerm(Perm.Settings)]
    public async Task<ActionResult<InstagramPostResultDto>> Post(InstagramPostRequest req)
    {
        var doc = await store.GetAsync(CurrentRestaurantId);
        if (doc is null) return BadRequest(new { code = "ig.none", message = "Connect an Instagram account first." });
        if (string.IsNullOrWhiteSpace(req.Caption)) return BadRequest(new { code = "ig.caption", message = "Write a caption." });
        if (req.Caption.Length > 2200) return BadRequest(new { code = "ig.caption", message = "Instagram allows 2,200 characters." });

        if (!MediaLinks.Enabled(config))
            return BadRequest(new { code = "ig.media", message = "Public media links are switched off on this server, so Instagram cannot fetch the picture." });

        string? imageUrl;
        try { imageUrl = await PublicImageUrlAsync(req); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = "ig.image", message = ex.Message }); }
        if (imageUrl is null) return BadRequest(new { code = "ig.image", message = "Choose a picture to post." });

        doc = await RefreshIfNeededAsync(doc);
        try
        {
            var mediaId = await graph.PublishImageAsync(doc.IgUserId, doc.IgToken, imageUrl, req.Caption.Trim());
            var permalink = await graph.PermalinkAsync(mediaId, doc.IgToken);
            await store.RecordPostAsync(CurrentRestaurantId, permalink);
            log.LogInformation("Store {Store} posted {Media} to Instagram", CurrentRestaurantId, mediaId);
            return new InstagramPostResultDto(mediaId, permalink);
        }
        catch (InstagramException ex) { return BadRequest(new { code = "ig.publish", message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = "ig.publish", message = ex.Message }); }
        catch (TimeoutException ex) { return BadRequest(new { code = "ig.slow", message = ex.Message }); }
    }

    // ---------- planned posts ----------

    [HttpGet("scheduled")]
    [RequirePerm(Perm.Settings)]
    public async Task<ActionResult<List<ScheduledPostDto>>> Scheduled()
    {
        var rows = await queue.ForStoreAsync(CurrentRestaurantId);
        var thumbs = await ThumbsAsync(rows);
        return rows.Select(r => r.ToDto(x => thumbs.GetValueOrDefault(Key(x)))).ToList();
    }

    /// <summary>Plan a post for a moment in the store's own time.</summary>
    [HttpPost("schedule")]
    [RequirePerm(Perm.Settings)]
    public async Task<ActionResult<ScheduledPostDto>> Schedule(SchedulePostRequest req)
    {
        if (await store.GetAsync(CurrentRestaurantId) is null)
            return BadRequest(new { code = "ig.none", message = "Connect an Instagram account first." });
        if (string.IsNullOrWhiteSpace(req.Caption))
            return BadRequest(new { code = "ig.caption", message = "Write a caption." });

        // Local because that is the clock the owner reads; stored in UTC because that is
        // the only clock a scheduler can trust.
        var dueUtc = DateTime.SpecifyKind(req.DueAtLocal, DateTimeKind.Local).ToUniversalTime();
        if (dueUtc <= DateTime.UtcNow.AddMinutes(-2))
            return BadRequest(new { code = "ig.past", message = "Pick a time in the future." });

        var doc = new ScheduledPostDoc
        {
            StoreId = CurrentRestaurantId,
            Caption = req.Caption.Trim(),
            DueAtUtc = dueUtc,
            CreatedAt = DateTime.UtcNow,
            MenuItemId = req.MenuItemId,
            StorePhotoId = req.StorePhotoId,
        };

        // An uploaded picture is filed as a store photo now, so it already has the public
        // address Instagram will ask for when the hour comes.
        if (req.PhotoData is { Length: > 0 } data)
        {
            if (!data.StartsWith("data:image/")) return BadRequest(new { code = "ig.image", message = "That file is not a picture." });
            var row = new Models.RestaurantPhoto { RestaurantId = CurrentRestaurantId, Data = data, IsMain = false };
            db.RestaurantPhotos.Add(row);
            await db.SaveChangesAsync();
            doc.StorePhotoId = row.Id;
        }
        if (doc.StorePhotoId is null && doc.MenuItemId is null)
            return BadRequest(new { code = "ig.image", message = "Choose a picture to post." });

        await queue.AddAsync(doc);
        var thumbs = await ThumbsAsync([doc]);
        return doc.ToDto(x => thumbs.GetValueOrDefault(Key(x)));
    }

    [HttpDelete("scheduled/{id}")]
    [RequirePerm(Perm.Settings)]
    public async Task<IActionResult> Cancel(string id)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(id, out var oid)) return NotFound();
        await queue.CancelAsync(oid, CurrentRestaurantId);
        return NoContent();
    }

    private static string Key(ScheduledPostDoc d) => d.StorePhotoId is { } p ? $"p{p}" : $"i{d.MenuItemId}";

    /// <summary>The little pictures the planner shows, resolved in one pass.</summary>
    private async Task<Dictionary<string, string?>> ThumbsAsync(IReadOnlyCollection<ScheduledPostDoc> rows)
    {
        var map = new Dictionary<string, string?>();
        var photoIds = rows.Where(r => r.StorePhotoId is not null).Select(r => r.StorePhotoId!.Value).Distinct().ToList();
        if (photoIds.Count > 0)
        {
            foreach (var row in await db.RestaurantPhotos.Where(p => photoIds.Contains(p.Id))
                         .Select(p => new { p.Id, p.Data }).ToListAsync())
                map[$"p{row.Id}"] = MediaLinks.Banner(config, row.Id, row.Data);
        }
        foreach (var itemId in rows.Where(r => r.StorePhotoId is null && r.MenuItemId is not null)
                     .Select(r => r.MenuItemId!.Value).Distinct())
        {
            var item = await catalog.ApprovedItemAsync(itemId);
            map[$"i{itemId}"] = item is null ? null : MediaLinks.Dish(config, itemId, item.PhotoData);
        }
        return map;
    }

    /// <summary>
    /// Turns the chosen picture into an address Instagram can fetch. An uploaded picture is
    /// filed as one of the store's own photos first — that is what gives it an address.
    /// </summary>
    private async Task<string?> PublicImageUrlAsync(InstagramPostRequest req)
    {
        if (req.MenuItemId is { } itemId)
        {
            var item = await catalog.ApprovedItemAsync(itemId);
            if (item is null || item.RestaurantId != CurrentRestaurantId)
                throw new InvalidOperationException("That product is not on your menu.");
            var url = MediaLinks.Dish(config, itemId, item.PhotoData);
            if (url is null) throw new InvalidOperationException("That product has no photo. Add one, or upload a picture here.");
            return url;
        }

        if (req.StorePhotoId is { } photoId)
        {
            var photo = await db.RestaurantPhotos
                .Where(p => p.Id == photoId && p.RestaurantId == CurrentRestaurantId)
                .Select(p => p.Data).FirstOrDefaultAsync();
            if (photo is not { Length: > 0 }) throw new InvalidOperationException("That photo is not in your gallery.");
            return MediaLinks.Banner(config, photoId, photo);
        }

        if (req.PhotoData is { Length: > 0 } data)
        {
            if (!data.StartsWith("data:image/")) throw new InvalidOperationException("That file is not a picture.");
            var row = new Models.RestaurantPhoto { RestaurantId = CurrentRestaurantId, Data = data, IsMain = false };
            db.RestaurantPhotos.Add(row);
            await db.SaveChangesAsync();
            return MediaLinks.Banner(config, row.Id, data);
        }

        return null;
    }

    /// <summary>Fewer than fifteen days left? Trade it for a fresh sixty.</summary>
    private async Task<StoreSocialDoc> RefreshIfNeededAsync(StoreSocialDoc doc)
    {
        if (doc.IgExpiresAt is not { } expires || (expires - DateTime.UtcNow).TotalDays > 15) return doc;
        try
        {
            var (token, newExpiry) = await graph.RefreshAsync(doc.IgToken);
            await store.UpdateTokenAsync(doc.Id, token, newExpiry);
            doc.IgToken = token;
            doc.IgExpiresAt = newExpiry;
            log.LogInformation("Store {Store} Instagram token refreshed to {Expiry:d}", doc.Id, newExpiry);
        }
        catch (Exception ex) { log.LogWarning(ex, "Instagram token refresh failed for store {Store}", doc.Id); }
        return doc;
    }
}
