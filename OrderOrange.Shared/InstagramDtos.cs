namespace OrderOrange.Shared;

/// <summary>
/// The shop's own Instagram, connected by the owner. OrderOrange never holds a Facebook
/// app on the shop's behalf: the owner brings a long-lived Instagram token, we keep it
/// for that one store and post with it.
/// </summary>
public record InstagramStatusDto(
    bool Connected,
    string Username = "",
    string AccountId = "",
    // When the token dies. Instagram's long-lived tokens last 60 days and are refreshed
    // automatically while the store keeps posting.
    DateTime? ExpiresAt = null,
    int DaysLeft = 0,
    // Instagram allows 50 posts in a rolling 24 hours, per account.
    int QuotaUsed = 0,
    int QuotaTotal = 0,
    string? LastPostUrl = null,
    DateTime? LastPostAt = null,
    // Filled when the last check failed — expired token, account switched to personal…
    string? Problem = null);

/// <summary>The one thing the owner pastes: a long-lived Instagram access token.</summary>
public record ConnectInstagramRequest(string Token);

/// <summary>
/// What to post. Exactly one picture source: a product from the menu, a photo from the
/// store's gallery, or a picture the owner just uploaded (a data URI).
/// </summary>
public record InstagramPostRequest(
    string Caption,
    int? MenuItemId = null,
    int? StorePhotoId = null,
    string? PhotoData = null);

public record InstagramPostResultDto(string MediaId, string Permalink);

/// <summary>A post already on the account, newest first.</summary>
public record InstagramMediaDto(string Id, string Permalink, string? ThumbUrl, string? Caption, DateTime? At);

/// <summary>A post the owner planned for a given moment.</summary>
public record ScheduledPostDto(
    string Id,
    string Caption,
    /// <summary>Store-local time, the way it was picked.</summary>
    DateTime DueAt,
    /// <summary>scheduled · posted · failed · cancelled</summary>
    string Status,
    string? ThumbUrl = null,
    string? Permalink = null,
    string? Error = null,
    DateTime? PostedAt = null);

/// <summary>
/// Plan a post. The picture is the same three ways as an immediate one; the moment is
/// given in the store's own local time, which is where the owner is standing.
/// </summary>
public record SchedulePostRequest(
    string Caption,
    DateTime DueAtLocal,
    int? MenuItemId = null,
    int? StorePhotoId = null,
    string? PhotoData = null);
