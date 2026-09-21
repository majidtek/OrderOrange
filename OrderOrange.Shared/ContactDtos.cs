namespace OrderOrange.Shared;

/// <summary>What the public contact form sends: who wrote, and what about.</summary>
public record ContactRequest(string Email, string Phone, string Title, string Text, string? Source = null);

/// <summary>One message as the admin desk lists it.</summary>
public record ContactMessageDto(
    string Id, string Email, string Phone, string Title, string Text, string Source, string Ip,
    DateTime CreatedAt, DateTime? ReadAt);

/// <summary>The counters alone — what the admin drawer badge polls.</summary>
public record ContactSummaryDto(int Unread, int Today, int DailyLimit, DateTime? LatestAt, string? LatestTitle);

/// <summary>A page of contact messages plus the counters the desk shows on top.</summary>
public record ContactPageDto(int Total, int Unread, int Today, int DailyLimit, List<ContactMessageDto> Rows);
