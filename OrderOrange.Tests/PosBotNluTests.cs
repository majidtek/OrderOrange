extern alias partnerweb;
using partnerweb::OrderOrange.RestaurantWeb.Services;

namespace OrderOrange.Tests;

/// <summary>
/// The partner order assistant's parser: "2 pizza table 2" in any of the app's
/// languages and digit scripts must come out as items + quantities + the table.
/// </summary>
public class PosBotNluTests
{
    // ───────────────────────────── folding ─────────────────────────────

    [Theory]
    [InlineData("PIZZA", "pizza")]
    [InlineData("پیتزا", "پيتزا")]                     // Persian ی folds to Arabic ي
    [InlineData("۲ پیتزا", "2 پيتزا")]                 // Persian digits
    [InlineData("٣ كباب", "3 كباب")]                   // Arabic-Indic digits
    [InlineData("Café!", "cafe")]                       // accents + punctuation
    [InlineData("میز ۱۲", "ميز 12")]
    public void Fold_normalizes(string input, string expected) =>
        Assert.Equal(expected, PosBotNlu.Fold(input));

    // ───────────────────────────── table extraction ─────────────────────────────

    [Theory]
    [InlineData("2 pizza table 2", "2")]
    [InlineData("2 پیتزا میز 2", "2")]
    [InlineData("۲ پیتزا میز ۵", "5")]
    [InlineData("2 بيتزا طاولة 3", "3")]
    [InlineData("kebap masa 7", "7")]
    [InlineData("две пиццы стол 4", "4")]
    [InlineData("دو عدد چلو کباب برای میز 2", "2")]
    [InlineData("burger for table 12", "12")]
    public void Table_is_found(string text, string expected) =>
        Assert.Equal(expected, PosBotNlu.ExtractTable(text).Table);

    [Theory]
    [InlineData("2 pizza")]
    [InlineData("چلو کباب کوبیده")]
    public void No_table_means_null(string text) =>
        Assert.Null(PosBotNlu.ExtractTable(text).Table);

    [Fact]
    public void Table_word_and_filler_leave_the_dish_query()
    {
        var (table, rest) = PosBotNlu.ExtractTable("دو عدد چلو کباب برای میز 2");
        Assert.Equal("2", table);
        Assert.DoesNotContain("ميز", rest);
        Assert.DoesNotContain("براي", rest);
        Assert.Contains("كباب", rest);
    }

    // ───────────────────────────── quantities ─────────────────────────────

    [Theory]
    [InlineData("2 pizza", 2, "pizza")]
    [InlineData("pizza", 1, "pizza")]
    [InlineData("two burgers", 2, "burgers")]
    [InlineData("دو چلو كباب", 2, "چلو كباب")]
    [InlineData("سه كباب", 3, "كباب")]
    [InlineData("اثنين شاورما", 2, "شاورما")]
    [InlineData("iki kebap", 2, "kebap")]
    public void Quantity_is_read(string segment, int qty, string query)
    {
        var want = PosBotNlu.ExtractQty(PosBotNlu.Fold(segment));
        Assert.Equal(qty, want.Qty);
        Assert.Equal(PosBotNlu.Fold(query), want.Query);
    }

    [Fact]
    public void Counter_words_never_pollute_the_dish()
    {
        // "دو عدد چلو کباب" is two chelo kababs — "عدد" is the counter, not food.
        var want = PosBotNlu.ExtractQty(PosBotNlu.Fold("دو عدد چلو کباب"));
        Assert.Equal(2, want.Qty);
        Assert.Equal(PosBotNlu.Fold("چلو کباب"), want.Query);
    }

    [Fact]
    public void Politeness_is_stripped()
    {
        var want = PosBotNlu.ExtractQty(PosBotNlu.Fold("لطفا 2 پیتزا بده"));
        Assert.Equal(2, want.Qty);
        Assert.Equal(PosBotNlu.Fold("پیتزا"), want.Query);
    }

    // ───────────────────────────── multi-item ─────────────────────────────

    [Fact]
    public void Two_items_split_on_and()
    {
        var command = PosBotNlu.Parse("2 كباب كوبيده و 1 نوشابه ميز 5");
        Assert.Equal("5", command.Table);
        Assert.Equal(2, command.Items.Count);
        Assert.Equal(2, command.Items[0].Qty);
        Assert.Equal(1, command.Items[1].Qty);
    }

    [Fact]
    public void English_multi_item()
    {
        var command = PosBotNlu.Parse("2 pizza and three cola table 9");
        Assert.Equal("9", command.Table);
        Assert.Equal(2, command.Items.Count);
        Assert.Equal(2, command.Items[0].Qty);
        Assert.Equal(3, command.Items[1].Qty);
        Assert.Equal("cola", command.Items[1].Query);
    }

    // ───────────────────────────── whole-utterance parse ─────────────────────────────

    [Theory]
    [InlineData("2 پیتزا میز 2", 1, 2, "2")]
    [InlineData("۲ پیتزا میز ۲", 1, 2, "2")]
    [InlineData("دو عدد چلو کباب برای میز 2", 1, 2, "2")]
    [InlineData("pizza", 1, 1, null)]
    [InlineData("میز 4", 0, 0, "4")]
    public void Parse_end_to_end(string text, int itemCount, int firstQty, string? table)
    {
        var command = PosBotNlu.Parse(text);
        Assert.Equal(itemCount, command.Items.Count);
        Assert.Equal(table, command.Table);
        if (itemCount > 0) Assert.Equal(firstQty, command.Items[0].Qty);
    }

    // ───────────────────────────── confirmations ─────────────────────────────

    [Theory]
    [InlineData("ثبت")]
    [InlineData("تایید")]
    [InlineData("باشه")]
    [InlineData("ok")]
    [InlineData("yes")]
    [InlineData("نعم")]
    [InlineData("تم")]
    [InlineData("evet")]
    public void Confirm_words_confirm(string word) => Assert.True(PosBotNlu.IsConfirm(word));

    [Theory]
    [InlineData("لغو")]
    [InlineData("کنسل")]
    [InlineData("no")]
    [InlineData("cancel")]
    [InlineData("لا")]
    public void Cancel_words_cancel(string word) => Assert.True(PosBotNlu.IsCancel(word));

    [Theory]
    [InlineData("2 پیتزا میز 2")]      // a real order is never read as yes/no
    [InlineData("چلو کباب")]
    public void Orders_are_not_confirmations(string text)
    {
        Assert.False(PosBotNlu.IsConfirm(text));
        Assert.False(PosBotNlu.IsCancel(text));
    }
}

/// <summary>Open/close/floor intents and payment-kind detection.</summary>
public class PosBotIntentTests
{
    [Theory]
    [InlineData("باز کن میز 5")]
    [InlineData("open table 5")]
    [InlineData("افتح طاولة 3")]
    [InlineData("masa 2 aç")]
    public void Open_is_detected(string text) =>
        Assert.Equal(PosBotNlu.Intent.OpenTable, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("ببند میز 5")]
    [InlineData("میز 2 رو حساب کن")]
    [InlineData("close table 4")]
    [InlineData("تسویه میز 3 نقدی")]
    [InlineData("اقفل الطاولة 2")]
    [InlineData("masa 4 kapat")]
    public void Close_is_detected(string text) =>
        Assert.Equal(PosBotNlu.Intent.CloseTable, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("میزها")]
    [InlineData("tables")]
    public void Floor_status_is_detected(string text) =>
        Assert.Equal(PosBotNlu.Intent.Tables, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("2 پیتزا میز 2")]
    [InlineData("دو عدد چلو کباب برای میز 2")]
    [InlineData("2 pizza and a cola")]
    public void Plain_orders_stay_orders(string text) =>
        Assert.Equal(PosBotNlu.Intent.Order, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("ببند میز 2 نقدی", PosBotNlu.PayKind.Cash)]
    [InlineData("close table 2 cash", PosBotNlu.PayKind.Cash)]
    [InlineData("تسویه میز 3 با کارت", PosBotNlu.PayKind.Card)]
    [InlineData("close table 5 card", PosBotNlu.PayKind.Card)]
    public void Pay_kind_is_read(string text, PosBotNlu.PayKind expected) =>
        Assert.Equal(expected, PosBotNlu.DetectPay(text));

    [Fact]
    public void No_pay_word_means_null() =>
        Assert.Null(PosBotNlu.DetectPay("ببند میز 2"));

    [Fact]
    public void Close_command_still_carries_its_table()
    {
        Assert.Equal("2", PosBotNlu.ExtractTable("ببند میز 2 نقدی").Table);
        Assert.Equal("4", PosBotNlu.ExtractTable("close table 4 card").Table);
    }
}

/// <summary>Floor questions: reserved/free/busy tables, revenue, live orders, bills.</summary>
public class PosBotQuestionTests
{
    [Theory]
    [InlineData("کدام میزها رزرو هست؟")]
    [InlineData("کدام میزهارزرو هست ؟")]      // glued, exactly as a rushed owner types it
    [InlineData("which tables are reserved")]
    [InlineData("الطاولات المحجوزة")]
    [InlineData("rezervasyonlar")]
    public void Reserved_questions(string text) =>
        Assert.Equal(PosBotNlu.Intent.Reserved, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("کدام میزها آزاد است؟")]
    [InlineData("which tables are free")]
    [InlineData("میزهای خالی")]
    public void Free_table_questions(string text) =>
        Assert.Equal(PosBotNlu.Intent.FreeTables, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("کدام میزها مشغول است")]
    [InlineData("busy tables")]
    public void Busy_table_questions(string text) =>
        Assert.Equal(PosBotNlu.Intent.BusyTables, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("فروش امروز چقدر بود؟")]
    [InlineData("درآمد امروز")]
    [InlineData("today's sales")]
    [InlineData("مبيعات اليوم")]
    public void Revenue_questions(string text) =>
        Assert.Equal(PosBotNlu.Intent.Revenue, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("چند سفارش داریم؟")]
    [InlineData("how many orders")]
    [InlineData("كم طلب عندنا")]
    public void Live_order_questions(string text) =>
        Assert.Equal(PosBotNlu.Intent.LiveOrders, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("صورتحساب میز 2")]
    [InlineData("میز 2 چقدر شده؟")]
    [InlineData("فاتورة طاولة 3")]
    public void Table_bill_questions(string text) =>
        Assert.Equal(PosBotNlu.Intent.TableBill, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("کمک")]
    [InlineData("help")]
    [InlineData("راهنما")]
    public void Help_requests(string text) =>
        Assert.Equal(PosBotNlu.Intent.Help, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("چی داری؟", true)]
    [InlineData("2 پیتزا میز 2", false)]
    public void Question_detection(string text, bool question) =>
        Assert.Equal(question, PosBotNlu.LooksLikeQuestion(text));

    [Fact]
    public void Orders_still_win_over_floor_words()
    {
        // An order that merely names a table must never become a table command.
        Assert.Equal(PosBotNlu.Intent.Order, PosBotNlu.DetectIntent("2 چلو کباب میز 4"));
        Assert.Equal(PosBotNlu.Intent.Order, PosBotNlu.DetectIntent("2 pizza table 4"));
    }
}

/// <summary>Messy, real-world phrasings — typos, plurals, glue words.</summary>
public class PosBotMessyInputTests
{
    [Theory]
    [InlineData("لیست میز های که رزر هست رو بده")]     // the owner's exact sentence: typo + plural + glue
    [InlineData("میزهای رزرو شده")]
    [InlineData("لیست رزروها")]
    [InlineData("reserved tables list")]
    [InlineData("rezerve masalar")]
    public void Messy_reserved_requests(string text) =>
        Assert.Equal(PosBotNlu.Intent.Reserved, PosBotNlu.DetectIntent(text));

    [Fact]
    public void Plural_marker_is_never_a_table_name()
    {
        Assert.Null(PosBotNlu.ExtractTable("میز های آزاد").Table);
        Assert.Null(PosBotNlu.ExtractTable("لیست میز های که رزر هست").Table);
        Assert.Null(PosBotNlu.ExtractTable("table that is free").Table);
    }

    [Fact]
    public void A_real_table_after_glue_is_still_found() =>
        Assert.Equal("7", PosBotNlu.ExtractTable("میز های شلوغ نه، میز 7 رو بگو").Table);

    [Theory]
    [InlineData("لیست میزها رو بده")]
    [InlineData("show tables")]
    public void Listing_tables_is_the_floor_overview(string text) =>
        Assert.Equal(PosBotNlu.Intent.Tables, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("فروش امروز رو بگو")]
    [InlineData("درامد امروز چقدر بود")]                // درآمد typed without the madda
    public void Messy_revenue_requests(string text) =>
        Assert.Equal(PosBotNlu.Intent.Revenue, PosBotNlu.DetectIntent(text));

    [Fact]
    public void Typos_do_not_break_plain_orders() =>
        Assert.Equal(PosBotNlu.Intent.Order, PosBotNlu.DetectIntent("2 چلو کباب کوبیده میز 3"));
}

/// <summary>بسته/باز as a table STATE inside a question vs the close/open COMMAND.</summary>
public class PosBotStateVsCommandTests
{
    [Theory]
    [InlineData("کدام میزها بسته هستن؟")]               // the owner's exact sentence
    [InlineData("کدوم میزا بسته هست")]
    [InlineData("which tables are closed?")]
    [InlineData("الطاولات المغلقة؟")]
    [InlineData("hangi masalar kapalı?")]
    public void Closed_state_question_lists_free_tables(string text) =>
        Assert.Equal(PosBotNlu.Intent.FreeTables, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("کدام میزها باز هستن؟")]
    [InlineData("which tables are open?")]
    [InlineData("أي الطاولات مفتوحة؟")]
    public void Open_state_question_lists_busy_tables(string text) =>
        Assert.Equal(PosBotNlu.Intent.BusyTables, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("میز 5 رو ببند", PosBotNlu.Intent.CloseTable)]
    [InlineData("ببند میز 5", PosBotNlu.Intent.CloseTable)]
    [InlineData("کدام میز را ببندم؟", PosBotNlu.Intent.CloseTable)]   // conjugated verb keeps it a command
    [InlineData("باز کن میز 4", PosBotNlu.Intent.OpenTable)]
    [InlineData("میز 4 رو باز کن", PosBotNlu.Intent.OpenTable)]
    [InlineData("close table 3", PosBotNlu.Intent.CloseTable)]
    public void Commands_stay_commands(string text, PosBotNlu.Intent expected) =>
        Assert.Equal(expected, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("کدوم میزها خالین؟", PosBotNlu.Intent.FreeTables)]     // suffixed plural of خالی
    [InlineData("میزهای خالی رو نشون بده", PosBotNlu.Intent.FreeTables)]
    [InlineData("کدوم میزها مشغولن", PosBotNlu.Intent.BusyTables)]
    [InlineData("occupied tables?", PosBotNlu.Intent.BusyTables)]
    public void Fuzzy_state_stems_land(string text, PosBotNlu.Intent expected) =>
        Assert.Equal(expected, PosBotNlu.DetectIntent(text));

    [Fact]
    public void State_words_are_never_table_names()
    {
        Assert.Null(PosBotNlu.ExtractTable("کدام میز بسته است").Table);
        Assert.Null(PosBotNlu.ExtractTable("میز باز کن").Table);
    }

    [Fact]
    public void Table_count_question_without_a_table_is_the_overview() =>
        Assert.Equal(PosBotNlu.Intent.Tables, PosBotNlu.DetectIntent("چند میز داریم؟"));

    [Fact]
    public void Bill_question_with_a_table_is_still_the_bill() =>
        Assert.Equal(PosBotNlu.Intent.TableBill, PosBotNlu.DetectIntent("میز 2 چقدر شده؟"));
}

/// <summary>Small talk: the assistant greets back and takes thanks gracefully.</summary>
public class PosBotSmallTalkTests
{
    [Theory]
    [InlineData("سلام")]
    [InlineData("hello")]
    [InlineData("مرحبا")]
    [InlineData("merhaba")]
    [InlineData("hi there")]
    public void Greetings_are_greetings(string text) =>
        Assert.Equal(PosBotNlu.Intent.Greeting, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("ممنون")]
    [InlineData("مرسی")]
    [InlineData("thanks")]
    [InlineData("شكرا")]
    [InlineData("teşekkürler")]
    public void Thanks_are_thanks(string text) =>
        Assert.Equal(PosBotNlu.Intent.Thanks, PosBotNlu.DetectIntent(text));

    [Fact]
    public void A_greeting_with_an_order_is_an_order() =>
        Assert.Equal(PosBotNlu.Intent.Order, PosBotNlu.DetectIntent("سلام 2 پیتزا برای میز 3"));

    [Fact]
    public void Long_sentences_are_never_small_talk() =>
        Assert.NotEqual(PosBotNlu.Intent.Greeting, PosBotNlu.DetectIntent("سلام میخوام بدونم کدوم میزها رزرو هستن"));
}

/// <summary>Round 2: phrasings without question marks, sold-vs-sales, one-table states.</summary>
public class PosBotRound2Tests
{
    [Theory]
    [InlineData("میزهای باز", PosBotNlu.Intent.BusyTables)]        // state list, no question mark
    [InlineData("میزهای بسته", PosBotNlu.Intent.FreeTables)]
    [InlineData("open tables", PosBotNlu.Intent.BusyTables)]
    [InlineData("میزای خالی", PosBotNlu.Intent.FreeTables)]
    [InlineData("کدوم میزا پرن", PosBotNlu.Intent.BusyTables)]
    public void State_lists_without_question_marks(string text, PosBotNlu.Intent expected) =>
        Assert.Equal(expected, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("چقدر فروختیم امروز؟")]
    [InlineData("how much did we sell today")]
    [InlineData("امروز چقدر فروش داشتیم")]
    public void Sold_is_revenue(string text) =>
        Assert.Equal(PosBotNlu.Intent.Revenue, PosBotNlu.DetectIntent(text));

    [Fact]
    public void Is_this_table_open_asks_for_its_bill() =>
        Assert.Equal(PosBotNlu.Intent.TableBill, PosBotNlu.DetectIntent("میز 4 بازه؟"));

    [Fact]
    public void Close_table_without_number_stays_a_command() =>
        Assert.Equal(PosBotNlu.Intent.CloseTable, PosBotNlu.DetectIntent("close table"));

    [Fact]
    public void Shorthand_open_with_a_number_stays_a_command() =>
        Assert.Equal(PosBotNlu.Intent.OpenTable, PosBotNlu.DetectIntent("میز 4 باز"));

    [Fact]
    public void Full_of_cheese_is_not_a_busy_table() =>
        Assert.Equal(PosBotNlu.Intent.Order, PosBotNlu.DetectIntent("2 پیتزا پر پنیر میز 3"));
}

/// <summary>
/// The wide corpus: dozens of ways real owners actually type, across languages.
/// Every sentence here was tried against the parser and pinned down.
/// </summary>
public class PosBotCorpusTests
{
    [Theory]
    // ── reservations ──
    [InlineData("رزروهای امشب رو نشونم بده", PosBotNlu.Intent.Reserved)]
    [InlineData("برای امشب کی رزرو کرده؟", PosBotNlu.Intent.Reserved)]
    [InlineData("any bookings tonight?", PosBotNlu.Intent.Reserved)]
    [InlineData("الحجوزات اليوم", PosBotNlu.Intent.Reserved)]
    [InlineData("bugün rezervasyon var mı", PosBotNlu.Intent.Reserved)]
    // ── revenue ──
    [InlineData("امروز چقدر پول درآوردیم", PosBotNlu.Intent.Revenue)]
    [InlineData("today's income", PosBotNlu.Intent.Revenue)]
    [InlineData("كم دخل اليوم", PosBotNlu.Intent.Revenue)]
    [InlineData("bugün ciro ne kadar", PosBotNlu.Intent.Revenue)]
    // ── live orders ──
    [InlineData("الان چند تا سفارش داریم؟", PosBotNlu.Intent.LiveOrders)]
    [InlineData("live orders", PosBotNlu.Intent.LiveOrders)]
    [InlineData("سفارشات الان", PosBotNlu.Intent.LiveOrders)]
    [InlineData("كم طلب عندنا الآن", PosBotNlu.Intent.LiveOrders)]
    // ── bills ──
    [InlineData("bill for table 2", PosBotNlu.Intent.TableBill)]
    [InlineData("حساب میز 3", PosBotNlu.Intent.TableBill)]
    [InlineData("الحساب طاولة 4", PosBotNlu.Intent.TableBill)]
    [InlineData("میز 5 چقدر شده؟", PosBotNlu.Intent.TableBill)]
    [InlineData("masa 3 hesap", PosBotNlu.Intent.TableBill)]
    // ── settle commands keep their verb ──
    [InlineData("میز 2 رو حساب کن", PosBotNlu.Intent.CloseTable)]
    [InlineData("تسویه میز 6", PosBotNlu.Intent.CloseTable)]
    [InlineData("settle table 6", PosBotNlu.Intent.CloseTable)]
    // ── floor overview ──
    [InlineData("وضعیت سالن", PosBotNlu.Intent.Tables)]
    [InlineData("سالن چطوره؟", PosBotNlu.Intent.Tables)]
    [InlineData("table status", PosBotNlu.Intent.Tables)]
    [InlineData("floor", PosBotNlu.Intent.Tables)]
    // ── free/busy ──
    [InlineData("میز خالی داریم؟", PosBotNlu.Intent.FreeTables)]
    [InlineData("any free tables", PosBotNlu.Intent.FreeTables)]
    [InlineData("which tables are taken", PosBotNlu.Intent.BusyTables)]
    [InlineData("فيه طاولات فاضية؟", PosBotNlu.Intent.FreeTables)]
    // ── greetings both ways ──
    [InlineData("صبح بخیر", PosBotNlu.Intent.Greeting)]
    [InlineData("good morning", PosBotNlu.Intent.Greeting)]
    [InlineData("صباح الخير", PosBotNlu.Intent.Greeting)]
    // ── orders survive everything above ──
    [InlineData("یه پیتزا سفارش بده میز 2", PosBotNlu.Intent.Order)]
    [InlineData("2 برگر و یه نوشابه برای میز 5", PosBotNlu.Intent.Order)]
    [InlineData("سه پرس جوجه میز 7", PosBotNlu.Intent.Order)]
    public void Corpus(string text, PosBotNlu.Intent expected) =>
        Assert.Equal(expected, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("میز دو رو ببند", "2")]              // word-number table
    [InlineData("میز شماره 5 رو باز کن", "5")]        // number label
    [InlineData("table no 8", "8")]
    [InlineData("2 پیتزا برای میز سه", "3")]
    [InlineData("طاولة رقم 12", "12")]
    public void Tables_in_words_and_labels(string text, string expected) =>
        Assert.Equal(expected, PosBotNlu.ExtractTable(text).Table);

    [Fact]
    public void Filler_is_never_a_table_name() =>
        Assert.Null(PosBotNlu.ExtractTable("یه میز برای 4 نفر باز کن").Table);

    [Fact]
    public void Word_number_quantity_still_parses()
    {
        var cmd = PosBotNlu.Parse("یه پیتزا و دوتا کولا میز چهار");
        Assert.Equal("4", cmd.Table);
        Assert.Equal(2, cmd.Items.Count);
        Assert.Equal(1, cmd.Items[0].Qty);
        Assert.Equal(2, cmd.Items[1].Qty);
    }

    [Theory]
    [InlineData("میزهای رزرو نشده", PosBotNlu.Intent.FreeTables)]      // negation flips reserved
    [InlineData("which tables are not reserved", PosBotNlu.Intent.FreeTables)]
    [InlineData("الطاولات غير المحجوزة", PosBotNlu.Intent.FreeTables)]
    [InlineData("میزهای رزرو شده", PosBotNlu.Intent.Reserved)]          // no negation → reserved
    [InlineData("کدوم میزا رزروه؟", PosBotNlu.Intent.Reserved)]
    public void Negation_flips_reserved(string text, PosBotNlu.Intent expected) =>
        Assert.Equal(expected, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("2 تا پیتزا میز شماره دو")]
    [InlineData("hello 2 pizza table 3 please")]
    [InlineData("میز 12 رو نقدی حساب کن")]
    public void Tricky_sentences_do_not_crash(string text) =>
        _ = PosBotNlu.DetectIntent(text);   // pinned: parses without throwing

    [Fact]
    public void Label_plus_word_number_lands()
    {
        var cmd = PosBotNlu.Parse("2 تا پیتزا میز شماره دو");
        Assert.Equal("2", cmd.Table);
        Assert.Single(cmd.Items);
        Assert.Equal(2, cmd.Items[0].Qty);
    }
}

/// <summary>Round 5: glued digits, typo'd table words, menu talk, booking commands.</summary>
public class PosBotRound5Tests
{
    [Theory]
    [InlineData("میز۵ رو ببند", "5")]                 // digit glued to the word
    [InlineData("2پیتزا میز3", "3")]
    [InlineData("close tabel 5", "5")]                 // typo'd "table"
    [InlineData("الطاوله 7", "7")]
    public void Glued_and_typoed_tables_land(string text, string expected) =>
        Assert.Equal(expected, PosBotNlu.ExtractTable(text).Table);

    [Fact]
    public void Vegetable_is_not_a_table() =>
        Assert.Null(PosBotNlu.ExtractTable("vegetable curry 2").Table);

    [Theory]
    [InlineData("منو رو نشون بده")]
    [InlineData("what's on the menu")]
    [InlineData("شو عندكم في المنيو")]
    public void Menu_talk_is_menu(string text) =>
        Assert.Equal(PosBotNlu.Intent.Menu, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("میز شش رو رزرو کن")]
    [InlineData("book table 5")]
    [InlineData("رزرو میز 4 برای ساعت 8")]
    public void Booking_commands_are_not_the_list(string text) =>
        Assert.Equal(PosBotNlu.Intent.MakeReservation, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("کدوم میزها رزرو هستن؟")]
    [InlineData("رزروهای امشب")]
    public void Booking_questions_stay_the_list(string text) =>
        Assert.Equal(PosBotNlu.Intent.Reserved, PosBotNlu.DetectIntent(text));

    [Fact]
    public void Clearing_a_table_closes_it() =>
        Assert.Equal(PosBotNlu.Intent.CloseTable, PosBotNlu.DetectIntent("میز 3 رو خالی کن"));

    [Theory]
    [InlineData("دو تا بستنی برای میز 3")]             // بستنی contains بستن — must stay food
    [InlineData("بستنی میز 2")]
    [InlineData("آب پرتقال میز 2")]
    public void Ice_cream_and_juice_are_food(string text) =>
        Assert.Equal(PosBotNlu.Intent.Order, PosBotNlu.DetectIntent(text));

    [Fact]
    public void Question_junk_leaves_the_item_query()
    {
        var cmd = PosBotNlu.Parse("قهوه داریم؟");
        Assert.Single(cmd.Items);
        Assert.Equal(PosBotNlu.Fold("قهوه"), cmd.Items[0].Query);
    }

    [Fact]
    public void Status_of_one_table_in_turkish_is_its_bill() =>
        Assert.Equal(PosBotNlu.Intent.TableBill, PosBotNlu.DetectIntent("masa 3 kapalı mı"));
}

/// <summary>Round 6: booking details, revenue periods, best sellers, draft editing.</summary>
public class PosBotRound6Tests
{
    [Theory]
    [InlineData("میز 6 رو برای ساعت 8 شب رزرو کن", 20, 0)]     // evening pushes 8 → 20
    [InlineData("book table 5 at 19", 19, 0)]
    [InlineData("رزرو ساعت ۸:۳۰", 8, 30)]                      // colon folds to a space
    [InlineData("احجز الطاولة الساعة 9 مساء", 21, 0)]
    public void Times_are_read(string text, int hour, int minute)
    {
        var time = PosBotNlu.ExtractTime(text);
        Assert.NotNull(time);
        Assert.Equal(hour, time!.Value.Hour);
        Assert.Equal(minute, time.Value.Minute);
    }

    [Fact]
    public void No_time_word_means_no_time() =>
        Assert.Null(PosBotNlu.ExtractTime("میز 6 رو رزرو کن"));

    [Theory]
    [InlineData("میز 6 برای 4 نفر", 4)]
    [InlineData("for 6 people", 6)]
    [InlineData("برای چهار نفر", 4)]                            // word number
    [InlineData("3 kişilik masa", 3)]
    public void Guests_are_read(string text, int guests) =>
        Assert.Equal(guests, PosBotNlu.ExtractGuests(text));

    [Fact]
    public void Booking_sentence_carries_all_three_details()
    {
        Assert.Equal(PosBotNlu.Intent.MakeReservation, PosBotNlu.DetectIntent("میز 6 رو برای ساعت 8 شب رزرو کن برای 4 نفر"));
        Assert.Equal("6", PosBotNlu.ExtractTable("میز 6 رو برای ساعت 8 شب رزرو کن برای 4 نفر").Table);
        Assert.Equal(4, PosBotNlu.ExtractGuests("میز 6 رو برای ساعت 8 شب رزرو کن برای 4 نفر"));
        Assert.Equal((20, 0), PosBotNlu.ExtractTime("میز 6 رو برای ساعت 8 شب رزرو کن برای 4 نفر"));
    }

    [Theory]
    [InlineData("فروش دیروز", PosBotNlu.Period.Yesterday)]
    [InlineData("sales this week", PosBotNlu.Period.Week)]
    [InlineData("درآمد این ماه چقدر بود", PosBotNlu.Period.Month)]
    [InlineData("فروش امروز", PosBotNlu.Period.Today)]
    [InlineData("مبيعات الاسبوع", PosBotNlu.Period.Week)]
    public void Revenue_periods(string text, PosBotNlu.Period expected)
    {
        Assert.Equal(PosBotNlu.Intent.Revenue, PosBotNlu.DetectIntent(text));
        Assert.Equal(expected, PosBotNlu.DetectPeriod(text));
    }

    [Theory]
    [InlineData("پرفروش ترین غذا چیه؟")]
    [InlineData("best sellers")]
    [InlineData("top items this month")]
    [InlineData("الأكثر مبيعا")]
    public void Best_sellers_are_the_leaderboard(string text) =>
        Assert.Equal(PosBotNlu.Intent.TopItems, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("یه نوشابه هم اضافه کن", true)]
    [InlineData("add another cola", true)]
    [InlineData("2 پیتزا میز 3", false)]
    public void Append_wishes_are_seen(string text, bool expected) =>
        Assert.Equal(expected, PosBotNlu.WantsAppend(text));

    [Theory]
    [InlineData("نوشابه رو حذف کن", true)]
    [InlineData("remove the cola", true)]
    [InlineData("2 پیتزا", false)]
    public void Remove_wishes_are_seen(string text, bool expected) =>
        Assert.Equal(expected, PosBotNlu.WantsRemove(text));

    [Fact]
    public void Removal_words_leave_the_item_query()
    {
        var cmd = PosBotNlu.Parse("نوشابه رو حذف کن");
        Assert.Single(cmd.Items);
        Assert.Equal(PosBotNlu.Fold("نوشابه"), cmd.Items[0].Query);
    }
}

/// <summary>Round 7: the owner's «تعداد میزهای باز» screenshot, and its neighbors.</summary>
public class PosBotRound7Tests
{
    [Theory]
    [InlineData("تعداد میزهای باز")]                    // the owner's exact sentence
    [InlineData("تعداد میزها")]
    [InlineData("table count")]
    public void Count_questions_get_the_floor_summary(string text) =>
        Assert.Equal(PosBotNlu.Intent.Tables, PosBotNlu.DetectIntent(text));

    [Fact]
    public void Count_of_free_tables_stays_specific() =>
        Assert.Equal(PosBotNlu.Intent.FreeTables, PosBotNlu.DetectIntent("تعداد میزهای خالی"));

    [Theory]
    [InlineData("تعداد سفارشات امروز")]
    [InlineData("امروز چند سفارش داشتیم؟")]
    [InlineData("كم طلب اليوم")]
    [InlineData("orders this week")]
    public void Orders_of_a_day_are_the_ledger(string text) =>
        Assert.Equal(PosBotNlu.Intent.Revenue, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("سفارشات الان")]
    [InlineData("الان چند تا سفارش داریم؟")]
    [InlineData("live orders")]
    public void Orders_right_now_stay_the_live_board(string text) =>
        Assert.Equal(PosBotNlu.Intent.LiveOrders, PosBotNlu.DetectIntent(text));

    [Fact]
    public void Total_question_reads_the_bill_but_total_command_settles()
    {
        Assert.Equal(PosBotNlu.Intent.TableBill, PosBotNlu.DetectIntent("جمع میز 5 چقدره؟"));
        Assert.Equal(PosBotNlu.Intent.CloseTable, PosBotNlu.DetectIntent("جمع کن میز 5"));
    }

    [Theory]
    [InlineData("وضعیت میز 5", PosBotNlu.Intent.TableBill)]     // status of ONE table = its bill
    [InlineData("table 7 status", PosBotNlu.Intent.TableBill)]
    [InlineData("وضعیت سالن", PosBotNlu.Intent.Tables)]
    public void Status_questions_split_by_scope(string text, PosBotNlu.Intent expected) =>
        Assert.Equal(expected, PosBotNlu.DetectIntent(text));

    [Fact]
    public void Cancelling_a_booking_goes_to_the_book() =>
        Assert.Equal(PosBotNlu.Intent.Reserved, PosBotNlu.DetectIntent("کنسل کن رزرو میز 2"));

    [Theory]
    [InlineData("کدام میز را ببندم؟")]                  // question + do-verb stays a command
    [InlineData("close table 3")]
    public void Close_commands_survive_the_question_guard(string text) =>
        Assert.Equal(PosBotNlu.Intent.CloseTable, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("چند میز باز داریم", PosBotNlu.Intent.BusyTables)]
    [InlineData("how many tables are open", PosBotNlu.Intent.BusyTables)]
    [InlineData("تعداد رزروها", PosBotNlu.Intent.Reserved)]
    [InlineData("سلام فروش امروز چقدره", PosBotNlu.Intent.Revenue)]   // greeting glued to a real question
    [InlineData("باز کن", PosBotNlu.Intent.OpenTable)]
    [InlineData("فروش", PosBotNlu.Intent.Revenue)]
    public void Neighbors_of_the_screenshot(string text, PosBotNlu.Intent expected) =>
        Assert.Equal(expected, PosBotNlu.DetectIntent(text));
}

/// <summary>Round 8: "what can you do?", list-all requests, correction phrasing.</summary>
public class PosBotRound8Tests
{
    [Theory]
    [InlineData("چکار می تونی برام انجام بدی ؟")]      // the owner's exact sentence
    [InlineData("چیکار میتونی بکنی")]
    [InlineData("what can you do for me?")]
    [InlineData("what can you do")]
    [InlineData("شو تقدر تسوي؟")]
    [InlineData("قابلیت هات چیه")]
    public void Ability_questions_reach_help(string text) =>
        Assert.Equal(PosBotNlu.Intent.Help, PosBotNlu.DetectIntent(text));

    [Theory]
    [InlineData("چه غذایی داریم؟")]                     // "چه" about FOOD is not about the bot
    [InlineData("2 پیتزا میز 3")]
    public void Food_talk_is_never_an_ability_question(string text) =>
        Assert.False(PosBotNlu.AsksAboutAbilities(text));

    [Theory]
    [InlineData("لیست کل میزهای باز رو بده", true)]     // the owner's exact sentence
    [InlineData("همه میزها", true)]
    [InlineData("show all tables", true)]
    [InlineData("میزهای باز", false)]
    public void All_requests_are_seen(string text, bool expected) =>
        Assert.Equal(expected, PosBotNlu.WantsAll(text));

    [Theory]
    [InlineData("لیست کل میزهای باز رو بده")]           // the owner's exact sentence
    [InlineData("نه فقط لیست میزهای باز رو بده")]        // …and his correction of it
    public void Listing_open_tables_is_the_busy_list(string text) =>
        Assert.Equal(PosBotNlu.Intent.BusyTables, PosBotNlu.DetectIntent(text));

    [Fact]
    public void A_leading_no_does_not_cancel_a_real_request() =>
        Assert.False(PosBotNlu.IsCancel("نه فقط لیست میزهای باز رو بده"));

    [Theory]
    [InlineData("۵", "5")]                       // Persian keyboard digits
    [InlineData("٣*٥", "3*5")]                   // Arabic-Indic, with the times sign kept
    [InlineData("3*5", "3*5")]
    [InlineData("MAT-۰۰۰۷", "MAT-0007")]
    public void Persian_digits_fold_for_the_code_box(string typed, string expected) =>
        Assert.Equal(expected, PosBotNlu.AsciiDigits(typed));
}
