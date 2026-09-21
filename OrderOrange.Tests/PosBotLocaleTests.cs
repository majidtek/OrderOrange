extern alias partnerweb;
using partnerweb::OrderOrange.RestaurantWeb.Services;

namespace OrderOrange.Tests;

/// <summary>
/// Which language the owner just typed in. The assistant answers in THAT language rather
/// than in whatever the portal is set to, so a manager who types Arabic into a Farsi
/// portal is answered in Arabic.
/// </summary>
public class PosBotLocaleTests
{
    [Theory]
    [InlineData("چقدر برنج تو انبار مونده", "fa")]
    [InlineData("کی آنلاین است", "fa")]
    [InlineData("پرفروش‌ترین غذا چیه", "fa")]
    [InlineData("كم عدد الموظفين", "ar")]
    [InlineData("وين الطلب حقي", "ar")]
    [InlineData("هل المطعم مفتوح", "ar")]
    [InlineData("کتنے گاہک ہیں", "ur")]
    [InlineData("میرا آرڈر کہاں ہے", "ur")]
    [InlineData("сколько заказов сегодня", "ru")]
    [InlineData("कितने ग्राहक हैं", "hi")]
    [InlineData("今日の売上はいくら", "ja")]
    [InlineData("今天的销售额", "zh")]
    public void Script_and_words_pin_the_language(string text, string expected) =>
        Assert.Equal(expected, PosBotNlu.DetectLocale(text));

    [Theory]
    [InlineData("how many staff do i have", "en")]
    [InlineData("what is running out today", "en")]
    [InlineData("kac musteri var bugun", "tr")]
    [InlineData("cuantos clientes hay hoy", "es")]
    [InlineData("combien de commandes aujourdhui", "fr")]
    [InlineData("wie viele bestellungen heute", "de")]
    [InlineData("quanti ordini oggi", "it")]
    public void Latin_languages_are_told_apart(string text, string expected) =>
        Assert.Equal(expected, PosBotNlu.DetectLocale(text));

    /// <summary>
    /// The whole point: the portal is in Farsi, the person is writing Arabic. The written
    /// language has to win, or the reply lands in a language they did not use.
    /// </summary>
    [Theory]
    [InlineData("كم عدد الموظفين")]
    [InlineData("هل المطعم مفتوح")]
    [InlineData("وين الطلب")]
    public void Arabic_beats_a_farsi_portal(string text) =>
        Assert.Equal("ar", PosBotNlu.DetectLocale(text, uiLocale: "fa"));

    [Theory]
    [InlineData("چقدر فروش داشتیم")]
    [InlineData("کی آنلاین است")]
    public void Persian_beats_an_arabic_portal(string text) =>
        Assert.Equal("fa", PosBotNlu.DetectLocale(text, uiLocale: "ar"));

    /// <summary>
    /// Text with no signal must not guess. A bare number or a dish name is not evidence
    /// of a language, and a wrong guess is worse than falling back to the portal.
    /// </summary>
    [Theory]
    [InlineData("5")]
    [InlineData("pizza")]
    [InlineData("🍕")]
    [InlineData("")]
    public void No_signal_means_no_guess(string text) =>
        Assert.Null(PosBotNlu.DetectLocale(text));

    /// <summary>
    /// A shared Perso-Arabic word with nothing to break the tie falls back to the portal
    /// rather than picking a side.
    /// </summary>
    [Fact]
    public void An_ambiguous_script_defers_to_the_portal()
    {
        Assert.Equal("fa", PosBotNlu.DetectLocale("حساب", uiLocale: "fa"));
        Assert.Equal("ar", PosBotNlu.DetectLocale("حساب", uiLocale: "ar"));
    }

    /// <summary>Persian letters گ چ پ ژ never come off an Arabic keyboard.</summary>
    [Theory]
    [InlineData("گزارش")]
    [InlineData("پرداخت")]
    [InlineData("چای")]
    public void Persian_only_letters_decide_alone(string text) =>
        Assert.Equal("fa", PosBotNlu.DetectLocale(text));
}
