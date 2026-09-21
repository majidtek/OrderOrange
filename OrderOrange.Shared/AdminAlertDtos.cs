namespace OrderOrange.Shared;

/// <summary>One thing that happened on the platform and that an administrator should see.</summary>
/// <param name="Kind">"user" for a new customer, "business" for a new shop.</param>
/// <param name="Url">Where the admin app should go when the alert is clicked, if anywhere.</param>
public record AdminAlertDto(
    int Id,
    string Kind,
    string Title,
    string Body,
    string? Url,
    DateTime CreatedAt,
    bool Read);

/// <summary>A page of alerts plus the count this administrator has not opened yet.</summary>
public record AdminAlertsDto(List<AdminAlertDto> Items, int Unread);
