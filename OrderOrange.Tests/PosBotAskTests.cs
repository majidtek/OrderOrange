extern alias partnerweb;
using partnerweb::OrderOrange.RestaurantWeb.Services;
using Ask = partnerweb::OrderOrange.RestaurantWeb.Services.PosBotNlu.Ask;
using Intent = partnerweb::OrderOrange.RestaurantWeb.Services.PosBotNlu.Intent;

namespace OrderOrange.Tests;

/// <summary>
/// The assistant's second language pass: questions about the BUSINESS — stock, staff,
/// salaries, money owed, reputation, traffic, the shop's own open sign.
///
/// Two things are being proved here. First that each question is recognised, in the
/// languages a shop in Oman actually types. Second — and this is the half that protects
/// the existing 48 tests — that this layer keeps its hands off every sentence the
/// order parser already understands.
/// </summary>
public class PosBotAskTests
{
    private static Ask Kind(string text) => PosBotNlu.DetectAsk(text).Kind;

    // ───────────────────────────── stock ─────────────────────────────

    [Theory]
    [InlineData("how much chicken is left in stock")]
    [InlineData("stock of rice")]
    [InlineData("كم يوجد دجاج في المخزون")]
    [InlineData("موجودی برنج چقدره")]
    [InlineData("انبار گوشت")]
    public void Stock_of_one_thing(string text) =>
        Assert.Equal(Ask.StockLevel, Kind(text));

    [Fact]
    public void Stock_question_keeps_the_material_name()
    {
        var asked = PosBotNlu.DetectAsk("how much chicken is left in stock");
        Assert.Equal(Ask.StockLevel, asked.Kind);
        Assert.Contains("chicken", asked.Subject);
        // The words that classified the sentence must not survive into the subject.
        Assert.DoesNotContain("stock", asked.Subject);
    }

    [Theory]
    [InlineData("what is running out")]
    [InlineData("ما الذي نفد")]
    [InlineData("چه چیزی رو به اتمام است")]
    public void Stock_running_low(string text) =>
        Assert.Equal(Ask.StockLow, Kind(text));

    [Fact]
    public void Bare_stock_word_lists_the_shelf() =>
        Assert.Equal(Ask.StockLow, Kind("inventory"));

    // ───────────────────────────── people ─────────────────────────────

    [Theory]
    [InlineData("how many staff do i have")]
    [InlineData("كم عدد الموظفين")]
    [InlineData("چند تا کارمند دارم")]
    public void Staff_count(string text) =>
        Assert.Equal(Ask.StaffCount, Kind(text));

    [Theory]
    [InlineData("who is online")]
    [InlineData("which staff are online")]
    [InlineData("من متصل الان")]
    [InlineData("کی آنلاین است")]
    public void Who_is_online(string text) =>
        Assert.Equal(Ask.TeamOnline, Kind(text));

    [Theory]
    [InlineData("how much salary did i pay this month")]
    [InlineData("كم دفعت رواتب")]
    [InlineData("حقوق پرداختی این ماه")]
    [InlineData("maas ne kadar")]
    public void Payroll(string text) =>
        Assert.Equal(Ask.Payroll, Kind(text));

    // ───────────────────────────── customers ─────────────────────────────

    [Theory]
    [InlineData("who are my best customers")]
    [InlineData("top customers")]
    [InlineData("افضل الزبائن")]
    [InlineData("بهترین مشتریان")]
    public void Top_customers(string text) =>
        Assert.Equal(Ask.TopCustomers, Kind(text));

    [Theory]
    [InlineData("how many customers do i have")]
    [InlineData("كم عدد الزبائن")]
    [InlineData("چند تا مشتری دارم")]
    public void Customer_count(string text) =>
        Assert.Equal(Ask.CustomerCount, Kind(text));

    // ───────────────────────────── reputation ─────────────────────────────

    [Theory]
    [InlineData("what is my rating")]
    [InlineData("any new reviews")]
    [InlineData("ما هو تقييمي")]
    [InlineData("امتیاز من چنده")]
    [InlineData("نظرات جدید")]
    public void Rating(string text) =>
        Assert.Equal(Ask.Rating, Kind(text));

    // ───────────────────────────── money owed ─────────────────────────────

    [Theory]
    [InlineData("what do i owe")]
    [InlineData("unpaid bills")]
    [InlineData("my expenses this month")]
    [InlineData("الفواتير غير المدفوعة")]
    [InlineData("هزینه های پرداخت نشده")]
    public void Bills_unpaid(string text) =>
        Assert.Equal(Ask.BillsUnpaid, Kind(text));

    // ───────────────────────────── traffic ─────────────────────────────

    [Theory]
    [InlineData("how many visits today")]
    [InlineData("كم زيارة اليوم")]
    [InlineData("بازدید امروز")]
    public void Visits(string text) =>
        Assert.Equal(Ask.Visits, Kind(text));

    // ───────────────────────────── the open sign ─────────────────────────────

    [Theory]
    [InlineData("is the shop open")]
    [InlineData("are we open right now")]
    [InlineData("هل المطعم مفتوح")]
    [InlineData("مغازه بازه")]
    public void Shop_status(string text) =>
        Assert.Equal(Ask.StoreStatus, Kind(text));

    [Theory]
    [InlineData("close the shop")]
    [InlineData("اغلق المطعم")]
    [InlineData("مغازه رو ببند")]
    public void Shop_close(string text) =>
        Assert.Equal(Ask.StoreClose, Kind(text));

    [Theory]
    [InlineData("open the shop")]
    [InlineData("افتح المطعم")]
    [InlineData("فروشگاه رو باز کن")]
    public void Shop_open(string text) =>
        Assert.Equal(Ask.StoreOpen, Kind(text));

    // ───────────────────────────── sales shape ─────────────────────────────

    [Theory]
    [InlineData("what is the average order")]
    [InlineData("متوسط الطلب")]
    [InlineData("میانگین فروش")]
    public void Average_order(string text) =>
        Assert.Equal(Ask.AvgOrder, Kind(text));

    [Theory]
    [InlineData("how many orders were cancelled")]
    [InlineData("كم طلب ملغى")]
    public void Cancelled_orders(string text) =>
        Assert.Equal(Ask.Cancelled, Kind(text));

    [Theory]
    [InlineData("which dishes sell worst")]
    [InlineData("کم فروش ترین غذا")]
    public void Worst_items(string text) =>
        Assert.Equal(Ask.WorstItems, Kind(text));

    // ───────────────────────────── deliveries ─────────────────────────────

    [Theory]
    [InlineData("how many deliveries today")]
    [InlineData("كم توصيل اليوم")]
    [InlineData("پیک های امروز")]
    public void Deliveries(string text) =>
        Assert.Equal(Ask.Deliveries, Kind(text));

    // ───────────────────────────── the whole picture ─────────────────────────────

    [Theory]
    [InlineData("how are we doing")]
    [InlineData("give me a summary")]
    [InlineData("كيف الحال")]
    [InlineData("اوضاع چطوره")]
    [InlineData("durum nasil")]
    public void Briefing(string text) =>
        Assert.Equal(Ask.Briefing, Kind(text));

    // ───────────────────────────── one named thing ─────────────────────────────

    [Fact]
    public void An_order_number_is_looked_up()
    {
        var asked = PosBotNlu.DetectAsk("what happened to order 1042");
        Assert.Equal(Ask.OrderStatus, asked.Kind);
        Assert.Equal("1042", asked.Subject);
    }

    [Fact]
    public void An_order_number_in_persian_digits_is_looked_up()
    {
        var asked = PosBotNlu.DetectAsk("سفارش ۱۰۴۲ چی شد");
        Assert.Equal(Ask.OrderStatus, asked.Kind);
        Assert.Equal("1042", asked.Subject);
    }

    /// <summary>A TABLE number is one or two digits and must never be read as an order.</summary>
    [Theory]
    [InlineData("2 pizza table 2")]
    [InlineData("close table 12")]
    public void A_table_number_is_not_an_order_number(string text) =>
        Assert.NotEqual(Ask.OrderStatus, Kind(text));

    [Fact]
    public void A_named_customer_is_looked_up()
    {
        var asked = PosBotNlu.DetectAsk("how much did leila spend");
        Assert.Equal(Ask.CustomerSpend, asked.Kind);
        Assert.Contains("leila", asked.Subject);
    }

    // ───────────────────────────── reports on anything ─────────────────────────────

    [Theory]
    [InlineData("give me a report")]
    [InlineData("أعطني تقرير")]
    [InlineData("یک گزارش بده")]
    [InlineData("bana rapor ver")]
    public void A_report_with_no_topic(string text) =>
        Assert.Equal(Ask.Report, Kind(text));

    [Fact]
    public void A_report_carries_its_topic()
    {
        var asked = PosBotNlu.DetectAsk("report on products");
        Assert.Equal(Ask.Report, asked.Kind);
        Assert.Contains("product", asked.Subject);
    }

    /// <summary>"report on stock" wants the REPORT, not a shelf reading — the report
    /// branch has to outrank the stock branch.</summary>
    [Fact]
    public void A_report_on_stock_is_a_report_not_a_stock_check()
    {
        var asked = PosBotNlu.DetectAsk("report on stock");
        Assert.Equal(Ask.Report, asked.Kind);
        Assert.True(PosBotNlu.MentionsStock(PosBotNlu.Fold(asked.Subject!)));
    }

    // ───────────────────────────── follow-ups ─────────────────────────────

    [Theory]
    [InlineData("and yesterday")]
    [InlineData("yesterday")]
    [InlineData("this week")]
    [InlineData("و دیروز")]
    [InlineData("امس")]
    public void A_bare_period_repeats_the_last_question(string text) =>
        Assert.Equal(Ask.SamePeriodAgain, Kind(text));

    /// <summary>A period word inside a real question is not a follow-up — there is a
    /// subject in the sentence, so it is a question in its own right.</summary>
    [Theory]
    [InlineData("how many staff yesterday")]
    [InlineData("what is running out today")]
    public void A_period_inside_a_question_is_not_a_follow_up(string text) =>
        Assert.NotEqual(Ask.SamePeriodAgain, Kind(text));

    [Theory]
    [InlineData("more")]
    [InlineData("show more")]
    [InlineData("بیشتر")]
    [InlineData("المزيد")]
    public void More_asks_for_the_rest(string text) =>
        Assert.Equal(Ask.MoreOfTheSame, Kind(text));

    // ═══════════════════════ hands off the order parser ═══════════════════════
    //
    // Everything below already has an owner. If this layer claims any of it, the
    // assistant stops being able to take an order — which is its main job.

    [Theory]
    [InlineData("2 pizza table 2")]
    [InlineData("دو عدد چلو کباب برای میز 2")]
    [InlineData("kebap masa 7")]
    [InlineData("burger for table 12")]
    [InlineData("۲ پیتزا میز ۵")]
    public void Orders_are_never_claimed(string text) =>
        Assert.Equal(Ask.None, Kind(text));

    [Theory]
    [InlineData("open table 5")]
    [InlineData("close table 3")]
    [InlineData("میز 4 رو باز کن")]
    [InlineData("حساب میز 2")]
    [InlineData("تسویه حساب میز 5")]
    public void Table_commands_are_never_claimed(string text) =>
        Assert.Equal(Ask.None, Kind(text));

    [Theory]
    [InlineData("which tables are free")]
    [InlineData("میزهای خالی")]
    [InlineData("reserved tables")]
    [InlineData("how many tables are busy")]
    public void Floor_questions_are_never_claimed(string text) =>
        Assert.Equal(Ask.None, Kind(text));

    [Theory]
    [InlineData("how much did we make today")]
    [InlineData("فروش امروز")]
    [InlineData("revenue this week")]
    public void Revenue_stays_with_the_order_parser(string text) =>
        Assert.Equal(Ask.None, Kind(text));

    [Theory]
    [InlineData("hello")]
    [InlineData("thanks")]
    [InlineData("سلام")]
    [InlineData("what can you do")]
    [InlineData("show me the menu")]
    public void Small_talk_and_menu_are_never_claimed(string text) =>
        Assert.Equal(Ask.None, Kind(text));

    [Fact]
    public void Empty_input_is_nothing() =>
        Assert.Equal(Ask.None, Kind("   "));

    /// <summary>
    /// The two layers are run one after the other, so a sentence claimed here must not
    /// be one the order parser would have answered well. This walks the whole existing
    /// intent vocabulary and asserts the split is clean.
    /// </summary>
    [Theory]
    [InlineData("2 pizza table 2", Intent.Order)]
    [InlineData("which tables are free", Intent.FreeTables)]
    [InlineData("فروش امروز", Intent.Revenue)]
    [InlineData("رزروهای امروز", Intent.Reserved)]
    [InlineData("show me the menu", Intent.Menu)]
    [InlineData("what can you do", Intent.Help)]
    public void The_two_layers_do_not_overlap(string text, Intent expected)
    {
        Assert.Equal(Ask.None, Kind(text));
        Assert.Equal(expected, PosBotNlu.DetectIntent(text));
    }
}
