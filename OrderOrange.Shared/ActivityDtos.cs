namespace OrderOrange.Shared;

/// <summary>
/// Sent by each app when someone opens a page — signed in or not.
/// <paramref name="Ip"/> is the VISITOR's address as the app saw it. These are Blazor
/// Server apps, so the ping to the API comes from the web server, not the browser: left
/// to itself the API would log its own machine for every person alive. The API only
/// honours this field from a caller on the same box, which is where the four apps run.
/// </summary>
public record TrackRequest(string App, string Page, string? Ip = null);

/// <summary>
/// One address that has been on the site, rolled up. <paramref name="Who"/> is empty
/// for a visitor who never signed in, which is most of them.
/// </summary>
public record VisitorDto(
    string Ip,
    int Visits,
    int Pages,
    DateTime FirstAt,
    DateTime LastAt,
    string LastPage,
    string LastApp,
    string Who);

/// <summary>One page a single address opened, and how often.</summary>
public record VisitorPageDto(string App, string Page, int Views, DateTime FirstAt, DateTime LastAt);

/// <summary>The visitors board: the page list plus the totals above it.</summary>
public record VisitorsDto(int TotalVisitors, int TotalVisits, int Guests, List<VisitorDto> Rows);

public record ActivityDto(int UserId, string UserName, UserRole Role, string App, string Page, DateTime At,
    string Ip = "");

/// <summary>Sent when a customer searches — guests included, they matter most.</summary>
public record TrackSearchRequest(string Term, int Results, string? App = "Customer", string? Locale = null);

/// <summary>One thing somebody looked for, as it happened.</summary>
public record SearchHitDto(int UserId, string UserName, string Term, int Results,
    string Locale, string Ip, DateTime At);

/// <summary>A term rolled up over the window: how often, how well it lands.</summary>
public record SearchTermDto(string Term, int Count, int ZeroResults, DateTime LastAt);

/// <summary>One page and how much traffic it drew.</summary>
public record PageHitDto(string App, string Page, int Views, int Visitors, DateTime LastAt);

/// <summary>Everything the insights board needs, in one round trip.</summary>
public record InsightsDto(
    int Searches,
    int Searchers,
    int ZeroResultSearches,
    int Visits,
    int Visitors,
    List<SearchTermDto> TopTerms,
    List<SearchTermDto> EmptyTerms,
    List<SearchHitDto> RecentSearches,
    List<PageHitDto> TopPages,
    List<ActivityDto> RecentVisits,
    List<int> ByHour);

/// <summary>One row of the "who's around" board.</summary>
public record UserPresenceDto(
    int UserId,
    string FullName,
    UserRole Role,
    DateTime? LastSeenAt,
    int VisitsToday,
    string? LastPage,
    string? LastApp,
    // The API's own clock. LastSeenAt is server-local time, and a WebAssembly browser has no
    // time-zone data (its DateTime.Now is UTC), so "online" must be judged against this.
    DateTime? ServerNow = null,
    // The last real page view (ActivityLogs) as opposed to LastSeenAt, which any API call
    // refreshes — a tab left open beats for hours; and where that beat comes from.
    DateTime? LastVisitAt = null,
    string? LastIp = null,
    string? BeatApp = null);

// ---------- Reports ----------

public record RestaurantReportRow(
    int Id,
    string Name,
    string LogoEmoji,
    int Orders,
    int Delivered,
    decimal Revenue,
    decimal Commission,
    double Rating);

public record AdminReportDto(
    int Orders,
    int Delivered,
    int Cancelled,
    int Rejected,
    decimal Revenue,
    decimal Commission,
    decimal AvgOrderValue,
    int NewCustomers,
    List<DayStatDto> PerDay,
    List<RestaurantReportRow> PerRestaurant);
