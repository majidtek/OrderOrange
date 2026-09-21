extern alias clientweb;
using clientweb::OrderOrange.ClientWeb.Services;
using static clientweb::OrderOrange.ClientWeb.Services.BotLexicon;

namespace OrderOrange.Tests;

/// <summary>
/// The chatbot's offline language brain: folding, typo tolerance, intent
/// detection, quantity/order-number extraction, option matching and language
/// identification — across scripts and with deliberate misspellings.
/// </summary>
public class BotNluTests
{
    // ─────────────────────────── Folding ───────────────────────────

    [Theory]
    [InlineData("PIZZA", "pizza")]
    [InlineData("Pizzâ", "pizza")]                       // Latin accents
    [InlineData("café", "cafe")]
    [InlineData("İstanbul", "istanbul")]                 // Turkish dotted capital I
    [InlineData("straße", "strasse")]                    // German sharp s
    [InlineData("ё", "е")]                               // Russian yo
    [InlineData("أهلاً", "اهلا")]                        // hamza forms + tashkeel
    [InlineData("بيتزة", "بيتزه")]                       // taa marbuta
    [InlineData("على", "علي")]                           // alef maqsura
    [InlineData("۲ پیتزا", "2 پيتزا")]                   // Persian digits + letters
    [InlineData("٣ شاورما", "3 شاورما")]                 // Arabic-Indic digits
    [InlineData("५ समोसे", "5 समोसे")]                    // Devanagari digits, vowel signs preserved
    [InlineData("ｐｉｚｚａ", "pizza")]                  // full-width Latin
    [InlineData("ہے", "هي")]                             // Urdu heh + yeh barree
    public void Fold_normalizes_across_scripts(string input, string expected) =>
        Assert.Equal(expected, BotNlu.Fold(input));

    [Theory]
    [InlineData("order", "oredr", 1)]                    // transposition = 1 edit
    [InlineData("pizza", "pizzza", 1)]
    [InlineData("burger", "burgr", 1)]
    public void EditDistance_counts_typos(string a, string b, int expected) =>
        Assert.Equal(expected, BotNlu.EditDistance(a, b, 3));

    // ─────────────────────── Intent: clean input ───────────────────────

    [Theory]
    [InlineData("hello", Intent.Greeting)]
    [InlineData("مرحبا", Intent.Greeting)]
    [InlineData("i want to order pizza", Intent.StartOrder)]
    [InlineData("where is my order", Intent.TrackOrder)]
    [InlineData("cancel my order", Intent.CancelOrder)]
    [InlineData("show my cart", Intent.ShowCart)]
    [InlineData("empty my cart", Intent.ClearCart)]
    [InlineData("checkout", Intent.Checkout)]
    [InlineData("order again", Intent.Reorder)]
    [InlineData("thanks", Intent.Thanks)]
    [InlineData("help", Intent.Help)]
    public void DetectIntent_english(string text, Intent expected) =>
        Assert.Equal(expected, BotNlu.DetectIntent(text).Intent);

    [Theory]
    [InlineData("وين طلبي", Intent.TrackOrder)]                  // Gulf Arabic
    [InlineData("أين طلبي؟", Intent.TrackOrder)]
    [InlineData("ابي اطلب برجر", Intent.StartOrder)]
    [InlineData("الغي طلبي", Intent.CancelOrder)]
    [InlineData("ورينا السلة", Intent.ShowCart)]                  // "شوف سلتي" variant
    [InlineData("سفارشم کجاست", Intent.TrackOrder)]              // Persian
    [InlineData("میخوام سفارش بدم", Intent.StartOrder)]
    [InlineData("میرا آرڈر کہاں ہے", Intent.TrackOrder)]         // Urdu
    [InlineData("मेरा ऑर्डर कहां है", Intent.TrackOrder)]          // Hindi
    [InlineData("mera order kahan hai", Intent.TrackOrder)]      // romanized Hindi
    [InlineData("siparişim nerede", Intent.TrackOrder)]          // Turkish
    [InlineData("où est ma commande", Intent.TrackOrder)]        // French
    [InlineData("je veux commander", Intent.StartOrder)]
    [InlineData("dónde está mi pedido", Intent.TrackOrder)]      // Spanish
    [InlineData("quiero pedir", Intent.StartOrder)]
    [InlineData("wo ist meine bestellung", Intent.TrackOrder)]   // German
    [InlineData("где мой заказ", Intent.TrackOrder)]             // Russian
    [InlineData("отменить заказ", Intent.CancelOrder)]
    [InlineData("dov'è il mio ordine", Intent.TrackOrder)]       // Italian
    [InlineData("cadê meu pedido", Intent.TrackOrder)]           // Portuguese
    [InlineData("我的订单在哪", Intent.TrackOrder)]               // Chinese
    [InlineData("取消订单", Intent.CancelOrder)]
    [InlineData("我要点餐", Intent.StartOrder)]
    [InlineData("注文はどこ", Intent.TrackOrder)]                 // Japanese
    [InlineData("注文をキャンセル", Intent.CancelOrder)]
    public void DetectIntent_multilingual(string text, Intent expected) =>
        Assert.Equal(expected, BotNlu.DetectIntent(text).Intent);

    // ─────────────────── Intent: misspelled input ───────────────────

    [Theory]
    [InlineData("wher is my ordr", Intent.TrackOrder)]           // en typos
    [InlineData("track my ordre", Intent.TrackOrder)]
    [InlineData("cancl my order", Intent.CancelOrder)]
    [InlineData("chekout", Intent.Checkout)]
    [InlineData("i wnat to order", Intent.StartOrder)]
    [InlineData("وين طلبببي", Intent.TrackOrder)]                // ar stretched typo
    [InlineData("تتبع طللبي", Intent.TrackOrder)]
    [InlineData("الغى طلبى", Intent.CancelOrder)]                // alef-maqsura spelling
    [InlineData("siparis takp", Intent.TrackOrder)]              // tr no diacritics + typo
    [InlineData("ou est ma comande", Intent.TrackOrder)]         // fr no accents + typo
    [InlineData("donde esta mi pedidoo", Intent.TrackOrder)]     // es
    [InlineData("wo ist meine bestelung", Intent.TrackOrder)]    // de missing letter
    [InlineData("где мой закз", Intent.TrackOrder)]              // ru missing letter
    [InlineData("quiero pedr", Intent.StartOrder)]               // es typo
    [InlineData("anular pedido", Intent.CancelOrder)]
    public void DetectIntent_survives_misspellings(string text, Intent expected) =>
        Assert.Equal(expected, BotNlu.DetectIntent(text).Intent);

    // ─────────────────────────── Yes / No ───────────────────────────

    [Theory]
    [InlineData("yes", true)]
    [InlineData("yess", true)]
    [InlineData("نعم", true)]
    [InlineData("ايوه", true)]
    [InlineData("بله", true)]
    [InlineData("evet", true)]
    [InlineData("oui", true)]
    [InlineData("да", true)]
    [InlineData("sí", true)]
    [InlineData("はい", true)]
    [InlineData("好的", true)]
    [InlineData("no", false)]
    [InlineData("لا", false)]
    [InlineData("نہیں", false)]
    [InlineData("hayır", false)]
    [InlineData("nein", false)]
    [InlineData("нет", false)]
    [InlineData("não", false)]
    [InlineData("いいえ", false)]
    [InlineData("不要", false)]
    public void DetectYesNo_across_languages(string text, bool expected) =>
        Assert.Equal(expected, BotNlu.DetectYesNo(text));

    [Fact]
    public void DetectYesNo_ignores_long_sentences() =>
        Assert.Null(BotNlu.DetectYesNo("i said i want to order a pizza right now"));

    [Theory]
    [InlineData("ہاں بلکل ٹھیک ہے", true)]              // longer yes answers
    [InlineData("claro que sí dale", true)]
    [InlineData("جی نہیں، رہنے دیں", false)]            // no wins over the yes-word جی
    [InlineData("nein danke", false)]
    [InlineData("nooo assolutamente", false)]
    public void DetectYesNo_handles_longer_answers(string text, bool expected) =>
        Assert.Equal(expected, BotNlu.DetectYesNo(text));

    // ─────────────────── Sentence forms & keyword fallback ───────────────────

    [Theory]
    [InlineData("im starving, get me some food", Intent.StartOrder)]
    [InlineData("how long until my food arrives?", Intent.TrackOrder)]
    [InlineData("has the restaurant accepted my order yet?", Intent.TrackOrder)]
    [InlineData("whats my total so far", Intent.ShowCart)]
    [InlineData("im done, place the order", Intent.Checkout)]
    [InlineData("جوعان بموت ابي بيتزا", Intent.StartOrder)]         // Gulf hungry slang
    [InlineData("گشنمه یه چیزی میخوام", Intent.StartOrder)]         // fa colloquial
    [InlineData("acıktım bir şeyler söyleyelim", Intent.StartOrder)] // tr colloquial
    [InlineData("жрать хочу", Intent.StartOrder)]                    // ru slang
    [InlineData("to com fome", Intent.StartOrder)]                   // pt chat
    [InlineData("我饿了", Intent.StartOrder)]
    [InlineData("お腹すいた", Intent.StartOrder)]
    public void DetectIntent_full_sentences(string text, Intent expected) =>
        Assert.Equal(expected, BotNlu.DetectIntent(text).Intent);

    [Theory]
    [InlineData("remove the pizza from my cart", Intent.RemoveItem)]
    [InlineData("take out the fries please", Intent.RemoveItem)]
    [InlineData("شيل البيبسي من السلة", Intent.RemoveItem)]
    [InlineData("نوشابه رو از سبدم حذف کن", Intent.RemoveItem)]
    [InlineData("quita el sushi del carrito", Intent.RemoveItem)]
    [InlineData("把寿司从购物车里拿掉", Intent.RemoveItem)]
    [InlineData("カートからコーラはずして", Intent.RemoveItem)]
    public void DetectIntent_remove_item(string text, Intent expected) =>
        Assert.Equal(expected, BotNlu.DetectIntent(text).Intent);

    // ─────────────────────────── Quantities ───────────────────────────

    [Theory]
    [InlineData("2 pizzas", 2, "pizzas")]
    [InlineData("pizza x3", 3, "pizza")]
    [InlineData("٣ شاورما", 3, "شاورما")]
    [InlineData("two burgers", 2, "burgers")]
    [InlineData("اثنين برجر", 2, "برجر")]
    [InlineData("iki pizza", 2, "pizza")]
    [InlineData("deux pizzas", 2, "pizzas")]
    [InlineData("три пиццы", 3, "пиццы")]
    public void ExtractQuantity_from_free_text(string text, int qty, string rest)
    {
        var (q, r) = BotNlu.ExtractQuantity(text);
        Assert.Equal(qty, q);
        Assert.Equal(rest, r);
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("٥", 5)]
    [InlineData("do", 2)]                                // hi word allowed as bare answer
    [InlineData("cinco", 5)]
    public void ExtractQuantity_bare_answer(string text, int qty) =>
        Assert.Equal(qty, BotNlu.ExtractQuantity(text, bareAnswer: true).Qty);

    [Fact]
    public void ExtractQuantity_cjk_numeral()
    {
        var (q, _) = BotNlu.ExtractQuantity("两个披萨");
        Assert.Equal(2, q);
    }

    [Fact]
    public void ExtractQuantity_does_not_misread_on_the_way() =>
        Assert.Null(BotNlu.ExtractQuantity("is my order on the way").Qty);

    // ─────────────────────── Order number ───────────────────────

    [Theory]
    [InlineData("track MF-1023", "1023")]
    [InlineData("where is order #1005", "1005")]
    [InlineData("وين الطلب 1010", "1010")]
    [InlineData("no number here", null)]
    public void ExtractOrderNumber_finds_numbers(string text, string? expected) =>
        Assert.Equal(expected, BotNlu.ExtractOrderNumber(text));

    // ─────────────────────── Option matching ───────────────────────

    [Fact]
    public void MatchOption_by_index_and_fuzzy_name()
    {
        var options = new[] { "Margherita Pizza", "Chicken Shawarma", "Beef Burger" };
        Assert.Equal(1, BotNlu.MatchOption("2", options));
        Assert.Equal(0, BotNlu.MatchOption("margarita", options));
        Assert.Equal(1, BotNlu.MatchOption("shawrma", options));
        Assert.Equal(2, BotNlu.MatchOption("beef burgr", options));
        Assert.Equal(-1, BotNlu.MatchOption("sushi", options));
    }

    // ─────────────────────── Language detection ───────────────────────

    [Theory]
    [InlineData("أريد بيتزا", "ar")]
    [InlineData("سفارش من کجاست", "fa")]
    [InlineData("میرا آرڈر کہاں ہے", "ur")]
    [InlineData("मेरा ऑर्डर कहां है", "hi")]
    [InlineData("mujhe order karna hai", "hi")]
    [InlineData("привет где мой заказ", "ru")]
    [InlineData("merhaba siparişim nerede", "tr")]
    [InlineData("je veux commander une pizza", "fr")]
    [InlineData("quiero pedir una pizza", "es")]
    [InlineData("ich möchte bestellen", "de")]
    [InlineData("voglio ordinare una pizza", "it")]
    [InlineData("quero pedir onde está", "pt")]
    [InlineData("我要点餐", "zh")]
    [InlineData("注文したい", "ja")]
    [InlineData("i want to order pizza", "en")]
    public void DetectLocale_pins_language(string text, string expected) =>
        Assert.Equal(expected, BotNlu.DetectLocale(text));

    [Fact]
    public void DetectLocale_stays_null_when_unsure() =>
        Assert.Null(BotNlu.DetectLocale("pizza"));

    // Persian, Arabic and Urdu share a script. Plenty of everyday messages carry no letter
    // that separates them, and answering a Persian customer in Arabic is a real bug the
    // app's own language can prevent.
    [Theory]
    [InlineData("آب")]                       // water — Arabic would be ماء
    [InlineData("سلام")]                     // greeting, spelled identically in Arabic
    [InlineData("حساب")]
    [InlineData("دو تا آب")]
    public void DetectLocale_answers_ambiguous_script_in_the_app_language(string text)
    {
        Assert.Equal("fa", BotNlu.DetectLocale(text, "fa"));
        Assert.Equal("ar", BotNlu.DetectLocale(text, "ar"));
    }

    [Theory]
    [InlineData("سفارش من کجاست", "fa")]
    [InlineData("سفارشم نرسید", "fa")]
    [InlineData("یه پیتزا میخوام", "fa")]
    [InlineData("چقدر طول میکشه", "fa")]
    public void DetectLocale_keeps_persian_even_when_the_app_is_arabic(string text, string expected) =>
        Assert.Equal(expected, BotNlu.DetectLocale(text, "ar"));

    [Theory]
    [InlineData("ابغى برجر", "ar")]
    [InlineData("وين طلبي", "ar")]
    [InlineData("متى يوصل الطلب", "ar")]
    [InlineData("مجھے کھانا چاہیے", "ur")]
    public void DetectLocale_keeps_the_written_language_over_the_app_language(string text, string expected) =>
        Assert.Equal(expected, BotNlu.DetectLocale(text, "fa"));

    // ─────────────────────── Query stripping ───────────────────────

    [Fact]
    public void StripToQuery_removes_intent_and_fillers()
    {
        var intent = BotNlu.DetectIntent("please i want a pizza");
        Assert.Equal(Intent.StartOrder, intent.Intent);
        Assert.Equal("pizza", BotNlu.StripToQuery("please i want a pizza", intent.Phrase));
    }

    [Fact]
    public void StripToQuery_arabic()
    {
        var intent = BotNlu.DetectIntent("ابي اطلب شاورما");
        Assert.Equal(Intent.StartOrder, intent.Intent);
        Assert.Equal("شاورما", BotNlu.StripToQuery("ابي اطلب شاورما", intent.Phrase));
    }
}
