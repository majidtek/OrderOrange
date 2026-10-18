using System.Globalization;

namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Dates and times, the way THIS shop wants them (Majed 2026-10-02: "also date time
/// localization"): its time zone, its date order, a 12- or 24-hour clock, and month and day
/// names in the reader's language.
///
/// Every timestamp the API sends is the SERVER's wall clock (the API runs on Oman time and
/// stamps with DateTime.Now). The helpers shift such a value into the shop's own time zone
/// before formatting it. Values that are dates rather than moments — a day picked in a date
/// picker, a report period, a calendar cell — must not move, so every helper takes
/// <c>shift: false</c> for those.
///
/// Like <see cref="Symbol"/>, these statics are assigned only by the WebAssembly apps (one
/// browser, one shop). The customer site is Blazor Server and keeps the defaults, which
/// reproduce the formats the apps used before: dd/MM/yyyy, 24-hour, English names, no shift.
/// </summary>
public static partial class Fmt
{
    /// <summary>"dmy" (31/12/2026), "mdy" (12/31/2026) or "ymd" (2026-12-31).</summary>
    public static string DateOrder { get; set; } = "dmy";

    /// <summary>True = 2:05 PM, false = 14:05.</summary>
    public static bool Clock12 { get; set; }

    /// <summary>Minutes to add to a server timestamp to get the shop's wall clock.</summary>
    public static int ShiftMinutes { get; private set; }

    private static int? _serverOffsetMinutes;

    /// <summary>
    /// Translator for month/day names and AM/PM (key → text, or null when there is none).
    /// The partner layout points this at its LanguageService.
    /// </summary>
    public static Func<string, string?>? Names { get; set; }

    /// <summary>Called once the shop is loaded: the server's and the shop's UTC offsets right now.</summary>
    public static void ConfigureClock(int serverOffsetMinutes, int storeOffsetMinutes)
    {
        _serverOffsetMinutes = serverOffsetMinutes;
        ShiftMinutes = storeOffsetMinutes - serverOffsetMinutes;
    }

    /// <summary>
    /// The server's wall clock now — the same clock every API timestamp is on, so comparisons
    /// with them are honest. In WebAssembly <c>DateTime.Now</c> is UTC; use this instead.
    /// </summary>
    public static DateTime ServerNow =>
        _serverOffsetMinutes is { } o ? DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(o), DateTimeKind.Unspecified) : DateTime.Now;

    /// <summary>The server's calendar day now (for "today" filters sent back to the API).</summary>
    public static DateTime ServerToday => ServerNow.Date;

    /// <summary>The shop's own wall clock now.</summary>
    public static DateTime StoreNow => Local(ServerNow);

    /// <summary>A server timestamp moved onto the shop's wall clock.</summary>
    public static DateTime Local(DateTime serverWall) => ShiftMinutes == 0 ? serverWall : serverWall.AddMinutes(ShiftMinutes);

    private static DateTime At(DateTime t, bool shift) => shift ? Local(t) : t;

    private static string Two(int n) => n.ToString("00", CultureInfo.InvariantCulture);

    private static string Name(string key, string fallback) => Names?.Invoke(key) is { Length: > 0 } s ? s : fallback;

    /// <summary>Month name: short "Dec" or long "December", translated.</summary>
    public static string MonthName(int month, bool longName = false) =>
        longName
            ? Name($"cal.month.{month}", CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month))
            : Name($"cal.mon.{month}", CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(month));

    /// <summary>Weekday name: short "Thu" or long "Thursday", translated.</summary>
    public static string WeekdayName(DayOfWeek day, bool longName = true) =>
        longName
            ? Name($"cal.weekday.{(int)day}", CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(day))
            : Name($"cal.wd.{(int)day}", CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedDayName(day));

    // ───────────────────────────── the formats ─────────────────────────────

    /// <summary>31/12/2026 · 12/31/2026 · 2026-12-31</summary>
    public static string Date(DateTime t, bool shift = true)
    {
        var d = At(t, shift);
        return DateOrder switch
        {
            "mdy" => $"{Two(d.Month)}/{Two(d.Day)}/{d.Year}",
            "ymd" => $"{d.Year}-{Two(d.Month)}-{Two(d.Day)}",
            _ => $"{Two(d.Day)}/{Two(d.Month)}/{d.Year}",
        };
    }

    /// <summary>14:05 · 2:05 PM</summary>
    public static string Time(DateTime t, bool shift = true)
    {
        var d = At(t, shift);
        if (!Clock12) return $"{Two(d.Hour)}:{Two(d.Minute)}";
        var h = d.Hour % 12 == 0 ? 12 : d.Hour % 12;
        return $"{h}:{Two(d.Minute)} {(d.Hour < 12 ? Name("cal.am", "AM") : Name("cal.pm", "PM"))}";
    }

    /// <summary>14:05:09 · 2:05:09 PM</summary>
    public static string TimeSeconds(DateTime t, bool shift = true)
    {
        var d = At(t, shift);
        if (!Clock12) return $"{Two(d.Hour)}:{Two(d.Minute)}:{Two(d.Second)}";
        var h = d.Hour % 12 == 0 ? 12 : d.Hour % 12;
        return $"{h}:{Two(d.Minute)}:{Two(d.Second)} {(d.Hour < 12 ? Name("cal.am", "AM") : Name("cal.pm", "PM"))}";
    }

    /// <summary>31/12/2026 14:05</summary>
    public static string DateTimeText(DateTime t, bool shift = true) => $"{Date(t, shift)} {Time(t, shift)}";

    /// <summary>31 Dec · Dec 31</summary>
    public static string DayMonth(DateTime t, bool shift = true)
    {
        var d = At(t, shift);
        return DateOrder == "mdy" ? $"{MonthName(d.Month)} {d.Day}" : $"{d.Day} {MonthName(d.Month)}";
    }

    /// <summary>31 Dec 14:05</summary>
    public static string DayMonthTime(DateTime t, bool shift = true) => $"{DayMonth(t, shift)} {Time(t, shift)}";

    /// <summary>31 Dec 2026 · Dec 31, 2026</summary>
    public static string DayMonthYear(DateTime t, bool shift = true)
    {
        var d = At(t, shift);
        return DateOrder == "mdy" ? $"{MonthName(d.Month)} {d.Day}, {d.Year}" : $"{d.Day} {MonthName(d.Month)} {d.Year}";
    }

    /// <summary>31 Dec 2026 14:05</summary>
    public static string DayMonthYearTime(DateTime t, bool shift = true) => $"{DayMonthYear(t, shift)} {Time(t, shift)}";

    /// <summary>Thursday, 31 Dec (long weekday) · Thu 31 Dec (short)</summary>
    public static string WeekdayDayMonth(DateTime t, bool longWeekday = true, bool shift = true)
    {
        var d = At(t, shift);
        var wd = WeekdayName(d.DayOfWeek, longWeekday);
        return longWeekday ? $"{wd}, {DayMonth(d, false)}" : $"{wd} {DayMonth(d, false)}";
    }

    /// <summary>Thursday · Thu</summary>
    public static string Weekday(DateTime t, bool longName = true, bool shift = true) => WeekdayName(At(t, shift).DayOfWeek, longName);

    /// <summary>December 2026 (long) · Dec 2026 (short)</summary>
    public static string MonthYear(DateTime t, bool longMonth = true, bool shift = true)
    {
        var d = At(t, shift);
        return $"{MonthName(d.Month, longMonth)} {d.Year}";
    }

    // Nullable conveniences: an absent value reads as an empty string.
    public static string Date(DateTime? t, bool shift = true) => t is { } v ? Date(v, shift) : "";
    public static string Time(DateTime? t, bool shift = true) => t is { } v ? Time(v, shift) : "";
    public static string DateTimeText(DateTime? t, bool shift = true) => t is { } v ? DateTimeText(v, shift) : "";
    public static string DayMonth(DateTime? t, bool shift = true) => t is { } v ? DayMonth(v, shift) : "";
    public static string DayMonthTime(DateTime? t, bool shift = true) => t is { } v ? DayMonthTime(v, shift) : "";
    public static string DayMonthYear(DateTime? t, bool shift = true) => t is { } v ? DayMonthYear(v, shift) : "";
    public static string DayMonthYearTime(DateTime? t, bool shift = true) => t is { } v ? DayMonthYearTime(v, shift) : "";
}
