extern alias clientweb;
using clientweb::OrderOrange.ClientWeb.Services;

namespace OrderOrange.Tests;

/// <summary>
/// "do you have pizza" is how most people ask for a dish, and for a long time the
/// assistant answered "I didn't catch that" — the whole sentence went to the menu search
/// because "do", "you" and "have" are not filler words. These pin the question frames in
/// the languages the app is used in.
/// </summary>
public class BotAskFormTests
{
    [Theory]
    // English, the shape that started this
    [InlineData("do you have pizza", "pizza")]
    [InlineData("Do you have any pizza?", "pizza")]
    [InlineData("do u have burger", "burger")]
    [InlineData("have you got shawarma", "shawarma")]
    [InlineData("do you sell coffee", "coffee")]
    [InlineData("is there any biryani", "biryani")]
    [InlineData("can i get a karak", "karak")]
    [InlineData("i want pizza", "pizza")]
    // the dish alone must survive untouched
    [InlineData("pizza", "pizza")]
    [InlineData("chicken biryani", "chicken biryani")]
    public void Question_frames_come_off_and_the_dish_stays(string typed, string expected) =>
        Assert.Equal(expected, BotNlu.StripToQuery(typed, ""));

    [Theory]
    [InlineData("عندكم برجر", "برجر")]
    [InlineData("هل يوجد شاورما", "شاورما")]
    [InlineData("پیتزا دارید", "پیتزا")]
    [InlineData("برگر دارین", "برگر")]
    public void Question_frames_come_off_in_arabic_and_persian(string typed, string expected) =>
        // Both sides are folded: Fold normalises Persian yeh (ی) to Arabic yeh (ي), so the
        // dish that comes back is the folded spelling, not the one that was typed.
        Assert.Equal(BotNlu.Fold(expected), BotNlu.StripAskForms(BotNlu.Fold(typed)));

    /// <summary>
    /// A sentence that is ONLY a question frame has no dish in it. Returning an empty
    /// string would read as silence to the caller, which then says "I didn't catch that"
    /// instead of asking what the person wants.
    /// </summary>
    [Fact]
    public void A_bare_question_is_never_reduced_to_nothing()
    {
        Assert.False(string.IsNullOrWhiteSpace(BotNlu.StripAskForms(BotNlu.Fold("do you have"))));
        Assert.False(string.IsNullOrWhiteSpace(BotNlu.StripAskForms(BotNlu.Fold("عندكم"))));
    }

    /// <summary>A frame must match whole words: "hay" must not eat the start of "hayashi".</summary>
    [Fact]
    public void A_frame_never_eats_part_of_a_dish_name()
    {
        Assert.Contains("hayashi", BotNlu.StripAskForms(BotNlu.Fold("hayashi")));
        Assert.Contains("temaki", BotNlu.StripAskForms(BotNlu.Fold("temaki")));
    }
}
