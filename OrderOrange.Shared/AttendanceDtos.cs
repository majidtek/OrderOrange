namespace OrderOrange.Shared;

// ---------- Staff attendance: "I'm here" and "Leaving", each with a place ----------

/// <summary>Where the phone was when the button was pressed. All null = the browser refused.</summary>
public record ClockRequest(double? Lat, double? Lng, double? Accuracy);

/// <summary>One shift: a clock-in, and the clock-out once it happened.</summary>
public record AttendanceDto(
    int Id,
    int UserId,
    string UserName,
    string? StaffName,
    DateTime InAt,
    DateTime? OutAt,
    double? InLat, double? InLng, double? InAccuracy,
    double? OutLat, double? OutLng, double? OutAccuracy,
    // Hours between in and out; for an open shift, hours so far.
    double Hours,
    // Metres from the store's pin at each press; null when either side has no position.
    double? InDistanceM = null,
    double? OutDistanceM = null);

// ---------- Leave: a few hours off, or whole days ----------

/// <summary>
/// A request for time off. Kind "hourly": From/To are the same day with times.
/// Kind "daily": From/To are dates (inclusive), times ignored.
/// </summary>
public record LeaveRequestDto(string Kind, DateTime From, DateTime To, string? Reason);

/// <summary>The owner's yes or no, with an optional line for the person.</summary>
public record LeaveDecisionRequest(string? Note);

public record LeaveDto(
    int Id,
    int UserId,
    string UserName,
    string? StaffName,
    string Kind,              // hourly | daily
    DateTime From,
    DateTime To,
    double Hours,             // hourly: the span; daily: 0
    int Days,                 // daily: inclusive day count; hourly: 0
    string? Reason,
    string Status,            // pending | approved | rejected
    DateTime RequestedAt,
    DateTime? DecidedAt,
    string? DecidedBy,
    string? DecisionNote);

/// <summary>What the attendance page shows the person pressing the buttons.</summary>
public record MyAttendanceDto(
    AttendanceDto? Open,
    List<AttendanceDto> Today,
    double HoursToday,
    double HoursThisMonth,
    int DaysThisMonth,
    // Their own leave: everything pending, plus this month's decided ones.
    List<LeaveDto> Leaves);

public record AttendancePersonDto(int UserId, string Name, string? StaffName, int Days, double Hours, DateTime? LastIn,
    double LeaveHours = 0, int LeaveDays = 0);

/// <summary>The owner's view over a date range: every shift, the totals per person, and the leave.</summary>
public record AttendanceReportDto(
    DateTime From,
    DateTime To,
    List<AttendancePersonDto> People,
    List<AttendanceDto> Rows,
    int OpenNow,
    // Pending requests (any date) first, then leave decided inside the range.
    List<LeaveDto> Leaves,
    int PendingLeaves,
    // Where the store itself is (Settings → map pin); null = never set, distances stay empty.
    double? StoreLat = null,
    double? StoreLng = null);
