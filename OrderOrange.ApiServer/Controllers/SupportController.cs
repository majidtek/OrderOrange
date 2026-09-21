using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using OrderOrange.ApiServer.Data;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Support tickets, from the first report to the fix. The partner side sits behind
/// <see cref="Perm.Support"/> and only ever sees its own store; the support desk
/// (administrators) sees every store under <c>api/admin/support/…</c>.
///
/// Lifecycle: open → in_progress ⇄ waiting → resolved → closed. A partner reply on a
/// resolved or closed ticket reopens it; a support reply on an open one starts work.
/// </summary>
public class SupportController(AppDbContext db, SupportStore store) : ApiControllerBase
{
    // =====================================================================
    //  Partner side
    // =====================================================================

    [HttpGet("tickets")]
    [RequirePerm(Perm.Support)]
    public async Task<ActionResult<SupportPageDto>> Mine(string status = "all", int skip = 0, int take = 50)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        return Ok(await PageAsync(CurrentRestaurantId, status, null, skip, take, partnerSide: true));
    }

    [HttpPost("tickets")]
    [RequirePerm(Perm.Support)]
    public async Task<ActionResult<SupportTicketDetailDto>> Create(CreateTicketRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var subject = (req.Subject ?? "").Trim();
        var text = (req.Text ?? "").Trim();
        if (subject.Length is 0 or > SupportCatalog.MaxSubjectChars) return BadRequest(new { code = "tk.e.subject" });
        if (text.Length is 0 or > SupportCatalog.MaxTextChars) return BadRequest(new { code = "tk.e.text" });
        if (ValidateAttachments(req.Attachments) is { } bad) return BadRequest(new { code = bad });

        var storeName = await db.Restaurants.Where(r => r.Id == CurrentRestaurantId).Select(r => r.Name).FirstOrDefaultAsync() ?? $"#{CurrentRestaurantId}";
        var now = DateTime.Now;
        var doc = new TicketDoc
        {
            Id = ObjectId.GenerateNewId(),
            Number = await store.NextNumberAsync(),
            RestaurantId = CurrentRestaurantId,
            RestaurantName = storeName,
            OpenedByUserId = CurrentUserId,
            OpenedByName = CurrentUserName,
            Subject = subject,
            Category = SupportCatalog.Categories.Contains(req.Category) ? req.Category : "other",
            Priority = SupportCatalog.Priorities.Contains(req.Priority) ? req.Priority : "normal",
            Status = "open",
            CreatedAt = now, UpdatedAt = now, LastMessageAt = now,
            LastFrom = "partner",
            LastPreview = Preview(text, req.Attachments),
            SupportUnread = true,
        };
        var files = await SaveFilesAsync(doc, req.Attachments);
        doc.Messages.Add(new TicketMessageDoc
        {
            Id = 1, From = "partner", AuthorId = CurrentUserId, AuthorName = CurrentUserName,
            Text = text, Attachments = files, At = now,
        });
        await store.InsertAsync(doc);
        return Ok(Detail(doc));
    }

    [HttpGet("tickets/{id}")]
    [RequirePerm(Perm.Support)]
    public async Task<ActionResult<SupportTicketDetailDto>> Get(string id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await store.GetAsync(oid, CurrentRestaurantId);
        if (doc is null) return NotFound();
        if (doc.PartnerUnread) { doc.PartnerUnread = false; await store.MarkReadAsync(oid, partnerSide: true); }
        return Ok(Detail(doc));
    }

    /// <summary>The partner writes back. On a resolved/closed ticket that reopens it.</summary>
    [HttpPost("tickets/{id}/messages")]
    [RequirePerm(Perm.Support)]
    public async Task<ActionResult<SupportTicketDetailDto>> Reply(string id, TicketReplyRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await store.GetAsync(oid, CurrentRestaurantId);
        if (doc is null) return NotFound();

        var text = (req.Text ?? "").Trim();
        var hasFiles = req.Attachments is { Count: > 0 };
        if ((text.Length == 0 && !hasFiles) || text.Length > SupportCatalog.MaxTextChars) return BadRequest(new { code = "tk.e.text" });
        if (ValidateAttachments(req.Attachments) is { } bad) return BadRequest(new { code = bad });

        var now = DateTime.Now;
        if (doc.Status is "resolved" or "closed")
        {
            doc.Status = "open"; doc.ResolvedAt = null; doc.ClosedAt = null;
            doc.Messages.Add(new TicketMessageDoc { Id = NextId(doc), From = "system", AuthorName = CurrentUserName, Kind = "status", StatusTo = "open", At = now });
        }
        else if (doc.Status == "waiting") doc.Status = "in_progress";

        var files = await SaveFilesAsync(doc, req.Attachments);
        doc.Messages.Add(new TicketMessageDoc
        {
            Id = NextId(doc), From = "partner", AuthorId = CurrentUserId, AuthorName = CurrentUserName,
            Text = text, Attachments = files, At = now,
        });
        doc.LastFrom = "partner"; doc.LastPreview = Preview(text, req.Attachments);
        doc.LastMessageAt = now; doc.UpdatedAt = now;
        doc.SupportUnread = true; doc.PartnerUnread = false;
        await store.ReplaceAsync(doc);
        return Ok(Detail(doc));
    }

    /// <summary>"Problem solved" — the partner closes the ticket themselves.</summary>
    [HttpPost("tickets/{id}/close")]
    [RequirePerm(Perm.Support)]
    public async Task<ActionResult<SupportTicketDetailDto>> Close(string id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await store.GetAsync(oid, CurrentRestaurantId);
        if (doc is null) return NotFound();
        if (doc.Status != "closed")
        {
            var now = DateTime.Now;
            doc.Status = "closed"; doc.ClosedAt = now; doc.ResolvedAt ??= now; doc.UpdatedAt = now;
            doc.Messages.Add(new TicketMessageDoc { Id = NextId(doc), From = "system", AuthorName = CurrentUserName, Kind = "status", StatusTo = "closed", At = now });
            doc.SupportUnread = true;
            await store.ReplaceAsync(doc);
        }
        return Ok(Detail(doc));
    }

    [HttpPost("tickets/{id}/rate")]
    [RequirePerm(Perm.Support)]
    public async Task<ActionResult<SupportTicketDetailDto>> Rate(string id, TicketRateRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        if (req.Rating is < 1 or > 5) return BadRequest(new { message = "Rating must be 1–5." });
        var doc = await store.GetAsync(oid, CurrentRestaurantId);
        if (doc is null) return NotFound();
        if (doc.Status is not ("resolved" or "closed")) return BadRequest(new { message = "Rate the help once the ticket is resolved." });
        doc.Rating = req.Rating; doc.RatingNote = Trim(req.Note, 500); doc.UpdatedAt = DateTime.Now;
        await store.ReplaceAsync(doc);
        return Ok(Detail(doc));
    }

    [HttpGet("tickets/{id}/files/{fileId}")]
    [RequirePerm(Perm.Support)]
    public async Task<ActionResult<SupportFileDto>> File(string id, string fileId)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        return await FileFor(id, fileId, CurrentRestaurantId);
    }

    // =====================================================================
    //  Support desk (administrators)
    // =====================================================================

    [HttpGet("~/api/admin/support/tickets")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<SupportPageDto>> AdminList(string status = "all", string? search = null, int skip = 0, int take = 50) =>
        Ok(await PageAsync(null, status, search, skip, take, partnerSide: false));

    [HttpGet("~/api/admin/support/summary")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<SupportSummaryDto>> AdminSummary()
    {
        var s = await store.SummaryAsync(null, partnerSide: false);
        return Ok(new SupportSummaryDto(s.Open, s.InProgress, s.Waiting, s.Resolved, s.Closed, s.Unread));
    }

    [HttpGet("~/api/admin/support/tickets/{id}")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<SupportTicketDetailDto>> AdminGet(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await store.GetAsync(oid, null);
        if (doc is null) return NotFound();
        if (doc.SupportUnread) { doc.SupportUnread = false; await store.MarkReadAsync(oid, partnerSide: false); }
        return Ok(Detail(doc));
    }

    /// <summary>Support answers. An open ticket moves to in_progress; the answerer takes it.</summary>
    [HttpPost("~/api/admin/support/tickets/{id}/messages")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<SupportTicketDetailDto>> AdminReply(string id, TicketReplyRequest req)
    {
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await store.GetAsync(oid, null);
        if (doc is null) return NotFound();

        var text = (req.Text ?? "").Trim();
        var hasFiles = req.Attachments is { Count: > 0 };
        if ((text.Length == 0 && !hasFiles) || text.Length > SupportCatalog.MaxTextChars) return BadRequest(new { code = "tk.e.text" });
        if (ValidateAttachments(req.Attachments) is { } bad) return BadRequest(new { code = bad });

        var now = DateTime.Now;
        if (doc.Status == "open") doc.Status = "in_progress";
        doc.AssignedTo ??= CurrentUserName;
        var files = await SaveFilesAsync(doc, req.Attachments);
        doc.Messages.Add(new TicketMessageDoc
        {
            Id = NextId(doc), From = "support", AuthorId = CurrentUserId, AuthorName = CurrentUserName,
            Text = text, Attachments = files, At = now,
        });
        doc.LastFrom = "support"; doc.LastPreview = Preview(text, req.Attachments);
        doc.LastMessageAt = now; doc.UpdatedAt = now;
        doc.PartnerUnread = true; doc.SupportUnread = false;
        await store.ReplaceAsync(doc);
        return Ok(Detail(doc));
    }

    [HttpPost("~/api/admin/support/tickets/{id}/status")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<SupportTicketDetailDto>> AdminStatus(string id, TicketStatusRequest req)
    {
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        if (!SupportCatalog.Statuses.Contains(req.Status)) return BadRequest(new { message = "Unknown status." });
        var doc = await store.GetAsync(oid, null);
        if (doc is null) return NotFound();

        var now = DateTime.Now;
        doc.Status = req.Status;
        doc.AssignedTo ??= CurrentUserName;
        if (req.Status == "resolved") doc.ResolvedAt = now;
        if (req.Status == "closed") { doc.ClosedAt = now; doc.ResolvedAt ??= now; }
        if (req.Status is "open" or "in_progress" or "waiting") { doc.ResolvedAt = null; doc.ClosedAt = null; }
        doc.Messages.Add(new TicketMessageDoc
        {
            Id = NextId(doc), From = "system", AuthorId = CurrentUserId, AuthorName = CurrentUserName,
            Kind = "status", StatusTo = req.Status, Text = Trim(req.Note, 1000) ?? "", At = now,
        });
        doc.LastMessageAt = now; doc.UpdatedAt = now; doc.LastFrom = "support";
        if (!string.IsNullOrWhiteSpace(req.Note)) doc.LastPreview = Trim(req.Note, 120)!;
        doc.PartnerUnread = true;
        await store.ReplaceAsync(doc);
        return Ok(Detail(doc));
    }

    [HttpPost("~/api/admin/support/tickets/{id}/assign")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<SupportTicketDetailDto>> AdminAssign(string id, TicketAssignRequest req)
    {
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await store.GetAsync(oid, null);
        if (doc is null) return NotFound();
        doc.AssignedTo = Trim(req.AssignedTo, 80);
        doc.UpdatedAt = DateTime.Now;
        await store.ReplaceAsync(doc);
        return Ok(Detail(doc));
    }

    [HttpGet("~/api/admin/support/tickets/{id}/files/{fileId}")]
    [Authorize(Roles = "Administrator")]
    public Task<ActionResult<SupportFileDto>> AdminFile(string id, string fileId) => FileFor(id, fileId, null);

    // =====================================================================
    //  Shared plumbing
    // =====================================================================

    private async Task<SupportPageDto> PageAsync(int? restaurantId, string status, string? search, int skip, int take, bool partnerSide)
    {
        take = Math.Clamp(take, 1, 200); skip = Math.Max(0, skip);
        var (items, total) = await store.ListAsync(restaurantId, status, search, skip, take);
        var counts = await store.MessageCountsAsync(items.Select(t => t.Id));
        var s = await store.SummaryAsync(restaurantId, partnerSide);
        return new SupportPageDto(
            items.Select(t => ToDto(t, counts.GetValueOrDefault(t.Id))).ToList(),
            (int)total,
            new SupportSummaryDto(s.Open, s.InProgress, s.Waiting, s.Resolved, s.Closed, s.Unread));
    }

    private async Task<ActionResult<SupportFileDto>> FileFor(string id, string fileId, int? restaurantId)
    {
        if (!ObjectId.TryParse(id, out var oid) || !ObjectId.TryParse(fileId, out var fid)) return NotFound();
        var doc = await store.GetAsync(oid, restaurantId);
        if (doc is null) return NotFound();
        var file = await store.GetFileAsync(oid, fid);
        if (file is null) return NotFound();
        return Ok(new SupportFileDto(file.Id.ToString(), file.Name, file.Mime, file.Size, file.Data));
    }

    private static string? ValidateAttachments(List<NewAttachmentRequest>? list)
    {
        if (list is null or { Count: 0 }) return null;
        if (list.Count > SupportCatalog.MaxAttachments) return "tk.e.tooMany";
        foreach (var a in list)
        {
            if (string.IsNullOrEmpty(a.Data) || !a.Data.StartsWith("data:") || !a.Data.Contains(',')) return "tk.e.attach";
            if (a.Data.Length > SupportCatalog.MaxAttachmentChars) return "tk.e.attach";
        }
        return null;
    }

    private async Task<List<TicketAttachmentRef>> SaveFilesAsync(TicketDoc doc, List<NewAttachmentRequest>? list)
    {
        var refs = new List<TicketAttachmentRef>();
        if (list is null) return refs;
        foreach (var a in list)
        {
            var comma = a.Data.IndexOf(',');
            var meta = a.Data[5..comma];
            var mime = meta.Split(';')[0];
            if (mime.Length == 0) mime = "application/octet-stream";
            var size = (int)((a.Data.Length - comma - 1) * 3L / 4);
            var name = ReservationsController.SafeFileName(a.Name) ?? (mime.StartsWith("image/") ? "photo" : "file");
            var file = new TicketFileDoc
            {
                Id = ObjectId.GenerateNewId(), TicketId = doc.Id, RestaurantId = doc.RestaurantId,
                Name = name, Mime = mime, Size = size, Data = a.Data, At = DateTime.Now,
            };
            await store.AddFileAsync(file);
            refs.Add(new TicketAttachmentRef { Id = file.Id.ToString(), Name = name, Mime = mime, Size = size, IsImage = mime.StartsWith("image/") });
        }
        return refs;
    }

    private static int NextId(TicketDoc doc) => doc.Messages.Count == 0 ? 1 : doc.Messages.Max(m => m.Id) + 1;

    private static string Preview(string text, List<NewAttachmentRequest>? files)
    {
        if (text.Length > 0) return text.Length > 120 ? text[..120] + "…" : text;
        return files is { Count: > 0 } ? (files.Any(f => f.Data.StartsWith("data:image/")) ? "📷" : "📎") : "";
    }

    private static string? Trim(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    private static SupportTicketDto ToDto(TicketDoc t, int messageCount) => new(
        t.Id.ToString(), t.Number, t.RestaurantId, t.RestaurantName, t.OpenedByName,
        t.Subject, t.Category, t.Priority, t.Status, t.AssignedTo,
        t.CreatedAt, t.UpdatedAt, t.LastMessageAt, t.LastFrom, t.LastPreview,
        t.PartnerUnread, t.SupportUnread, messageCount, t.Rating, t.ResolvedAt, t.ClosedAt);

    private static SupportTicketDetailDto Detail(TicketDoc t) => new(
        ToDto(t, t.Messages.Count),
        t.Messages.OrderBy(m => m.Id).Select(m => new SupportMessageDto(
            m.Id, m.From, m.AuthorName, m.Text,
            m.Attachments.Select(a => new SupportAttachmentDto(a.Id, a.Name, a.Mime, a.Size, a.IsImage)).ToList(),
            m.At, m.Kind, m.StatusTo)).ToList());
}
