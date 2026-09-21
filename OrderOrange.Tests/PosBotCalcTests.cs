extern alias partnerweb;
using partnerweb::OrderOrange.RestaurantWeb.Services;
using Clock = partnerweb::OrderOrange.RestaurantWeb.Services.PosBotNlu.Clock;

namespace OrderOrange.Tests;

/// <summary>
/// The assistant's arithmetic and its clock.
///
/// Half of these tests are about REFUSING. The bot's main job is taking orders, and an
/// order is mostly bare numbers — "2 pizza table 2". A calculator that grabs those is
/// worse than no calculator at all, so a message only counts as a sum when every single
/// token in it is a number, an operator or a bracket.
/// </summary>
public class PosBotCalcTests
{
    private static double Value(string text)
    {
        var sum = PosBotNlu.TryCalculate(text);
        Assert.True(sum.Ok, $"expected [{text}] to be a sum");
        return sum.Value;
    }

    // ───────────────────────────── plain arithmetic ─────────────────────────────

    [Theory]
    [InlineData("2 + 2", 4)]
    [InlineData("2+2", 4)]
    [InlineData("10 - 3", 7)]
    [InlineData("6 * 7", 42)]
    [InlineData("100 / 4", 25)]
    [InlineData("2 + 3 * 4", 14)]          // precedence, not left to right
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("2 ^ 10", 1024)]
    [InlineData("7.5 + 2.5", 10)]
    [InlineData("-5 + 12", 7)]              // a leading sign is not a subtraction
    [InlineData("2+2=?", 4)]                // trailing punctuation is fine
    public void Sums_are_computed(string text, double expected) =>
        Assert.Equal(expected, Value(text), 4);

    [Theory]
    [InlineData("۲ + ۲", 4)]                // Persian digits
    [InlineData("٣ * ٤", 12)]               // Arabic-Indic digits
    [InlineData("۱۲ × ۳", 36)]              // Persian digits with a multiplication sign
    [InlineData("۱۰٫۵ + ۰٫۵", 11)]          // Arabic decimal separator
    public void Every_digit_script_counts(string text, double expected) =>
        Assert.Equal(expected, Value(text), 4);

    [Theory]
    [InlineData("12 times 3", 36)]
    [InlineData("100 divided by 4", 25)]
    [InlineData("20 plus 5", 25)]
    [InlineData("30 minus 12", 18)]
    [InlineData("۱۲ ضربدر ۳", 36)]
    [InlineData("۲۰ منهای ۵", 15)]
    public void Word_operators_work(string text, double expected) =>
        Assert.Equal(expected, Value(text), 4);

    [Theory]
    [InlineData("15% of 240", 36)]
    [InlineData("20% of 300", 60)]
    [InlineData("٪۱۰ از ۲۰۰", 20)]
    public void Percentages(string text, double expected) =>
        Assert.Equal(expected, Value(text), 4);

    /// <summary>Dividing by zero has no answer — it must not throw and must not claim one.</summary>
    [Fact]
    public void Divide_by_zero_is_declined() =>
        Assert.False(PosBotNlu.TryCalculate("5 / 0").Ok);

    [Theory]
    [InlineData("(2 + 3")]
    [InlineData("2 + 3)")]
    public void Unbalanced_brackets_are_declined(string text) =>
        Assert.False(PosBotNlu.TryCalculate(text).Ok);

    [Fact]
    public void The_working_is_shown_back()
    {
        var sum = PosBotNlu.TryCalculate("۱۲ × ۳");
        Assert.True(sum.Ok);
        Assert.Equal("12 * 3", sum.Expression);
    }

    // ═══════════════════════ when it must NOT do maths ═══════════════════════

    [Theory]
    [InlineData("2 pizza table 2")]
    [InlineData("۲ پیتزا میز ۵")]
    [InlineData("burger for table 12")]
    [InlineData("kebap masa 7")]
    [InlineData("3 coffee and 2 tea")]      // "and" is a plus ONLY between bare numbers
    public void An_order_is_never_a_sum(string text) =>
        Assert.False(PosBotNlu.TryCalculate(text).Ok);

    [Theory]
    [InlineData("5")]
    [InlineData("42")]
    [InlineData("۷")]
    public void A_lone_number_is_not_a_sum(string text) =>
        Assert.False(PosBotNlu.TryCalculate(text).Ok);

    [Theory]
    [InlineData("how many staff do i have")]
    [InlineData("what is running out")]
    [InlineData("close table 3")]
    [InlineData("")]
    public void Ordinary_questions_are_not_sums(string text) =>
        Assert.False(PosBotNlu.TryCalculate(text).Ok);

    // ───────────────────────────── the clock ─────────────────────────────

    [Theory]
    [InlineData("what is the date today")]
    [InlineData("what is the date")]
    [InlineData("ما هو تاريخ اليوم")]
    [InlineData("امروز چندمه")]
    [InlineData("bugünün tarihi ne")]
    public void Date_questions(string text) =>
        Assert.Equal(Clock.Date, PosBotNlu.DetectClock(text));

    [Theory]
    [InlineData("what time is it")]
    [InlineData("كم الساعة")]
    [InlineData("ساعت چنده")]
    [InlineData("saat kac")]
    public void Time_questions(string text) =>
        Assert.Equal(Clock.Time, PosBotNlu.DetectClock(text));

    /// <summary>
    /// "today" on its own is a period filter for a sales question, never a request for
    /// the date — the assistant would otherwise stop being able to say "and today?".
    /// </summary>
    [Theory]
    [InlineData("today")]
    [InlineData("امروز")]
    [InlineData("revenue today")]
    [InlineData("how many staff")]
    [InlineData("2 + 2")]
    public void The_clock_declines_everything_else(string text) =>
        Assert.Equal(Clock.None, PosBotNlu.DetectClock(text));

    // ───────────────────────────── how a date reads ─────────────────────────────

    /// <summary>A Persian reader gets the Jalali date next to the Gregorian one, because
    /// that is the date they would write down.</summary>
    [Fact]
    public void A_persian_date_carries_the_jalali_calendar()
    {
        var text = PosBotNlu.DescribeDate(new DateTime(2026, 8, 22), "fa");
        Assert.Contains("2026", text);
        // 22 Aug 2026 is 31 Mordad 1405 — the Jalali YEAR is the robust thing to assert.
        Assert.Contains("1405", text);
    }

    /// <summary>An Arabic reader gets the Hijri date — the Umm al-Qura one, which is what
    /// is printed on a wall calendar in the Gulf.</summary>
    [Fact]
    public void An_arabic_date_carries_the_hijri_calendar()
    {
        var text = PosBotNlu.DescribeDate(new DateTime(2026, 8, 22), "ar");
        Assert.Contains("2026", text);
        Assert.Contains("هـ", text);
    }

    /// <summary>An English reader gets one calendar, not three.</summary>
    [Fact]
    public void An_english_date_is_just_the_date()
    {
        var text = PosBotNlu.DescribeDate(new DateTime(2026, 8, 22), "en");
        Assert.Contains("2026", text);
        Assert.DoesNotContain("·", text);
    }

    /// <summary>
    /// The Gregorian half must stay Gregorian. Building a Persian culture pulls in the
    /// Jalali calendar as its default, which would silently renumber the year to 1405 —
    /// the two calendars are shown side by side here, never conflated.
    /// </summary>
    [Fact]
    public void The_gregorian_half_is_never_renumbered()
    {
        var text = PosBotNlu.DescribeDate(new DateTime(2026, 8, 22), "fa");
        Assert.Contains("2026", text);
        Assert.DoesNotContain("1405 ·", text);
    }

    [Fact]
    public void The_time_is_the_wall_clock()
    {
        var text = PosBotNlu.DescribeTime(new DateTime(2026, 8, 22, 14, 35, 0), "en");
        Assert.Equal("14:35", text);
    }
}
