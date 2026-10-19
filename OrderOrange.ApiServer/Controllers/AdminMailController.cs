using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The admin mailbox: info@orderorange.com read, answered and tidied from the admin panel
/// (Majed 2026-10-15). Administrators only; the mailbox password never leaves the server.
/// </summary>
[Authorize(Roles = "Administrator")]
public class AdminMailController(MailboxService mail, MailWatcher watcher, ILogger<AdminMailController> logger) : ApiControllerBase
{
    private ObjectResult Failed(Exception ex, string what)
    {
        logger.LogWarning(ex, "Mailbox: {What} failed.", what);
        return StatusCode(502, new { message = $"Mailbox: {ex.Message}" });
    }

    [HttpGet("~/api/admin/mail/status")]
    public ActionResult<MailStatusDto> Status() =>
        new MailStatusDto(mail.IsConfigured, mail.Account, watcher.Unread, watcher.LastCheckAt, watcher.LastError, watcher.PollMinutes);

    /// <summary>The badge: unread in the inbox, asked live (cheap STATUS call).</summary>
    [HttpGet("~/api/admin/mail/unread")]
    public async Task<ActionResult<int>> Unread()
    {
        if (!mail.IsConfigured) return 0;
        try { return (await mail.InboxStatusAsync()).Unread; }
        catch (Exception ex) { logger.LogWarning(ex, "Mailbox unread failed."); return watcher.Unread; }
    }

    [HttpGet("~/api/admin/mail/folders")]
    public async Task<ActionResult<List<MailFolderDto>>> Folders()
    {
        if (!mail.IsConfigured) return BadRequest(new { message = "No mailbox is configured on the server." });
        try { return await mail.FoldersAsync(); }
        catch (Exception ex) { return Failed(ex, "folders"); }
    }

    [HttpGet("~/api/admin/mail/messages")]
    public async Task<ActionResult<MailPageDto>> Messages(string folder = "INBOX", int skip = 0, int take = 25, string? search = null, bool unread = false)
    {
        if (!mail.IsConfigured) return BadRequest(new { message = "No mailbox is configured on the server." });
        try { return await mail.ListAsync(folder, skip, take, search, unread); }
        catch (Exception ex) { return Failed(ex, "list"); }
    }

    [HttpGet("~/api/admin/mail/messages/{uid:long}")]
    public async Task<ActionResult<MailMessageDto>> Message(long uid, string folder = "INBOX")
    {
        try
        {
            var m = await mail.GetAsync(folder, (uint)uid);
            return m is null ? NotFound() : m;
        }
        catch (Exception ex) { return Failed(ex, "open"); }
    }

    [HttpGet("~/api/admin/mail/messages/{uid:long}/attachments/{index:int}")]
    public async Task<ActionResult<MailFileDto>> Attachment(long uid, int index, string folder = "INBOX")
    {
        try
        {
            var f = await mail.AttachmentAsync(folder, (uint)uid, index);
            return f is null ? NotFound() : f;
        }
        catch (Exception ex) { return Failed(ex, "attachment"); }
    }

    [HttpPost("~/api/admin/mail/mark")]
    public async Task<IActionResult> Mark(MailMarkRequest req)
    {
        if (req.Uids is not { Count: > 0 }) return BadRequest(new { message = "Nothing selected." });
        try { await mail.MarkAsync(req.Folder, req.Uids, req.Seen); return NoContent(); }
        catch (Exception ex) { return Failed(ex, "mark"); }
    }

    [HttpPost("~/api/admin/mail/delete")]
    public async Task<IActionResult> Delete(MailDeleteRequest req)
    {
        if (req.Uids is not { Count: > 0 }) return BadRequest(new { message = "Nothing selected." });
        try { await mail.DeleteAsync(req.Folder, req.Uids); return NoContent(); }
        catch (Exception ex) { return Failed(ex, "delete"); }
    }

    [HttpPost("~/api/admin/mail/send")]
    public async Task<IActionResult> Send(MailSendRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.To)) return BadRequest(new { message = "Enter at least one recipient." });
        if (string.IsNullOrWhiteSpace(req.Subject) && string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { message = "Write a subject or a message." });
        try { await mail.SendAsync(req); return NoContent(); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) { return Failed(ex, "send"); }
    }
}
