using System.Text;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The platform mailbox (info@orderorange.com) read and answered from the admin panel
/// (Majed 2026-10-15: "link my email to the admin panel, I can manage my email and add
/// notification"). IMAP for reading, SMTP for sending — both from the same "Smtp" settings
/// the sign-in codes already use, plus optional Mail:ImapHost / Mail:ImapPort. Every call
/// opens its own short connection; the box is small and this keeps the code honest.
/// </summary>
public sealed class MailboxService(IConfiguration config, ILogger<MailboxService> logger)
{
    private string? SmtpHost => config["Smtp:Host"] is { Length: > 0 } h ? h : null;
    private string ImapHost => config["Mail:ImapHost"] is { Length: > 0 } h ? h : SmtpHost ?? "";
    private int ImapPort => int.TryParse(config["Mail:ImapPort"], out var p) ? p : 993;
    private int SmtpPort => int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
    private string User => config["Smtp:User"] ?? "";
    private string Password => (config["Smtp:Password"] ?? "").Replace(" ", "");
    private string From => config["Smtp:From"] is { Length: > 0 } f ? f : User;
    private string FromName => config["Smtp:FromName"] is { Length: > 0 } n ? n : "OrderOrange";

    public bool IsConfigured => ImapHost.Length > 0 && User.Length > 0;
    public string Account => User;

    private async Task<ImapClient> OpenAsync(CancellationToken ct = default)
    {
        var client = new ImapClient { Timeout = 30_000 };
        await client.ConnectAsync(ImapHost, ImapPort, ImapPort == 143 ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.SslOnConnect, ct);
        await client.AuthenticateAsync(User, Password, ct);
        return client;
    }

    private static string KindOf(IMailFolder f)
    {
        if (f.Attributes.HasFlag(FolderAttributes.Inbox) || f.FullName.Equals("INBOX", StringComparison.OrdinalIgnoreCase)) return "inbox";
        if (f.Attributes.HasFlag(FolderAttributes.Sent)) return "sent";
        if (f.Attributes.HasFlag(FolderAttributes.Drafts)) return "drafts";
        if (f.Attributes.HasFlag(FolderAttributes.Trash)) return "trash";
        if (f.Attributes.HasFlag(FolderAttributes.Junk)) return "junk";
        var n = f.Name.ToLowerInvariant();
        return n.Contains("sent") ? "sent" : n.Contains("draft") ? "drafts" : n.Contains("trash") || n.Contains("deleted") ? "trash"
            : n.Contains("junk") || n.Contains("spam") ? "junk" : "other";
    }

    public async Task<List<MailFolderDto>> FoldersAsync(CancellationToken ct = default)
    {
        using var client = await OpenAsync(ct);
        var folders = new List<IMailFolder> { client.Inbox };
        foreach (var ns in client.PersonalNamespaces)
            folders.AddRange(await client.GetFoldersAsync(ns, false, ct));
        var result = new List<MailFolderDto>();
        foreach (var f in folders.DistinctBy(f => f.FullName))
        {
            if (f.Attributes.HasFlag(FolderAttributes.NonExistent) || f.Attributes.HasFlag(FolderAttributes.NoSelect)) continue;
            try { await f.StatusAsync(StatusItems.Count | StatusItems.Unread, ct); } catch { continue; }
            result.Add(new MailFolderDto(f.FullName, f.Name, KindOf(f), f.Unread, f.Count));
        }
        await client.DisconnectAsync(true, ct);
        // Inbox first, then the well-known ones, then the rest by name.
        string[] order = ["inbox", "sent", "drafts", "junk", "trash", "other"];
        return result.OrderBy(f => Array.IndexOf(order, f.Kind)).ThenBy(f => f.Name).ToList();
    }

    private async Task<IMailFolder> FolderAsync(ImapClient client, string path, FolderAccess access, CancellationToken ct)
    {
        var folder = string.IsNullOrEmpty(path) || path.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(path, ct);
        await folder.OpenAsync(access, ct);
        return folder;
    }

    public async Task<MailPageDto> ListAsync(string folderPath, int skip, int take, string? search, bool unreadOnly, CancellationToken ct = default)
    {
        using var client = await OpenAsync(ct);
        var folder = await FolderAsync(client, folderPath, FolderAccess.ReadOnly, ct);

        SearchQuery query = SearchQuery.All;
        if (unreadOnly) query = SearchQuery.NotSeen;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // Sender or subject — never "To": every mail in this box is to the same address.
            query = query.And(SearchQuery.SubjectContains(s).Or(SearchQuery.FromContains(s)));
        }
        var uids = await folder.SearchAsync(query, ct);          // ascending uid = oldest first
        var page = uids.Reverse().Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToList();
        var items = new List<MailSummaryDto>();
        if (page.Count > 0)
        {
            var summaries = await folder.FetchAsync(page,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.PreviewText, ct);
            foreach (var m in summaries.OrderByDescending(m => m.UniqueId.Id))
            {
                var from = m.Envelope?.From?.Mailboxes.FirstOrDefault();
                items.Add(new MailSummaryDto(
                    m.UniqueId.Id, folder.FullName,
                    from?.Name is { Length: > 0 } n ? n : from?.Address ?? "", from?.Address ?? "",
                    m.Envelope?.Subject ?? "", (m.Envelope?.Date ?? m.InternalDate ?? DateTimeOffset.Now).LocalDateTime,
                    m.Flags?.HasFlag(MessageFlags.Seen) == true, m.Attachments.Any(), m.Size ?? 0,
                    Clip(m.PreviewText ?? "", 140)));
            }
        }
        var unread = unreadOnly ? uids.Count : (await folder.SearchAsync(SearchQuery.NotSeen, ct)).Count;
        await client.DisconnectAsync(true, ct);
        return new MailPageDto(items, uids.Count, unread);
    }

    public async Task<MailMessageDto?> GetAsync(string folderPath, uint uid, CancellationToken ct = default)
    {
        using var client = await OpenAsync(ct);
        var folder = await FolderAsync(client, folderPath, FolderAccess.ReadWrite, ct);
        var id = new UniqueId(uid);
        MimeMessage msg;
        try { msg = await folder.GetMessageAsync(id, ct); }
        catch (MessageNotFoundException) { return null; }
        // Opening it is reading it.
        try { await folder.AddFlagsAsync(id, MessageFlags.Seen, true, ct); } catch { }

        var attachments = msg.Attachments.Select((a, i) => new MailAttachmentDto(
            i, a is MimePart p ? p.FileName ?? $"attachment-{i + 1}" : $"attachment-{i + 1}",
            a.ContentType.MimeType, a is MimePart mp && mp.Content is { } c ? c.Stream.Length : 0)).ToList();

        var html = msg.HtmlBody;
        if (html is not null)
        {
            // Inline pictures travel as cid: parts; the browser needs them as data: URIs.
            long budget = 3_000_000;
            foreach (var part in msg.BodyParts.OfType<MimePart>().Where(p => p.ContentId is { Length: > 0 } && p.Content is not null))
            {
                using var ms = new MemoryStream();
                await part.Content.DecodeToAsync(ms, ct);
                if (ms.Length > budget) continue;
                budget -= ms.Length;
                var cid = part.ContentId.Trim('<', '>');
                html = html.Replace("cid:" + cid, $"data:{part.ContentType.MimeType};base64,{Convert.ToBase64String(ms.ToArray())}", StringComparison.OrdinalIgnoreCase);
            }
        }
        var from = msg.From.Mailboxes.FirstOrDefault();
        var dto = new MailMessageDto(
            uid, folder.FullName,
            from?.Name is { Length: > 0 } n ? n : from?.Address ?? "", from?.Address ?? "",
            msg.To.Mailboxes.Select(m => m.ToString()).ToList(), msg.Cc.Mailboxes.Select(m => m.ToString()).ToList(),
            msg.Subject ?? "", msg.Date.LocalDateTime, html, msg.TextBody, attachments, msg.MessageId);
        await client.DisconnectAsync(true, ct);
        return dto;
    }

    public async Task<MailFileDto?> AttachmentAsync(string folderPath, uint uid, int index, CancellationToken ct = default)
    {
        using var client = await OpenAsync(ct);
        var folder = await FolderAsync(client, folderPath, FolderAccess.ReadOnly, ct);
        MimeMessage msg;
        try { msg = await folder.GetMessageAsync(new UniqueId(uid), ct); }
        catch (MessageNotFoundException) { return null; }
        var entity = msg.Attachments.ElementAtOrDefault(index);
        if (entity is null) return null;
        using var ms = new MemoryStream();
        if (entity is MimePart part) await part.Content.DecodeToAsync(ms, ct);
        else if (entity is MessagePart mp) await mp.Message.WriteToAsync(ms, ct);
        if (ms.Length > 20_000_000) return null;
        var name = entity is MimePart p2 ? p2.FileName ?? $"attachment-{index + 1}" : $"attachment-{index + 1}.eml";
        await client.DisconnectAsync(true, ct);
        return new MailFileDto(name, entity.ContentType.MimeType, Convert.ToBase64String(ms.ToArray()));
    }

    public async Task MarkAsync(string folderPath, IList<uint> uids, bool seen, CancellationToken ct = default)
    {
        using var client = await OpenAsync(ct);
        var folder = await FolderAsync(client, folderPath, FolderAccess.ReadWrite, ct);
        var ids = uids.Select(u => new UniqueId(u)).ToList();
        if (seen) await folder.AddFlagsAsync(ids, MessageFlags.Seen, true, ct);
        else await folder.RemoveFlagsAsync(ids, MessageFlags.Seen, true, ct);
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>To the Trash when the server has one; gone for good when it is already the Trash.</summary>
    public async Task DeleteAsync(string folderPath, IList<uint> uids, CancellationToken ct = default)
    {
        using var client = await OpenAsync(ct);
        var folder = await FolderAsync(client, folderPath, FolderAccess.ReadWrite, ct);
        var ids = uids.Select(u => new UniqueId(u)).ToList();
        var trash = await SpecialAsync(client, SpecialFolder.Trash, ["Trash", "Deleted Items", "Deleted"], ct);
        if (trash is not null && trash.FullName != folder.FullName)
        {
            await folder.MoveToAsync(ids, trash, ct);
        }
        else
        {
            await folder.AddFlagsAsync(ids, MessageFlags.Deleted, true, ct);
            await folder.ExpungeAsync(ct);
        }
        await client.DisconnectAsync(true, ct);
    }

    private static async Task<IMailFolder?> SpecialAsync(ImapClient client, SpecialFolder which, string[] names, CancellationToken ct)
    {
        try { if (client.GetFolder(which) is { } f) return f; } catch { }
        foreach (var ns in client.PersonalNamespaces)
        {
            foreach (var f in await client.GetFoldersAsync(ns, false, ct))
                if (names.Any(n => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) return f;
        }
        return null;
    }

    /// <summary>Sends, files a copy in Sent, and flags the answered original.</summary>
    public async Task SendAsync(MailSendRequest req, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(FromName, From));
        foreach (var a in SplitAddresses(req.To)) message.To.Add(a);
        foreach (var a in SplitAddresses(req.Cc)) message.Cc.Add(a);
        if (message.To.Count == 0) throw new ArgumentException("Enter at least one recipient.");
        message.Subject = req.Subject ?? "";

        var body = new BodyBuilder();
        if (req.Html) body.HtmlBody = req.Body;
        else
        {
            body.TextBody = req.Body;
            body.HtmlBody = "<div style=\"font-family:Segoe UI,Tahoma,Arial,sans-serif;font-size:14px;white-space:pre-wrap\">" +
                            System.Net.WebUtility.HtmlEncode(req.Body) + "</div>";
        }
        message.Body = body.ToMessageBody();

        // Threading: answer under the original.
        if (req.ReplyUid > 0)
        {
            try
            {
                using var imap = await OpenAsync(ct);
                var folder = await FolderAsync(imap, req.ReplyFolder ?? "INBOX", FolderAccess.ReadWrite, ct);
                var original = await folder.GetMessageAsync(new UniqueId(req.ReplyUid), ct);
                if (original.MessageId is { Length: > 0 } mid)
                {
                    message.InReplyTo = mid;
                    foreach (var r in original.References) message.References.Add(r);
                    message.References.Add(mid);
                }
                await folder.AddFlagsAsync(new UniqueId(req.ReplyUid), MessageFlags.Answered, true, ct);
                await imap.DisconnectAsync(true, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Could not thread the reply under {Uid}.", req.ReplyUid); }
        }

        using (var smtp = new SmtpClient { Timeout = 30_000 })
        {
            await smtp.ConnectAsync(SmtpHost, SmtpPort, SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            await smtp.AuthenticateAsync(User, Password, ct);
            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(true, ct);
        }

        // A copy in Sent, so the panel shows what went out.
        try
        {
            using var imap = await OpenAsync(ct);
            var sent = await SpecialAsync(imap, SpecialFolder.Sent, ["Sent", "Sent Items", "Sent Messages"], ct);
            if (sent is not null) await sent.AppendAsync(message, MessageFlags.Seen, ct);
            await imap.DisconnectAsync(true, ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Sent, but could not file the copy in Sent."); }
    }

    private static IEnumerable<MailboxAddress> SplitAddresses(string? text) =>
        (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => MailboxAddress.TryParse(t, out var a) ? a : null)
            .Where(a => a is not null)!;

    /// <summary>The watcher's cheap question: how many unread, and the next uid the server will hand out.</summary>
    public async Task<(int Unread, uint UidNext)> InboxStatusAsync(CancellationToken ct = default)
    {
        using var client = await OpenAsync(ct);
        await client.Inbox.StatusAsync(StatusItems.Unread | StatusItems.UidNext, ct);
        var result = (client.Inbox.Unread, client.Inbox.UidNext?.Id ?? 0);
        await client.DisconnectAsync(true, ct);
        return result;
    }

    /// <summary>Unseen messages at or above a uid — the ones that arrived since last look.</summary>
    public async Task<List<MailSummaryDto>> NewSinceAsync(uint fromUid, int take, CancellationToken ct = default)
    {
        using var client = await OpenAsync(ct);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
        var range = new UniqueIdRange(new UniqueId(Math.Max(1, fromUid)), UniqueId.MaxValue);
        var uids = await inbox.SearchAsync(SearchQuery.NotSeen.And(SearchQuery.Uids(range)), ct);
        var page = uids.OrderByDescending(u => u.Id).Take(take).ToList();
        var items = new List<MailSummaryDto>();
        if (page.Count > 0)
        {
            var summaries = await inbox.FetchAsync(page, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope, ct);
            foreach (var m in summaries.OrderByDescending(m => m.UniqueId.Id))
            {
                var from = m.Envelope?.From?.Mailboxes.FirstOrDefault();
                items.Add(new MailSummaryDto(m.UniqueId.Id, "INBOX", from?.Name is { Length: > 0 } n ? n : from?.Address ?? "", from?.Address ?? "",
                    m.Envelope?.Subject ?? "", (m.Envelope?.Date ?? DateTimeOffset.Now).LocalDateTime, false, false, 0, ""));
            }
        }
        await client.DisconnectAsync(true, ct);
        return items;
    }

    private static string Clip(string s, int max)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }
}
