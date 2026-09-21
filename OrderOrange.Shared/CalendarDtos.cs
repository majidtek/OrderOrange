namespace OrderOrange.Shared;

// ---------- The store's shared calendar: meetings, deliveries, deadlines, notes — with reminders ----------

/// <summary>One entry on the store's calendar. Times are the store's local time.</summary>
public record CalendarEventDto(
    int Id,
    string Title,
    string? Details,
    string Color,
    DateTime StartAt,
    DateTime? EndAt,
    bool AllDay,
    // Minutes before StartAt to remind the team; null = no reminder. 0 = at the time.
    int? RemindMinutes,
    DateTime? RemindedAt,
    string CreatedBy,
    DateTime CreatedAt);

public record SaveCalendarEventRequest(
    string Title,
    string? Details,
    string Color,
    DateTime StartAt,
    DateTime? EndAt,
    bool AllDay,
    int? RemindMinutes);

/// <summary>A reminder that is due now (or recently) and has not been acknowledged.</summary>
public record CalendarReminderDto(int EventId, string Title, DateTime StartAt, bool AllDay, string Color, int MinutesLeft);
