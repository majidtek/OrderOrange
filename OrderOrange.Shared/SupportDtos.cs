namespace OrderOrange.Shared;

/// <summary>
/// Support tickets: a partner reports a problem, OrderOrange support works it to a fix.
/// Fixed vocabularies live here so both apps and the API agree on the words.
/// </summary>
public static class SupportCatalog
{
    public static readonly string[] Categories = ["technical", "orders", "payments", "menu", "account", "feature", "other"];
    public static readonly string[] Priorities = ["low", "normal", "high", "urgent"];
    /// <summary>open → in_progress ⇄ waiting → resolved → closed (a partner reply reopens).</summary>
    public static readonly string[] Statuses = ["open", "in_progress", "waiting", "resolved", "closed"];

    public const int MaxAttachments = 5;
    /// <summary>Data-URI length cap per attachment (~2 MB of bytes).</summary>
    public const int MaxAttachmentChars = 2_800_000;
    public const int MaxTextChars = 4000;
    public const int MaxSubjectChars = 140;
}

/// <summary>A file hanging off a message — metadata only; bytes come from the files endpoint.</summary>
public record SupportAttachmentDto(string Id, string Name, string Mime, int Size, bool IsImage);

/// <summary>Kind: message | status (a system line, StatusTo says where it went).</summary>
public record SupportMessageDto(
    int Id, string From, string AuthorName, string Text,
    List<SupportAttachmentDto> Attachments, DateTime At, string Kind, string? StatusTo = null);

public record SupportTicketDto(
    string Id, string Number, int RestaurantId, string RestaurantName, string OpenedByName,
    string Subject, string Category, string Priority, string Status, string? AssignedTo,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime LastMessageAt, string LastFrom, string LastPreview,
    bool PartnerUnread, bool SupportUnread, int MessageCount, int? Rating,
    DateTime? ResolvedAt, DateTime? ClosedAt);

public record SupportTicketDetailDto(SupportTicketDto Ticket, List<SupportMessageDto> Messages);

/// <summary>The bytes of one attachment, as a data URI the browser can show or save.</summary>
public record SupportFileDto(string Id, string Name, string Mime, int Size, string Data);

public record NewAttachmentRequest(string Name, string Data);

public record CreateTicketRequest(
    string Subject, string Category, string Priority, string Text,
    List<NewAttachmentRequest>? Attachments = null);

public record TicketReplyRequest(string Text, List<NewAttachmentRequest>? Attachments = null);

public record TicketStatusRequest(string Status, string? Note = null);

public record TicketAssignRequest(string? AssignedTo);

public record TicketRateRequest(int Rating, string? Note = null);

/// <summary>Counts for the header tiles. Unread = tickets with news for the side asking.</summary>
public record SupportSummaryDto(int Open, int InProgress, int Waiting, int Resolved, int Closed, int Unread);

public record SupportPageDto(List<SupportTicketDto> Items, int Total, SupportSummaryDto Summary);
