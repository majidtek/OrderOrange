using System.Globalization;

namespace OrderOrange.RestaurantWeb.Services;

/// <summary>
/// "What is today?" and "what time is it?" — the two questions a person asks a thing that
/// is clearly a computer.
///
/// <para>Answering with the Gregorian date alone would be a thin answer in this market. A
/// shop in Muscat works to the Hijri calendar as well as the Gregorian one, and a Persian
/// speaker thinks in Jalali dates — «۱ شهریور» is the date they would write on a note.
/// .NET carries both calendars, so the assistant gives the one the reader actually uses
/// alongside the one the system runs on.</para>
/// </summary>
public static partial class PosBotNlu
{
    /// <summary>Which clock question was asked.</summary>
    public enum Clock { None, Date, Time, Weekday }

    private static class Ticks
    {
        static Ticks() { }

        private static string[] F(params string[] words) => words.Select(Fold).ToArray();

        /// <summary>The noun "date" — a calendar reading, not a period filter.</summary>
        internal static readonly string[] Date = F(
            "تاریخ", "تاريخ", "چندم", "چندمه", "چندمشه", "تقویم",
            "التاريخ", "تقويم", "كم التاريخ",
            "date", "calendar",
            "tarih", "kacinci", "kaçıncı",
            "дата", "число",
            "fecha", "datum", "data",
            "تاریخ", "तारीख", "日期", "日付");

        /// <summary>The noun "time" — the clock, not a stretch of it.</summary>
        internal static readonly string[] Time = F(
            "ساعت", "ساعته", "وقت", "زمان",
            "الساعة", "ساعة", "الوقت", "كم الساعة",
            "time", "clock", "oclock",
            "saat", "zaman",
            "время", "час",
            "hora", "heure", "uhr", "ora", "horas",
            "وقت", "समय", "时间", "几点", "時刻", "時間");

        /// <summary>The noun "day" — asked with a question word it means the weekday.</summary>
        internal static readonly string[] Day = F(
            "روز", "روزه",
            "يوم", "اليوم",
            "day", "weekday",
            "gun", "gün",
            "день",
            "dia", "día", "jour", "tag", "giorno",
            "دن", "दिन", "星期", "曜日");
    }

    /// <summary>
    /// Is this a question about the calendar or the clock? Deliberately strict: "today"
    /// on its own is a period filter for a sales question, never a request for the date,
    /// so a date or time NOUN has to be present.
    /// </summary>
    public static Clock DetectClock(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Clock.None;

        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return Clock.None;

        // A sum that happens to mention hours is still a sum.
        if (TryCalculate(raw).Ok) return Clock.None;

        var hasTime = AnyLike(tokens, Ticks.Time);
        var hasDate = AnyLike(tokens, Ticks.Date);
        var hasDay = AnyLike(tokens, Ticks.Day);

        // "opening hours", «ساعت کاری» — a question about the shop's schedule, which the
        // settings page owns. The clock is only the clock when nothing else claims it.
        if (AnyLike(tokens, Ways.Shop) && hasTime) return Clock.None;

        if (hasTime) return Clock.Time;
        if (hasDate) return Clock.Date;

        // "what day is it" — a day noun needs a question to become a weekday question,
        // otherwise «امروز چند سفارش داشتیم» would answer with a weekday.
        if (hasDay && (LooksLikeQuestion(raw) || AnyLike(tokens, Ways.Who) || AnyLike(tokens, Ways.HowMany)))
            return tokens.Any(TodayWords.Contains) || tokens.Length <= 4 ? Clock.Weekday : Clock.None;

        return Clock.None;
    }

    /// <summary>
    /// The date as the reader would write it: always Gregorian, plus the Jalali date for
    /// a Persian reader and the Hijri one for an Arabic reader. The second calendar is
    /// the point — it is what makes the answer useful rather than merely correct.
    /// </summary>
    public static string DescribeDate(DateTime now, string? locale)
    {
        var culture = CultureFor(locale);
        var gregorian = now.ToString("dddd, d MMMM yyyy", culture);

        var second = locale switch
        {
            "fa" => Jalali(now),
            "ar" or "ur" => Hijri(now),
            _ => null,
        };

        return second is null ? gregorian : $"{gregorian} · {second}";
    }

    /// <summary>The wall clock, in the reader's own conventions (24-hour where that is
    /// the norm, AM/PM where it is not).</summary>
    public static string DescribeTime(DateTime now, string? locale) =>
        now.ToString("HH:mm", CultureFor(locale));

    public static string DescribeWeekday(DateTime now, string? locale) =>
        now.ToString("dddd", CultureFor(locale));

    /// <summary>
    /// A culture for formatting only. Note it is built from the language alone: the
    /// PERSIAN culture would otherwise drag in the Jalali calendar as its default and
    /// silently renumber the Gregorian date, which is exactly the confusion this method
    /// exists to avoid — the two calendars are shown side by side, never conflated.
    /// </summary>
    private static CultureInfo CultureFor(string? locale)
    {
        try
        {
            var culture = new CultureInfo(string.IsNullOrWhiteSpace(locale) ? "en" : locale);
            var clone = (CultureInfo)culture.Clone();
            clone.DateTimeFormat.Calendar = new GregorianCalendar();
            return clone;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static readonly string[] JalaliMonths =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
    ];

    private static string Jalali(DateTime now)
    {
        try
        {
            var calendar = new PersianCalendar();
            var month = calendar.GetMonth(now);
            return $"{calendar.GetDayOfMonth(now)} {JalaliMonths[month - 1]} {calendar.GetYear(now)}";
        }
        catch (ArgumentException) { return ""; }
    }

    private static readonly string[] HijriMonths =
    [
        "محرم", "صفر", "ربيع الأول", "ربيع الآخر", "جمادى الأولى", "جمادى الآخرة",
        "رجب", "شعبان", "رمضان", "شوال", "ذو القعدة", "ذو الحجة",
    ];

    private static string Hijri(DateTime now)
    {
        try
        {
            // Umm al-Qura is the civil calendar of the Gulf; the plain Hijri calendar can
            // sit a day out from the one printed on an Omani wall.
            var calendar = new UmAlQuraCalendar();
            var month = calendar.GetMonth(now);
            return $"{calendar.GetDayOfMonth(now)} {HijriMonths[month - 1]} {calendar.GetYear(now)} هـ";
        }
        catch (ArgumentException) { return ""; }
    }
}
