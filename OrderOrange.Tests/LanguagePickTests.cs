using OrderOrange.ClientCore.Services;

namespace OrderOrange.Tests;

/// <summary>
/// "Switch to Arabic." Both assistants share this resolver, so a customer and a partner
/// change language the same way.
///
/// The refusals matter as much as the matches: a language name is a short, ordinary word,
/// and "french fries" must never switch the app to French.
/// </summary>
public class LanguagePickTests
{
    [Theory]
    [InlineData("switch to arabic", "ar")]
    [InlineData("change language to persian", "fa")]
    [InlineData("set language english", "en")]
    [InlineData("speak turkish", "tr")]
    [InlineData("use french", "fr")]
    public void English_requests(string text, string expected) =>
        Assert.Equal(expected, LanguagePick.Resolve(text));

    [Theory]
    [InlineData("زبان را به فارسی تغییر بده", "fa")]
    [InlineData("به عربی", "ar")]
    [InlineData("زبان انگلیسی", "en")]
    [InlineData("غير اللغة إلى العربية", "ar")]
    [InlineData("اللغة الإنجليزية", "en")]
    [InlineData("dili türkçe yap", "tr")]
    public void Requests_in_other_languages(string text, string expected) =>
        Assert.Equal(expected, LanguagePick.Resolve(text));

    /// <summary>A name on its own is enough — it is the whole message, so it can only be
    /// a request.</summary>
    [Theory]
    [InlineData("arabic", "ar")]
    [InlineData("العربية", "ar")]
    [InlineData("فارسی", "fa")]
    [InlineData("english", "en")]
    [InlineData("türkçe", "tr")]
    [InlineData("中文", "zh")]
    [InlineData("日本語", "ja")]
    public void A_bare_name_is_a_request(string text, string expected) =>
        Assert.Equal(expected, LanguagePick.Resolve(text));

    /// <summary>Every language the app speaks can be asked for by its English name.</summary>
    [Theory]
    [InlineData("english", "en")]
    [InlineData("arabic", "ar")]
    [InlineData("persian", "fa")]
    [InlineData("urdu", "ur")]
    [InlineData("hindi", "hi")]
    [InlineData("turkish", "tr")]
    [InlineData("french", "fr")]
    [InlineData("spanish", "es")]
    [InlineData("german", "de")]
    [InlineData("russian", "ru")]
    [InlineData("italian", "it")]
    [InlineData("portuguese", "pt")]
    [InlineData("chinese", "zh")]
    [InlineData("japanese", "ja")]
    public void All_fourteen_are_reachable(string text, string expected) =>
        Assert.Equal(expected, LanguagePick.Resolve(text));

    // ═══════════════════════ when it must NOT switch ═══════════════════════

    /// <summary>
    /// The dangerous case. A language name buried in a longer sentence about something
    /// else is not a request — it is a word.
    /// </summary>
    [Theory]
    [InlineData("2 french fries table 4")]
    [InlineData("add a turkish coffee to table 3")]
    [InlineData("how many orders did we have today")]
    [InlineData("what is running out")]
    [InlineData("2 + 2")]
    [InlineData("")]
    public void Ordinary_messages_never_switch(string text) =>
        Assert.Null(LanguagePick.Resolve(text));

    [Fact]
    public void A_language_carries_its_own_name() =>
        Assert.False(string.IsNullOrWhiteSpace(LanguagePick.NativeName("ar")));
}
