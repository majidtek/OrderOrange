namespace OrderOrange.Shared;

// ---------- The admin mailbox (info@orderorange.com read and answered inside the admin panel) ----------

/// <summary>One IMAP folder. Kind is inbox | sent | drafts | trash | junk | other.</summary>
public record MailFolderDto(string Path, string Name, string Kind, int Unread, int Total);

/// <summary>One row of a folder listing — enough to decide whether to open it.</summary>
public record MailSummaryDto(
    uint Uid, string Folder, string From, string FromAddress, string Subject, DateTime Date,
    bool Seen, bool HasAttachments, long Size, string Preview);

public record MailPageDto(List<MailSummaryDto> Items, int Total, int Unread);

public record MailAttachmentDto(int Index, string Name, string ContentType, long Size);

/// <summary>A whole message. Html is the body with inline pictures embedded; Text the plain twin.</summary>
public record MailMessageDto(
    uint Uid, string Folder, string From, string FromAddress, List<string> To, List<string> Cc,
    string Subject, DateTime Date, string? Html, string? Text, List<MailAttachmentDto> Attachments, string? MessageId);

/// <summary>An attachment handed over as base64 so the browser can save it.</summary>
public record MailFileDto(string Name, string ContentType, string Data);

public record MailMarkRequest(string Folder, List<uint> Uids, bool Seen);
public record MailDeleteRequest(string Folder, List<uint> Uids);

/// <summary>
/// An outgoing mail. ReplyFolder + ReplyUid name the message being answered, so the reply
/// threads under it and the original is marked answered.
/// </summary>
public record MailSendRequest(string To, string? Cc, string Subject, string Body, bool Html = false,
    string? ReplyFolder = null, uint ReplyUid = 0);

public record MailStatusDto(bool Configured, string Account, int Unread, DateTime? LastCheckAt, string? LastError, int PollMinutes);
