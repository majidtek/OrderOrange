namespace OrderOrange.RestaurantWeb.Services;

/// <summary>
/// The half of the assistant's language brain that answers QUESTIONS ABOUT THE BUSINESS
/// rather than taking an order.
///
/// <para>It is deliberately a second pass, run before <see cref="DetectIntent"/> and
/// falling straight through when it recognises nothing. The order-taking parser is
/// covered by a large test suite and understands the floor extremely well; this layer
/// only claims a sentence when it carries a keyword from a domain that parser has no
/// opinion about — stock, staff, salaries, bills, ratings, traffic, the shop's own open
/// sign. Where the two could overlap (the words "open" and "close", which the order
/// parser uses for a TABLE) the rule is spelled out rather than left to whichever branch
/// happens to run first.</para>
///
/// <para>Every list is multilingual for the same reason the rest of the app is: a shop in
/// Oman is as likely to type Arabic or Farsi as English, and just as likely to mix them
/// in one sentence. Matching goes through the same fold-and-fuzz helpers the order parser
/// uses, so typos, glued suffixes and Persian/Arabic digits all land.</para>
/// </summary>
public static partial class PosBotNlu
{
    /// <summary>A question the assistant can answer from the store's own data.</summary>
    public enum Ask
    {
        /// <summary>Nothing here for this layer — let the order parser have it.</summary>
        None,

        /// <summary>"how much chicken is left" — one material's remaining stock.</summary>
        StockLevel,

        /// <summary>"what is running out" — everything under its minimum.</summary>
        StockLow,

        /// <summary>"how many staff do I have".</summary>
        StaffCount,

        /// <summary>"who is working right now" — presence, not the payroll list.</summary>
        TeamOnline,

        /// <summary>"how much salary did I pay this month".</summary>
        Payroll,

        /// <summary>"who are my best customers".</summary>
        TopCustomers,

        /// <summary>"how many customers do I have".</summary>
        CustomerCount,

        /// <summary>"what is my rating" / "any new reviews".</summary>
        Rating,

        /// <summary>"what do I owe" — unpaid and overdue bills.</summary>
        BillsUnpaid,

        /// <summary>"how many people visited the shop page".</summary>
        Visits,

        /// <summary>"are we open?" — a question about the sign, not a command.</summary>
        StoreStatus,

        /// <summary>"open the shop" — flip the sign on.</summary>
        StoreOpen,

        /// <summary>"close the shop" — flip the sign off.</summary>
        StoreClose,

        /// <summary>"how many deliveries today".</summary>
        Deliveries,

        /// <summary>"what is the average order".</summary>
        AvgOrder,

        /// <summary>"how many orders were cancelled".</summary>
        Cancelled,

        /// <summary>"which dishes sell worst" — the bottom of the leaderboard.</summary>
        WorstItems,

        /// <summary>"take me to the reports page". The page name is the subject.</summary>
        Navigate,

        /// <summary>"how is the shop doing?" — one message with the whole day in it.</summary>
        Briefing,

        /// <summary>"how much has Leila spent?" — one named customer. Name is the subject.</summary>
        CustomerSpend,

        /// <summary>"what happened to order 1042?" — the number is the subject.</summary>
        OrderStatus,

        /// <summary>"and yesterday?" — repeat the last answer over a different stretch of
        /// time. The caller supplies the question; this layer only spots the follow-up.</summary>
        SamePeriodAgain,

        /// <summary>"show me more" — the last list again, untruncated.</summary>
        MoreOfTheSame,

        /// <summary>"give me a report on products" — the subject names which report, and
        /// the answer is a headline figure plus the way to open and export the full one.</summary>
        Report,
    }

    /// <summary>What the question was about, when it names something: a material, a page.</summary>
    public sealed record Asked(Ask Kind, string? Subject = null);

    /// <summary>
    /// The vocabularies, in a nested holder so they are built LAZILY.
    ///
    /// This is not a style choice. Static field initialisers in a partial class run in
    /// textual order within each file, but the order ACROSS files is the compiler's to
    /// pick. These lists all call <see cref="Fold"/>, and Fold reads a vocabulary declared
    /// in the other half of the class — so initialising them eagerly here threw a
    /// TypeInitializationException the moment the other file happened to go second. A
    /// nested type is only initialised when something first touches it, which is always
    /// from inside a method, by which time the outer class is fully built. The explicit
    /// static constructor removes `beforefieldinit` so that promise is exact rather than
    /// merely likely.
    /// </summary>
    private static class Ways
    {
        static Ways() { }

        private static string[] F(params string[] words) => words.Select(Fold).ToArray();

        /// <summary>Raw materials and the shelf they sit on.</summary>
        internal static readonly string[] Stock = F(
            "موجودی", "انبار", "موجود", "مواد", "ماده",
            "مخزون", "المخزون", "مستودع", "خامات",
            "stock", "inventor", "supply", "supplies", "ingredient", "material",
            "stok", "depo", "malzeme",
            "запас", "склад", "остат",
            "almacen", "inventario", "existencias",
            "matiere", "lager", "bestand", "magazzino", "scorte",
            "estoque", "اسٹاک", "स्टॉक", "库存", "在庫");

        /// <summary>Running out, nearly gone, below the minimum.
        /// «خالی» is deliberately absent: an empty TABLE is the commonest use of that
        /// word in this app, and the floor already owns it.</summary>
        internal static readonly string[] Low = F(
            "تموم", "کمبود", "اتمام",
            "نفد", "نافد", "ناقص", "قليل", "بيخلص",
            "empty", "finish", "running", "shortage", "depleted",
            "bitiyor", "tukendi", "tükendi",
            "заканчива", "кончил",
            "agotado", "acaba", "epuise", "épuisé",
            "knapp", "esaurit", "acabando",
            "ختم", "खत्म", "不足", "切れ");

        internal static readonly string[] Staff = F(
            "کارمند", "کارکنان", "پرسنل", "کارگر",
            "موظف", "موظفين", "موظفون", "العاملين", "طاقم",
            "staff", "employee", "worker", "personnel",
            "personel", "calisan", "çalışan", "eleman",
            "сотрудник", "работник", "персонал",
            "empleado", "plantilla", "salarie", "salarié", "mitarbeiter",
            "dipendent", "personale", "funcionario", "funcionário",
            "ملازم", "عملہ", "कर्मचारी", "员工", "従業員");

        internal static readonly string[] Online = F(
            "آنلاین", "انلاین", "برخط", "سرکار", "شیفت",
            "متصل", "متصلين", "اونلاين", "حاضرين",
            "online", "logged", "cevrimici", "çevrimiçi",
            "онлайн", "conecta", "connecte", "connecté",
            "angemeldet", "collegat", "conectad",
            "آن لائن", "ऑनलाइन", "在线", "オンライン");

        internal static readonly string[] Salary = F(
            "حقوق", "دستمزد",
            "راتب", "رواتب", "اجور",
            "salary", "salaries", "wage", "payroll", "paycheck",
            "maas", "maaş", "ucret", "ücret", "bordro",
            "зарплат", "оклад",
            "salario", "sueldo", "nomina", "nómina", "salaire",
            "gehalt", "stipendio", "salário",
            "تنخواہ", "वेतन", "工资", "給与");

        internal static readonly string[] Customer = F(
            "مشتری", "مشتریان", "مشتریها", "خریدار",
            "زبون", "زبائن", "عميل", "عملاء", "الزبائن",
            "customer", "client", "buyer", "patron",
            "musteri", "müşteri",
            "клиент", "покупател",
            "cliente", "clientes", "acheteur", "kunde", "kunden",
            "freguês", "fregues",
            "گاہک", "ग्राहक", "客户", "顧客");

        internal static readonly string[] Rating = F(
            "امتیاز", "امتياز", "ستاره", "نظرات", "بازخورد",
            "تقييم", "تقييمات", "مراجعة", "مراجعات", "نجوم",
            "rating", "review", "feedback",
            "puan", "yorum", "degerlendirme", "değerlendirme",
            "рейтинг", "отзыв",
            "valoracion", "valoración", "resena", "reseña",
            "bewertung", "rezension", "recension", "avaliacao", "avaliação",
            "रेटिंग", "评分", "评价", "評価");

        /// <summary>The shop's OWN costs — rent, power, suppliers. Not a guest's bill,
        /// which the order parser already owns through its bill words plus a table.</summary>
        internal static readonly string[] Expense = F(
            "هزینه", "هزینهها", "مخارج", "بدهی", "قبوض",
            "مصروف", "مصاريف", "فواتير", "ديون", "مستحق",
            "expense", "expenses", "overhead", "utility", "utilities", "payable", "debt", "owe", "owed",
            "gider", "masraf", "borc", "borç",
            "расход", "долг",
            "gasto", "gastos", "deuda",
            "depense", "dépense", "dette",
            "ausgabe", "kosten", "schuld",
            "spesa", "spese", "debito",
            "despesa", "divida", "dívida",
            "اخراجات", "खर्च", "开支", "経費");

        internal static readonly string[] Unpaid = F(
            "پرداختنشده", "نپرداخته", "معوق",
            "غيرمدفوع", "متاخر", "متأخر", "مستحقة",
            "unpaid", "outstanding", "overdue", "owing",
            "odenmemis", "ödenmemiş", "gecikmis", "gecikmiş",
            "неоплач", "просроч",
            "impagado", "vencido", "impaye", "impayé",
            "unbezahlt", "faellig", "fällig",
            "impagato", "scaduto",
            "बकाया", "未付", "未払");

        internal static readonly string[] Visit = F(
            "بازدید", "بازدیدها", "ترافیک", "بیننده",
            "زيارة", "زيارات", "زوار", "مشاهدات",
            "visit", "visitor", "traffic", "impression",
            "ziyaret", "trafik", "goruntuleme", "görüntüleme",
            "посещен", "трафик", "просмотр",
            "visita", "visitante", "trafico", "tráfico",
            "visite", "trafic", "besuch", "aufruf", "zugriff",
            "visitatori", "traffico", "visualizzazioni",
            "acesso", "visitas",
            "विज़िट", "访问", "訪問");

        /// <summary>The business itself — as opposed to a table, an order or a dish. This
        /// is what separates "close the shop" from "close table 4".</summary>
        internal static readonly string[] Shop = F(
            "مغازه", "فروشگاه", "رستوران", "رستورانم", "کافه", "کسب", "شعبه",
            "متجر", "المتجر", "مطعم", "المطعم", "المحل", "الفرع", "كافيه",
            "shop", "store", "restaurant", "business", "branch", "venue",
            "dukkan", "dükkan", "magaza", "mağaza", "restoran", "isletme", "işletme",
            "магазин", "ресторан", "заведен",
            "tienda", "negocio", "restaurante",
            "boutique", "commerce", "etablissement", "établissement",
            "laden", "geschaft", "geschäft",
            "negozio", "ristorante",
            "دکان", "餐厅", "店铺", "店舗");

        internal static readonly string[] Delivery = F(
            "پیک", "دلیوری",
            "توصيل", "التوصيل", "مندوب", "دليفري",
            "delivery", "deliveries", "courier", "dispatch", "shipment",
            "teslimat", "kurye",
            "доставк", "курьер",
            "entrega", "repartidor", "livraison", "livreur",
            "lieferung", "kurier", "consegna", "fattorino",
            "ڈیلیوری", "डिलीवरी", "配送", "配達");

        internal static readonly string[] Average = F(
            "میانگین", "متوسط", "average", "mean",
            "ortalama", "средн", "promedio", "moyenne", "durchschnitt", "media",
            "औसत", "اوسط", "平均");

        internal static readonly string[] Cancelled = F(
            "لغوشده", "کنسل", "ملغى", "الغاء", "إلغاء",
            "cancel", "cancelled", "canceled", "rejected",
            "iptal", "отмен", "cancelad", "annule", "annulé", "storniert", "annullat",
            "منسوخ", "रद्द", "取消", "キャンセル");

        /// <summary>The bottom of the leaderboard: "worst", "slowest", "least sold".</summary>
        internal static readonly string[] Worst = F(
            "کمفروش", "بدفروش", "کمترین", "راکد",
            "الاقل", "الأقل",
            "worst", "slowest", "least", "unpopular",
            "enaz", "kotu", "kötü",
            "худш", "наимень",
            "peor", "pire", "schlechtest", "wenigst",
            "peggior", "pior",
            "最差", "最少");

        /// <summary>"take me to", "go to", "show me the … page".</summary>
        internal static readonly string[] Go = F(
            "برو", "ببر", "صفحه", "برگه",
            "اذهب", "صفحة",
            "goto", "navigate", "page", "screen",
            "sayfa",
            "перейд", "страниц",
            "pagina", "página",
            "seite", "offne", "öffne",
            "صفحہ", "पेज", "页面", "ページ");

        /// <summary>Words that make a sentence a "how many / how much" question.</summary>
        internal static readonly string[] HowMany = F(
            "چند", "چقدر", "تعداد", "مقدار",
            "عدد", "كمية",
            "many", "much", "count", "number",
            "kac", "kaç", "sayi", "sayı", "toplam",
            "сколько", "количеств",
            "cuant", "cuánt", "combien", "wieviel", "quant",
            "kitne", "कितने", "多少");

        /// <summary>An order, as a noun. Pairs with "average" and "cancelled" to make
        /// them sales questions rather than stray adjectives.</summary>
        internal static readonly string[] OrderNoun = F(
            "طلب", "طلبات", "الطلب", "الطلبات",
            "سفارش", "سفارشات", "سفارشها",
            "order", "orders", "siparis", "sipariş", "заказ",
            "pedido", "commande", "bestellung", "ordine",
            "آرڈر", "ऑर्डर", "订单", "注文");

        /// <summary>A leading interrogative: the difference between "is the shop open?"
        /// and "open the shop". Imperatives do not start with these.</summary>
        internal static readonly string[] Asking = F(
            "is", "are", "was", "were", "do", "does", "did", "can", "will", "should",
            "هل", "أهل", "ايش", "شو", "وش",
            "آیا", "ايا",
            "mi", "mu", "ли", "es", "est", "ist", "e");

        /// <summary>"how are we doing" — a request for the whole picture at once.</summary>
        internal static readonly string[] Briefing = F(
            "خلاصه", "گزارش روز", "اوضاع", "چهخبر", "وضعیت",
            "ملخص", "الوضع", "الحال", "الاحوال",
            "briefing", "summary", "overview", "situation", "recap", "snapshot",
            "ozet", "özet", "durum",
            "сводка", "обзор", "ситуац",
            "resumen", "resume", "résumé", "apercu", "aperçu",
            "ubersicht", "übersicht", "zusammenfassung",
            "riepilogo", "panoramica", "resumo",
            "خلاصہ", "सारांश", "概况", "概要");

        /// <summary>Determiners and conjunctions that ride along with a bare period:
        /// "and yesterday", "this week", «و دیروز». They carry no subject of their own,
        /// so they must not stop a follow-up from being recognised as one.</summary>
        internal static readonly string[] FollowGlue = F(
            "this", "that", "last", "past", "previous", "and", "about", "what", "how",
            "و", "این", "اون", "همین", "گذشته", "قبل", "قبلی",
            "هذا", "هذه", "الماضي", "الماضية", "الفائت",
            "gecen", "geçen", "bu", "onceki", "önceki",
            "прошл", "эта", "этой",
            "esta", "este", "pasada", "pasado", "cette", "dernier", "derniere", "dernière",
            "diese", "letzte", "questa", "scorsa", "esta", "passada");

        /// <summary>Money leaving a customer's pocket: "spent", «خرج», «أنفق».</summary>
        internal static readonly string[] Spend = F(
            "خرج", "خرید", "پرداخت", "هزینه", "داده",
            "انفق", "أنفق", "صرف", "دفع", "اشترى",
            "spend", "spent", "paid", "bought", "purchase",
            "harca", "odedi", "ödedi",
            "потрат", "истрат",
            "gasto", "gastado", "depense", "dépensé", "ausgegeben", "speso", "gastou",
            "خرچ", "खर्च", "花了", "使った");

        /// <summary>What the shop sells, as a business noun rather than as food. This is
        /// the word people reach for when asking for a REPORT — "products", «محصولات»,
        /// «المنتجات» — none of which appear in the order parser's food vocabulary.</summary>
        internal static readonly string[] Product = F(
            "محصول", "محصولات", "کالا", "کالاها", "اقلام", "جنس",
            "منتج", "منتجات", "المنتجات", "اصناف", "الاصناف", "سلع",
            "product", "products", "item", "items", "dish", "dishes", "sku",
            "urun", "ürün", "urunler", "ürünler",
            "товар", "продукт", "блюд",
            "producto", "productos", "articulo", "artículo",
            "produit", "produits", "artikel", "produkt", "prodotto", "prodotti",
            "produto", "produtos",
            "مصنوعات", "उत्पाद", "产品", "商品", "菜品", "製品");

        /// <summary>"report", "statement", "print me the numbers" — a request for the
        /// FULL thing rather than a figure in a chat bubble.</summary>
        internal static readonly string[] ReportWord = F(
            "گزارش", "گزارشات", "صورت", "صورتوضعیت", "امار", "آمار",
            "تقرير", "تقارير", "كشف", "بيان", "احصائيات", "إحصائيات",
            "report", "statement", "statistics", "stats", "analytics", "breakdown",
            "rapor", "istatistik",
            "отчет", "отчёт", "статистик",
            "informe", "reporte", "estadistica", "estadística",
            "rapport", "statistique", "bericht", "auswertung", "statistik",
            "rapporto", "statistiche", "relatorio", "relatório",
            "رپورٹ", "रिपोर्ट", "报表", "报告", "レポート", "統計");

        /// <summary>"how is it going" — the verb half of the briefing question.</summary>
        internal static readonly string[] Doing = F(
            "doing", "going", "looking", "standing",
            "الحال", "الاحوال", "الوضع", "الامور",
            "چطوره", "چطور", "اوضاع",
            "gidiyor", "nasil", "nasıl",
            "дела", "идут",
            "va", "vamos", "geht", "andiamo", "vai");

        /// <summary>"more", "show the rest" — the last list again, in full.</summary>
        internal static readonly string[] More = F(
            "بیشتر", "بقیه", "ادامه", "بازم",
            "المزيد", "الباقي", "كمان", "زياده",
            "more", "rest", "others", "continue", "expand",
            "daha", "devam", "digerleri", "diğerleri",
            "ещё", "еще", "остальн",
            "mas", "más", "resto", "plus", "reste", "mehr", "weitere",
            "altri", "ancora", "mais", "restante",
            "مزید", "और", "更多", "もっと");

        /// <summary>"who" — the question that turns a staff word into a presence question.</summary>
        internal static readonly string[] Who = F(
            "چهکسی", "کدوم",
            "who", "kim", "кто", "quien", "quién", "wer", "chi", "quem",
            "کون", "कौन");
    }

    // ─────────────────────────── the dispatcher ───────────────────────────

    /// <summary>
    /// Reads a sentence for one of the business questions above. Returns
    /// <see cref="Ask.None"/> — the common case — for anything this layer has no business
    /// claiming, which leaves the order parser free to do its job.
    /// </summary>
    public static Asked DetectAsk(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new Asked(Ask.None);

        var folded = Fold(raw);
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return new Asked(Ask.None);

        var meaty = tokens.Where(t => !GrammarJunk.Contains(t) && !PolitenessFillers.Contains(t)).ToArray();
        var asksWho = AnyLike(tokens, Ways.Who);
        var asksHowMany = AnyLike(tokens, Ways.HowMany);

        // ---- follow-ups ----
        // These have to come first: they are defined by what the sentence LACKS, so any
        // later branch that matched a stray word would swallow them.
        //
        // "and yesterday?" is only a follow-up when nothing else is left in it. A message
        // whose every meaningful word is a period word cannot be a new question — there
        // is no subject in it to ask about. Determiners and "and" ride along and are
        // dropped first, or "this week" would never qualify.
        var bare = meaty.Where(t => !Ways.FollowGlue.Any(g => t == g)).ToArray();
        if (bare.Length > 0 && bare.All(IsPeriodWord)) return new Asked(Ask.SamePeriodAgain);

        // "more" / «بیشتر», alone. With anything else it is an ordinary word.
        if (bare.Length is > 0 and <= 2 && bare.Any(t => Ways.More.Any(m => TokenLike(t, m))))
            return new Asked(Ask.MoreOfTheSame);

        // ---- a report on something ----
        // Before the individual domains: "report on stock" wants the REPORT, not the
        // shelf reading. The subject is whatever the sentence names; an empty subject
        // means "a report" with no topic, which the caller answers with the menu of them.
        if (AnyLike(tokens, Ways.ReportWord))
        {
            var topic = Strip(tokens, Ways.ReportWord, Ways.HowMany, Ways.Who,
                              Ways.Go, Ways.FollowGlue, Ways.Asking);
            return new Asked(Ask.Report, topic);
        }

        // ---- the whole picture ----
        // Either a summary noun, or the "how is it going" shape: a question word next to
        // a state verb, with no other subject in the sentence to be about.
        if (AnyLike(tokens, Ways.Briefing)
            || ((asksHowMany || AnyLike(tokens, Ways.Who) || tokens.Contains(Fold("how")))
                && AnyLike(tokens, Ways.Doing)))
            return new Asked(Ask.Briefing);

        // ---- one order, by number ----
        // An order word plus a long number is unambiguous; a table number is 1–2 digits
        // and already excluded by the table guard below.
        if (AnyLike(tokens, Ways.OrderNoun))
        {
            var number = tokens.FirstOrDefault(t => t.Length >= 3 && t.All(char.IsAsciiDigit));
            if (number is not null) return new Asked(Ask.OrderStatus, number);
        }

        // A table in the sentence means the floor, and the floor belongs to the order
        // parser — "how much is table 4" is a bill, not an inventory question.
        var aboutATable = ExtractsRealTable(raw);

        // Even without a number, a table WORD hands the sentence back: «میزهای خالی»
        // is the free-tables list, not an empty shelf.
        var mentionsTables = tokens.Any(IsTableWord) || tokens.Any(TablesListWords.Contains)
                             || AnyLike(tokens, TablesListStems)
                             || folded.Contains("ميز") || folded.Contains("طاول");

        // ---- the shop's own open sign ----
        // First, because "open" and "close" are the order parser's verbs for a TABLE. A
        // shop word is what tells the two apart; without one this block only fires for a
        // plainly interrogative "are we open?".
        var opening = AnyLike(tokens, OpenStems);
        var closing = AnyLike(tokens, CloseStems) || AnyHas(meaty, ClosedStateStems);
        var stateful = AnyHas(meaty, OpenStateStems) || AnyHas(meaty, ClosedStateStems);
        // An imperative never begins with "is"/"are"/«هل»/«آیا». That single signal is
        // what separates "is the shop open?" from "open the shop".
        var leadsInterrogative = tokens.Length > 0 && Ways.Asking.Any(a => tokens[0] == a);
        var shopish = AnyLike(tokens, Ways.Shop) || (leadsInterrogative && stateful);

        if (shopish && !aboutATable && !mentionsTables && (opening || closing || stateful))
        {
            if (leadsInterrogative || LooksLikeQuestion(raw)) return new Asked(Ask.StoreStatus);
            if (closing) return new Asked(Ask.StoreClose);
            if (opening) return new Asked(Ask.StoreOpen);
            return new Asked(Ask.StoreStatus);
        }

        // ---- stock ----
        var stockish = AnyLike(tokens, Ways.Stock);
        if (!mentionsTables && (stockish || AnyLike(tokens, Ways.Low)))
        {
            var subject = Strip(tokens, Ways.Stock, Ways.Low, Ways.HowMany, Ways.Who, Ways.Go);
            // "how much rice is left" names one thing; "what is running out" wants the list.
            if (stockish && subject.Length > 0) return new Asked(Ask.StockLevel, subject);
            return new Asked(Ask.StockLow);
        }

        // ---- people ----
        // «متصل», «آنلاین», "logged in" mean one thing in this app and appear in no other
        // domain, so they stand on their own — no staff word or "who" required.
        if (AnyLike(tokens, Ways.Online)) return new Asked(Ask.TeamOnline);

        if (AnyLike(tokens, Ways.Salary)) return new Asked(Ask.Payroll);

        if (AnyLike(tokens, Ways.Staff))
            return new Asked(asksWho && !asksHowMany ? Ask.TeamOnline : Ask.StaffCount);

        // ---- customers ----
        if (AnyLike(tokens, Ways.Customer))
        {
            if (asksHowMany && !tokens.Any(MostWords.Contains)) return new Asked(Ask.CustomerCount);
            return new Asked(Ask.TopCustomers);
        }

        // A person's name with a spend word and no customer noun: "how much has Leila
        // spent". The name is whatever is left; the caller looks it up and says so
        // plainly when there is no such customer.
        if (asksHowMany && AnyLike(tokens, Ways.Spend) && !mentionsTables)
        {
            var who = Strip(tokens, Ways.HowMany, Ways.Who, Ways.Spend, RevenueStems, Ways.OrderNoun, Ways.FollowGlue, Ways.Asking);
            // One or two words is a name; more than that is a sentence about something else.
            if (who.Length > 0 && who.Split(' ').Length <= 2 && !who.Any(char.IsAsciiDigit))
                return new Asked(Ask.CustomerSpend, who);
        }

        // ---- reputation ----
        if (AnyLike(tokens, Ways.Rating)) return new Asked(Ask.Rating);

        // ---- money owed ----
        if (!aboutATable && (AnyLike(tokens, Ways.Expense) || AnyLike(tokens, Ways.Unpaid)))
            return new Asked(Ask.BillsUnpaid);

        // ---- traffic ----
        if (AnyLike(tokens, Ways.Visit)) return new Asked(Ask.Visits);

        // ---- deliveries ----
        if (!aboutATable && AnyLike(tokens, Ways.Delivery)) return new Asked(Ask.Deliveries);

        // ---- the shape of the day's sales ----
        // After the domains above, so "average delivery time" is not an average-order
        // question. Each needs a money or order word to qualify.
        var salesish = AnyLike(tokens, RevenueStems) || tokens.Any(MoneyWords.Contains)
                       || tokens.Any(OrderCountWords.Contains) || AnyLike(tokens, Ways.OrderNoun);

        if (salesish && AnyLike(tokens, Ways.Cancelled)) return new Asked(Ask.Cancelled);
        if (salesish && AnyLike(tokens, Ways.Average)) return new Asked(Ask.AvgOrder);

        // "which dish sells worst" — the mirror of TopItems, which the order parser owns.
        // «کم فروش» arrives as two tokens, so a bare "how much/few" next to a sales word
        // counts as well as the glued «کم‌فروش».
        var lowSelling = AnyLike(tokens, Ways.Worst)
                         || (tokens.Any(t => t == Fold("کم")) && AnyLike(tokens, RevenueStems));
        if (lowSelling && (salesish || AnyLike(tokens, MenuStems)))
            return new Asked(Ask.WorstItems);

        // ---- navigation ----
        // Last, and only when a page is actually named: "open" alone is a table verb.
        if (!aboutATable && AnyLike(tokens, Ways.Go))
        {
            var target = Strip(tokens, Ways.Go, Ways.HowMany, Ways.Who);
            if (target.Length > 0) return new Asked(Ask.Navigate, target);
        }

        return new Asked(Ask.None);
    }

    // Topic tests the caller uses to route "a report about X" when the words the owner
    // chose do not match any report's TITLE — "stock" against "Raw material usage".

    /// <summary>Does it talk about what the kitchen sells? The order parser's menu
    /// vocabulary covers «غذا» and «قائمة», but not the word a report is asked for by —
    /// nobody says "a report on the menu", they say "a report on products".</summary>
    public static bool MentionsProducts(string folded)
    {
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return AnyLike(tokens, MenuStems) || tokens.Any(MenuWords.Contains)
            || AnyLike(tokens, Ways.Product);
    }

    /// <summary>Does it talk about the people who buy?</summary>
    public static bool MentionsCustomers(string folded) =>
        AnyLike(folded.Split(' ', StringSplitOptions.RemoveEmptyEntries), Ways.Customer);

    /// <summary>Does this folded text talk about the shelf?</summary>
    public static bool MentionsStock(string folded) =>
        AnyLike(folded.Split(' ', StringSplitOptions.RemoveEmptyEntries), Ways.Stock);

    /// <summary>Does it talk about the people who work here?</summary>
    public static bool MentionsPeople(string folded)
    {
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return AnyLike(tokens, Ways.Staff) || AnyLike(tokens, Ways.Salary);
    }

    /// <summary>Does it talk about the shop's own costs?</summary>
    public static bool MentionsMoneyOwed(string folded)
    {
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return AnyLike(tokens, Ways.Expense) || AnyLike(tokens, Ways.Unpaid);
    }

    /// <summary>
    /// Is this token nothing but a stretch of time? Used to spot a bare follow-up — a
    /// message made only of period words is "and yesterday?", not a new question.
    /// </summary>
    private static bool IsPeriodWord(string token) =>
        TodayWords.Contains(token)
        || HasAny(token, YesterdayStems)
        || HasAny(token, WeekStems)
        || HasAny(token, MonthStems)
        || ShortMonthWords.Contains(token);

    /// <summary>
    /// What is left of a sentence once the words that classified it are taken out — the
    /// material being asked about, the page being asked for. Grammar glue goes too, so
    /// «چقدر برنج تو انبار مونده» leaves just «برنج».
    /// </summary>
    private static string Strip(string[] tokens, params string[][] vocabularies)
    {
        var kept = tokens.Where(t =>
            !GrammarJunk.Contains(t) &&
            !PolitenessFillers.Contains(t) &&
            !NoiseWords.Contains(t) &&
            !QuestionWords.Contains(t) &&
            !vocabularies.Any(v => v.Any(k => TokenLike(t, k))));
        return string.Join(' ', kept).Trim();
    }
}
