using System.Globalization;
using System.Text;

namespace OrderOrange.RestaurantWeb.Services;

/// <summary>
/// The order assistant's compact language brain: folds text across scripts, pulls the
/// table out of a sentence, splits multi-item orders, reads quantities in words and any
/// digit script, and recognizes typed confirmations. Pure functions — fully unit-tested.
/// </summary>
public static partial class PosBotNlu
{
    /// <summary>One requested line: how many of what.</summary>
    public sealed record Want(int Qty, string Query);

    /// <summary>Everything a single utterance asked for.</summary>
    public sealed record Command(List<Want> Items, string? Table);

    // ───────────────────────────── folding ─────────────────────────────

    /// <summary>
    /// Lowercase, strip diacritics, unify Perso-Arabic letter variants, fold every digit
    /// script to ASCII, punctuation to spaces.
    /// </summary>
    public static string Fold(string s)
    {
        var text = SegmentCjk(FoldChars(s));
        return string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The character-level half of folding — no word segmentation.</summary>
    private static string FoldChars(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var raw in s.ToLowerInvariant().Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
            var c = raw switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ک' => 'ك',
                'ی' => 'ي',
                'ھ' or 'ہ' => 'ه',
                'ے' => 'ي',
                _ => raw,
            };
            if (char.IsDigit(c))
            {
                var v = CharUnicodeInfo.GetDecimalDigitValue(c);
                // "میز۵" and "2پیتزا" split at the letter/digit seam.
                if (sb.Length > 0 && IsWordChar(sb[^1]) && !char.IsDigit(sb[^1])) sb.Append(' ');
                sb.Append(v >= 0 ? (char)('0' + v) : c);
                continue;
            }
            if (IsWordChar(c) && sb.Length > 0 && char.IsDigit(sb[^1])) sb.Append(' ');
            sb.Append(IsWordChar(c) ? c : ' ');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Part of a word. Devanagari and its neighbours hang their vowels off the letter
    /// as spacing marks — dropping them would tear «खोलो» into "ख ल".
    /// </summary>
    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) ||
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// Chinese and Japanese write without spaces, so the words the assistant knows are
    /// cut out of the run by hand — longest first, so 「テーブル」 wins over 「ル」 and
    /// 关闭 over 关. Everything else stays glued and simply never matches.
    /// </summary>
    private static readonly string[] CjkVocabulary =
    {
        // Japanese
        "テーブル", "こんにちは", "こんばんは", "おはよう", "ありがとう", "どうも", "メニュー", "ヘルプ", "助けて",
        "満席", "空いている", "空いて", "空き", "会計", "伝票", "売上", "営業額", "予約", "注文", "訂位",
        "閉めて", "閉める", "開けて", "開ける", "今日", "本日", "今週", "今月", "昨日", "夕方", "現金", "カード",
        "人気", "いくつ", "どの", "夜",
        // Chinese
        "桌子", "账单", "帳單", "菜单", "菜單", "帮助", "谢谢", "你好", "取消", "确认", "预订", "订单",
        "营业额", "销售", "畅销", "关闭", "打开", "结账", "现金", "刷卡", "空着", "有人", "使用中",
        "今天", "本周", "本月", "昨天", "现在", "所有", "全部", "多少", "哪些", "晚上",
        // single characters, last so the phrases above always win
        "桌", "席", "空", "关", "开", "有", "的", "些", "号", "位", "名", "人", "今", "好", "不",
        "の", "を", "は", "が",
    };

    /// <summary>The vocabulary folded the same way input is, so 「テーブル」 matches 「テーフル」.</summary>
    private static readonly string[] CjkWords =
        CjkVocabulary.Select(FoldChars).Where(w => w.Length > 0).OrderByDescending(w => w.Length).ToArray();

    private static bool IsCjk(char c) =>
        c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿';

    private static string SegmentCjk(string s)
    {
        if (!s.Any(IsCjk)) return s;
        var sb = new StringBuilder(s.Length * 2);
        for (var i = 0; i < s.Length;)
        {
            var hit = IsCjk(s[i]) ? CjkWords.FirstOrDefault(w => i + w.Length <= s.Length && s.AsSpan(i, w.Length).SequenceEqual(w)) : null;
            if (hit is null) { sb.Append(s[i]); i++; continue; }
            sb.Append(' ').Append(hit).Append(' ');
            i += hit.Length;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Digits only, folded to ASCII: "۵" and "٥" become "5", everything else stays.
    /// The POS code box lives on Persian keyboards — ۳*۵ must ring like 3*5.
    /// </summary>
    public static string AsciiDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsDigit(c))
            {
                var v = CharUnicodeInfo.GetDecimalDigitValue(c);
                sb.Append(v >= 0 ? (char)('0' + v) : c);
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ───────────────────────────── vocabulary ─────────────────────────────

    // Every inflection is spelled out rather than matched by containment: "masala"
    // must never read as "masa", and "vegetable" must never read as "table".
    private static readonly HashSet<string> TableWords =
        new[]
        {
            "table", "tables", "tabel", "tbl",
            "میز", "ميز", "میزها", "میزی", "الميز", "میزیں", "میزوں",
            "طاولة", "طاوله", "الطاولة", "الطاوله", "طاولات", "الطاولات",
            "masa", "masalar", "masaya", "masada", "masanın",
            "mesa", "mesas", "tavolo", "tavoli", "tavolino",
            "tisch", "tische", "tischen", "tisches",
            "стол", "столы", "столик", "столика", "столики", "столе",
            "टेबल", "मेज", "テーブル", "席", "桌", "桌子",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> TableFillers =
        new[]
        {
            "for", "برای", "براي", "به", "الى", "الي", "لل", "على", "علي",
            "için", "icin", "para", "pour", "на", "で", "の", "を",
            "la", "le", "il", "el", "los", "las", "les", "der", "die", "das", "den", "dem", "da", "de", "del",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly Dictionary<string, int> NumberWords = BuildNumbers();

    private static Dictionary<string, int> BuildNumbers()
    {
        var raw = new Dictionary<string, int>
        {
            ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
            ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
            ["یک"] = 1, ["یه"] = 1, ["دو"] = 2, ["سه"] = 3, ["چهار"] = 4, ["پنج"] = 5,
            // "نه" is left out on purpose: it means "no" far more often than "nine".
            ["شش"] = 6, ["هفت"] = 7, ["هشت"] = 8, ["ده"] = 10, ["دوتا"] = 2, ["یدونه"] = 1,
            ["واحد"] = 1, ["اثنين"] = 2, ["اتنين"] = 2, ["ثلاثة"] = 3, ["اربعة"] = 4,
            ["خمسة"] = 5, ["ستة"] = 6, ["سبعة"] = 7, ["ثمانية"] = 8, ["تسعة"] = 9, ["عشرة"] = 10,
            ["bir"] = 1, ["iki"] = 2, ["üç"] = 3, ["dört"] = 4, ["beş"] = 5,
            // "on" (Turkish ten) is left out: in English it is a preposition.
            ["altı"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9,
            ["uno"] = 1, ["una"] = 1, ["dos"] = 2, ["tres"] = 3, ["cuatro"] = 4, ["cinco"] = 5,
            ["seis"] = 6, ["siete"] = 7, ["ocho"] = 8, ["nueve"] = 9, ["diez"] = 10,
            ["un"] = 1, ["une"] = 1, ["deux"] = 2, ["trois"] = 3, ["quatre"] = 4, ["cinq"] = 5,
            ["eins"] = 1, ["zwei"] = 2, ["drei"] = 3, ["vier"] = 4, ["fünf"] = 5, ["sechs"] = 6,
            ["due"] = 2, ["tre"] = 3, ["quattro"] = 4, ["cinque"] = 5,
            ["dois"] = 2, ["duas"] = 2, ["três"] = 3, ["quatro"] = 4,
            ["один"] = 1, ["два"] = 2, ["две"] = 2, ["три"] = 3, ["четыре"] = 4, ["пять"] = 5,
            ["ایک"] = 1, ["تین"] = 3, ["چار"] = 4, ["پانچ"] = 5,
            ["एक"] = 1, ["दो"] = 2, ["तीन"] = 3, ["चार"] = 4, ["पांच"] = 5,
        };
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (word, value) in raw) dict[Fold(word)] = value;
        return dict;
    }

    /// <summary>
    /// A number written as a word, tolerating the clitics Arabic and Persian glue onto
    /// the front of it: «لأربعة أشخاص» is four people, not none.
    /// </summary>
    private static int? WordNumber(string token)
    {
        if (NumberWords.TryGetValue(token, out var n)) return n;
        // Only long number words may be un-glued: «بده» ("give") must not read as
        // ب + ده ("ten"), while «لأربعة» is still four.
        foreach (var prefix in new[] { "ال", "لل", "ل", "ب", "و", "ف" })
            if (token.StartsWith(prefix, StringComparison.Ordinal) && token.Length - prefix.Length >= 3 &&
                NumberWords.TryGetValue(token[prefix.Length..], out var m)) return m;
        return null;
    }

    /// <summary>
    /// Counter and politeness words that belong to the COUNT or the sentence, never to
    /// the dish: "دو عدد چلو کباب" is two chelo kababs, and "لطفا" seasons nothing.
    /// </summary>
    private static readonly HashSet<string> NoiseWords =
        new[]
        {
            "عدد", "تا", "دونه", "دانه", "حبة", "حبه", "قطعة", "قطعه", "پرس",
            "piece", "pieces", "pcs", "portion", "portions", "adet", "tane", "porsiyon",
            "لطفا", "please", "بده", "بیار", "بزن", "کن", "ثبت", "سفارش", "اضافه",
            "میخوام", "می‌خوام", "ابغى", "ابي", "اريد", "بدي", "هات", "جيب",
            "want", "get", "give", "me", "a", "an", "the", "add", "order",
            "lütfen", "lutfen", "istiyorum", "ver", "por favor", "favor", "quiero",
            "s'il", "plaît", "plait", "voudrais", "bitte", "möchte", "mochte",
            "vorrei", "per", "gostaria", "пожалуйста", "хочу", "дай",
        }.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> ConfirmWordSet =
        new[]
        {
            "ثبت", "تایید", "تاييد", "باشه", "بله", "اره", "آره", "اوکی", "حتما", "درسته", "بفرست",
            "ok", "okay", "yes", "yep", "yeah", "yup", "sure", "correct", "right", "confirm", "confirmed", "done", "go",
            "نعم", "تم", "تمام", "اوك", "اكيد", "تثبيت", "يالله", "ماشي", "طيب", "زين",
            "evet", "tamam", "olur", "si", "sí", "sim", "claro", "vale", "да", "давай", "oui", "d'accord",
            "ja", "jawohl", "genau", "certo", "certamente", "ठीक", "हाँ", "हां", "ٹھیک", "هاں", "はい", "确认", "好",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> CancelWordSet =
        new[]
        {
            "لغو", "کنسل", "كنسل", "نه", "نخیر", "خیر", "بیخیال", "ولش",
            "no", "nope", "nah", "cancel", "cancelled", "stop", "forget",
            "لا", "الغاء", "الغ", "خلاص", "iptal", "hayır", "hayir", "vazgeç",
            "нет", "отмена", "non", "annuler", "nein", "abbrechen", "não", "nao", "cancelar",
            "annulla", "नहीं", "نہیں", "いいえ", "取消", "不",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    // ───────────────────────────── intents ─────────────────────────────

    public enum Intent
    {
        Order, OpenTable, CloseTable, Tables,
        FreeTables, BusyTables, Reserved, Revenue, LiveOrders, TableBill, Help,
        Greeting, Thanks, Menu, MakeReservation, TopItems
    }

    public enum PayKind { Cash, Card }

    // Command stems are matched fuzzily (TokenLike): conjugations ("ببندم", "افتحي"),
    // glue ("بازکن") and one-letter typos all land on the verb.
    private static readonly string[] OpenStems =
        new[]
        {
            "باز", "بازكن", "open", "افتح", "فتح", "aç", "ac", "açar", "açın", "açalım", "açsana", "abrir", "открой", "خول",
            "abre", "abra", "ouvr", "ouvre", "öffn", "offn", "eröffn", "aprir", "apri", "apra",
            "откр", "کھول", "کھولیں", "खोल", "खोलो", "खोलें", "खोलिए",
            "开", "打开", "开台", "開", "開けて", "開ける",
        }.Select(Fold).ToArray();

    // NOTE: "بستن" is deliberately absent — "بستنی" (ice cream) contains it and a
    // fuzzy match would turn a dessert order into a close command.
    private static readonly string[] CloseStems =
        new[]
        {
            "ببند", "بسته", "حساب", "تسویه", "تسويه", "پرداخت", "جمع",
            "close", "checkout", "settle", "bill", "pay", "paying", "paid", "clear",
            "اقفل", "سكر", "اغلق", "الحساب", "قفل", "احسب", "دفع", "تصفيه", "تصفية",
            "kapat", "hesap", "ödeme", "odeme", "cerrar", "закрой", "счет",
            "cierr", "cobrar", "cobra", "ferm", "encaiss", "regler", "schliess", "schließ",
            "abrechn", "zahl", "kassier", "chiud", "incass", "pagare", "fech", "encerrar", "pagar",
            "закр", "оплат", "بند", "बंद", "关", "关闭", "结账", "閉めて", "閉める", "会計",
        }.Select(Fold).ToArray();

    /// <summary>بسته/مغلق/closed describe a table's STATE inside a question.</summary>
    private static readonly HashSet<string> ClosedStateWords =
        new[]
        {
            "بسته", "مغلق", "مغلقة", "مغلقه", "مقفلة", "مسكرة", "closed", "kapalı", "kapali",
            // "ferme" (the imperative) is deliberately absent — only «fermée» is a state.
            "cerrada", "cerradas", "cerrado", "cerrados", "fermée", "fermees", "fermées",
            "geschlossen", "chiuso", "chiusi", "chiusa", "fechada", "fechadas", "fechado",
            "закрыты", "закрытые", "закрыт",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>باز/مفتوح/open describe a table's STATE inside a question.</summary>
    private static readonly HashSet<string> OpenStateWords =
        new[]
        {
            "باز", "بازه", "بازن", "بازند", "مفتوح", "مفتوحة", "مفتوحه", "open", "opened",
            "açık", "acik", "abierta", "abiertas", "abierto", "abiertos", "ouverte", "ouvertes",
            "geöffnet", "geoffnet", "offen", "aperto", "aperti", "aperta", "aberta", "abertas", "aberto",
            "открыты", "открытые", "открыт",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>Conjugated do-verbs — their presence keeps a sentence a COMMAND.</summary>
    private static readonly HashSet<string> DoVerbWords =
        new[]
        {
            "کن", "کنم", "کنید", "کنین", "بکن", "بکنم", "ببندم", "ببندید", "بازکنم", "سوي", "افعل",
            // Turkish "et" is left out — it is also the word for meat.
            "کریں", "کرو", "करो", "करें", "edin", "yap", "haz", "fais", "faites", "mach",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> GreetingWords =
        new[]
        {
            "سلام", "درود", "hello", "hi", "hey", "مرحبا", "مرحبه", "هلا", "اهلا", "وسهلا", "سهلا", "هاي",
            "selam", "merhaba", "hola", "привет", "здравствуйте", "bonjour", "bonsoir", "salut",
            "ciao", "salve", "buongiorno", "buonasera", "olá", "ola", "hallo", "guten", "grüß", "gruss",
            "नमस्ते", "ہیلو", "السلام", "عليكم", "خوبی", "چطوری", "خوبين",
            "صبح", "بخیر", "بخير", "عصر", "شب", "مساء", "صباح", "الخير", "وقت",
            "good", "morning", "evening", "afternoon", "day", "günaydın", "gunaydin",
            "iyi", "akşamlar", "aksamlar", "günler", "gunler", "buenos", "buenas", "días", "dias",
            "tardes", "noches", "bom", "boa", "dia", "noite", "tarde", "abend", "tag", "morgen",
            "добрый", "доброе", "вечер", "утро", "день",
            "你好", "こんにちは", "おはよう", "こんばんは",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> ThanksWords =
        new[]
        {
            "ممنون", "ممنونم", "مرسی", "مرسي", "تشکر", "متشکرم", "دمت", "گرم", "لطف", "خسته", "نباشی", "نباشيد",
            "دستت", "دستتون", "نکنه", "درد",
            "thanks", "thank", "thx", "appreciate", "شكرا", "جزيلا", "مشكور", "يعطيك", "العافية", "تسلم",
            "teşekkür", "tesekkur", "teşekkürler", "sağol", "sagol", "eyvallah",
            "gracias", "merci", "спасибо", "благодарю", "danke", "vielen", "grazie", "obrigado", "obrigada",
            "شکریہ", "धन्यवाद", "शुक्रिया", "谢谢", "ありがとう", "どうも",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    // Stem views of the state/thanks vocabularies, so prefixes (المغلقة) and
    // suffixes (teşekkürler) still land. Declared AFTER the sets they mirror.
    private static readonly string[] ClosedStateStems = ClosedStateWords.ToArray();
    private static readonly string[] OpenStateStems = OpenStateWords.ToArray();
    private static readonly string[] ThanksStems = ThanksWords.ToArray();

    /// <summary>
    /// Words that intensify a pleasantry without adding meaning: "thanks A LOT",
    /// «شكرا جزيلا», "danke SCHÖN". They neither make nor break the small talk.
    /// </summary>
    private static readonly HashSet<string> PolitenessFillers =
        new[]
        {
            "you", "so", "very", "much", "lot", "lots", "a", "big", "really", "again", "man", "mate", "bro",
            "خیلی", "بسیار", "زیاد", "جدا", "كتير", "كثير", "واجد", "بابا",
            "mille", "beaucoup", "très", "tres", "bien", "muchas", "muchisimas", "muito", "muita",
            "vielen", "schön", "schon", "sehr", "ederim", "çok", "cok", "большое", "огромное",
            "बहुत", "بہت",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Verbs that settle a table outright. They outrank the bill noun that usually
    /// trails them: «تسویه حساب میز ۵» closes the table, it does not just show it.
    /// </summary>
    private static readonly string[] SettleStems =
        new[] { "تسویه", "تسويه", "تصفيه", "تصفية", "settle", "checkout", "cobrar", "encaiss", "abrechn", "kassier", "结账" }
            .Select(Fold).ToArray();

    /// <summary>Plain verbs that only mean money next to a money word or a date.</summary>
    private static readonly HashSet<string> MoneyVerbs =
        new[] { "make", "made", "take", "took", "taken", "bring", "brought", "درآوردیم", "دراوردیم", "گرفتیم", "قبضنا" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> MoneyWords =
        // Turkish "para" (money) is left out — in Spanish the same word means "for".
        new[] { "much", "money", "cash", "پول", "درامد", "فلوس", "مبلغ", "کم", "dinero", "argent", "geld", "soldi", "деньги" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>"open A table" — an article after the verb makes it an instruction.</summary>
    private static readonly HashSet<string> IndefiniteWords =
        new[] { "a", "an", "the", "one", "یه", "یک", "یکی", "un", "une", "una", "uno", "ein", "eine", "um", "uma", "bir" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> TablesListWords =
        new[]
        {
            "میزها", "میز‌ها", "tables", "الطاولات", "الطاولة", "masalar", "столы", "وضعیت",
            "سالن", "salon", "صالة", "صاله", "الصالة", "الصاله", "وضع", "floor", "hall",
            "status", "durum", "الوضع", "حالة", "الحالة", "estado", "état", "etat", "stato",
            "zustand", "tischstatus", "состояние", "sala", "comedor", "salle",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The same list as stems, so «الصالة» and "tischstatus" land glued. Words that
    /// hide inside food names are NOT here: "sala" is in "salad", "hall" in "shallot",
    /// and Turkish «dürüm» folds to the very same letters as "durum".
    /// </summary>
    private static readonly string[] TablesListStems =
        new[] { "سالن", "salon", "صاله", "صالة", "floor", "status", "durumu", "durumlar", "وضعیت", "وضع",
                "estado", "zustand", "состояни", "tischstatus" }
            .Select(Fold).ToArray();

    /// <summary>Menu talk: "what's on the menu", «منو رو نشون بده».</summary>
    private static readonly HashSet<string> MenuWords =
        new[]
        {
            "منو", "منوی", "منيو", "المنيو", "القائمه", "القائمة", "قائمة", "الاصناف", "الأصناف",
            "menu", "menü", "menus", "menü", "menüyü", "menuyu", "menüde", "menude",
            "carta", "cardápio", "cardapio", "speisekarte", "меню", "مینو",
            "मेन्यू", "मेनू", "メニュー", "菜单", "菜單",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>Menu words as stems, so "menüyü", "menyusu" and «منوها» all land.</summary>
    /// <summary>
    /// The words a "what have you got?" is made of once the question word is taken away.
    /// A sentence of nothing but these and question words wants the menu, not a dish.
    /// </summary>
    private static readonly HashSet<string> HaveWords =
        new[] { "you", "u", "we", "ya", "there", "here", "have", "has", "got", "do", "does", "is", "are", "any", "anything",
                "something", "everything", "available", "availability", "sell", "serve", "offer", "offers", "special", "specials",
                "today", "tonight", "now", "kind", "kinds", "type", "types", "option", "options", "stuff", "things", "items", "item",
                "food", "foods", "dish", "dishes", "the", "a", "an", "of", "on", "in", "for", "to", "me", "us", "your", "our",
                // Arabic / Gulf
                "عند", "عندك", "عندكم", "عندنا", "فيه", "في", "موجود", "متوفر", "شي", "شيء", "اشياء", "اكل", "طعام", "اليوم", "عندهم",
                // Persian
                "دارید", "داريد", "داریم", "داريم", "داري", "دارین", "هست", "هستش", "چیزی", "چيزي", "چیز", "غذا", "غذایی", "امروز", "موجوده",
                // Turkish / others that reach the same sentence
                "var", "neler", "ne", "hay", "tienen", "tienes", "avez", "vous", "habt", "ihr" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly string[] MenuStems =
        new[] { "منو", "منيو", "menu", "menü", "carta", "cardapio", "cardápio", "speisekarte", "меню", "菜单", "メニュー" }
            .Select(Fold).ToArray();

    private static readonly HashSet<string> CashWords =
        new[]
        {
            "نقدی", "نقد", "کش", "cash", "كاش", "نقدا", "nakit", "efectivo", "наличные", "наличными",
            "espèces", "especes", "contanti", "dinheiro", "bar", "نقدأ", "کیش", "नकद", "现金", "現金",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> CardWords =
        new[]
        {
            "کارت", "card", "بطاقة", "بطاقه", "بالبطاقة", "kart", "kartla", "tarjeta", "карта", "картой",
            "carte", "kreditkarte", "cartão", "cartao", "credito", "कार्ड", "刷卡", "カード",
        }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    // ────────────── fuzzy keyword matching: typos and glue are the normal case ──────────────

    /// <summary>Damerau-lite edit distance with an early cap — enough for one-typo words.</summary>
    private static int Edit(string a, string b, int cap)
    {
        if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    /// <summary>
    /// Is this token a "table" word? Exact vocabulary plus one-typo tolerance for the
    /// long forms ("tabel", "الطاوله") — but NO containment: "vegetable" is not a table.
    /// </summary>
    /// <summary>
    /// Typo tolerance is granted to the English and Arabic table words only, and every
    /// other inflection is spelled out instead. One edit away from "masa" is "masala",
    /// from "tisch" is "fisch", from "tavoli" is "tavola" — food, all of it.
    /// </summary>
    private static bool IsTableWord(string t) =>
        TableWords.Contains(t) ||
        (t.Length >= 4 && (Edit(t, "table", 1) <= 1 || Edit(t, TblFolded, 1) <= 1));

    private static readonly string TblFolded = Fold("طاوله");

    /// <summary>
    /// Does this token mean this keyword? Exact, containment (glued words), or one
    /// typo away for words long enough that a single edit can't flip the meaning.
    /// </summary>
    private static bool TokenLike(string token, string key)
    {
        if (token == key) return true;
        if (key.Length >= 4 && token.Contains(key, StringComparison.Ordinal)) return true;
        // Four-letter words have too many minimal pairs to fuzz: "dill" is one edit
        // from "bill", "cold" from "sold", and «بکنی» ("you do") from Urdu «بکنگ»
        // ("booking"). Below five letters a keyword must be spelled, not guessed.
        if (key.Length >= 5 && token.Length >= 3 && Edit(token, key, 1) <= 1) return true;
        return false;
    }

    private static bool AnyLike(string[] tokens, string[] keys) =>
        tokens.Any(t => keys.Any(k => TokenLike(t, k)));

    /// <summary>
    /// Containment-only match — no typo distance. For STATE words, where one edit
    /// flips meaning: "close" (command) must never read as "closed" (state).
    /// </summary>
    private static bool TokenHas(string token, string key) =>
        token == key || (key.Length >= 4 && token.Contains(key, StringComparison.Ordinal));

    private static bool AnyHas(string[] tokens, string[] keys) =>
        tokens.Any(t => keys.Any(k => TokenHas(t, k)));

    /// <summary>Does this single token carry any of these stems?</summary>
    private static bool HasAny(string token, string[] keys) =>
        keys.Any(k => TokenHas(token, k));

    // Stems matched fuzzily inside tokens, so glued words ("میزهارزرو"), plurals and
    // one-letter typos ("رزر") all land.
    private static readonly string[] ReservedStems =
        new[]
        {
            // «رزر» is spelled out because the typo is common and «رزرو» is now too
            // short to reach it by edit distance.
            "رزرو", "رزر", "رزروها", "reserved", "reservation", "reserv", "booking", "booked", "book",
            "حجز", "احجز", "محجوز", "محجوزه", "حجوزات", "rezerv", "rezervasyon",
            "réserv", "reserva", "reservieren", "reservier", "prenot", "брон", "забронир",
            "बुकिंग", "予約", "预订", "訂位",
        }.Select(Fold).ToArray();

    /// <summary>
    /// Booking words too short to fuzz: Urdu «بکنگ» is a single edit from Persian
    /// «بکنی» ("you do"), so it only ever matches itself.
    /// </summary>
    private static readonly HashSet<string> ReservedWords =
        new[] { "بکنگ", "بکنگز", "بکینگ" }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static bool MentionsReservation(string[] tokens) =>
        AnyLike(tokens, ReservedStems) || tokens.Any(ReservedWords.Contains);

    private static readonly string[] RevenueStems =
        new[]
        {
            "فروش", "فروخت", "درامد", "درآمد", "دراورد", "کاسبی", "سود", "فروختیم",
            "revenue", "sales", "sold", "sell", "income", "earning", "earned", "turnover", "takings",
            "مبيعات", "دخل", "بعنا", "ربح", "ربحنا", "ارباح", "أرباح", "ايراد", "إيراد",
            // "kazandı" is left out — «kazandibi» is a dessert, not a takings word.
            "ciro", "gelir", "kazanc", "kazanç", "kazandık", "kazandik", "satis", "satış", "hasilat", "hasılat",
            "выручка", "продаж", "доход", "ventas", "vendim", "vendid", "ingresos", "facturacion", "facturación",
            "chiffre", "affaires", "recette", "vente", "vendu", "umsatz", "einnahmen", "verkauf", "erlös", "erlos",
            "incass", "vendite", "venduto", "fatturato", "vendas", "faturamento", "receita", "vendemos",
            "فروخت", "بکری", "बिक्री", "कमाई", "营业额", "销售", "売上",
        }.Select(Fold).ToArray();

    /// <summary>"list/show/give" words — they turn a phrase into a request for information.</summary>
    private static readonly HashSet<string> RequestWords =
        new[] { "لیست", "فهرست", "list", "قائمة", "قائمه", "liste", "بده", "بدید", "بگو", "نشون", "نشان", "show", "give", "عرض", "اعرض", "göster", "goster",
                "وضعیت", "حالة", "الحالة", "status", "durum",
                "muestra", "muéstrame", "montre", "zeig", "zeige", "mostra", "mostre", "покажи", "دکھاؤ", "दिखाओ" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Grammar glue that must never be mistaken for a table NAME: "میز های" is a
    /// plural, not table "های"; "که/رو/هست" are sentence machinery.
    /// </summary>
    private static readonly HashSet<string> GrammarJunk =
        new[] { "ها", "های", "هایی", "که", "رو", "را", "هست", "هستند", "هستن", "است", "اند", "چی", "چیه",
                "داریم", "دارم", "داره", "نداریم", "موجود",
                "اللی", "التي", "الذي", "لي", "شو", "ايش", "عندنا", "فيه",
                "the", "that", "which", "are", "is", "of", "me", "and", "there", "have", "has", "do", "we",
                "la", "le", "les", "il", "lo", "los", "las", "el", "un", "una", "des", "du", "de", "da", "das", "der", "die", "den", "dem",
                "sont", "est", "son", "están", "estan", "está", "esta", "sind", "sono", "estão", "estao",
                "var", "mı", "mi", "mu", "mü", "ki", "ist", "какие", "ہیں", "ہے", "है", "सी", "कौन", "کی", "का", "کا",
                "の", "を", "は", "が", "有", "的", "些" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> FreeWords =
        new[] { "آزاد", "خالی", "خالیه", "free", "empty", "فارغ", "فاضي", "فاضية", "boş", "bos",
                "libre", "libres", "свободные", "vacío", "vacia", "frei", "libero", "liberi", "livre", "livres", "خالی" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> BusyWords =
        new[] { "مشغول", "پر", "busy", "occupied", "dolu", "مشغولة", "مشغوله", "ocupada", "занятые",
                "ocupadas", "occupée", "occupees", "besetzt", "occupato", "occupati", "ocupado", "ocupados", "занятый" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    // The same free/busy vocabulary as stems, so suffixed forms ("خالیند"), glued
    // words and one-letter typos still read as the state question.
    private static readonly string[] FreeStems =
        new[]
        {
            "ازاد", "خالي", "free", "empty", "vacant", "available", "فارغ", "فاضي", "متاح", "شاغر",
            "boş", "bos", "libre", "свободн", "vací", "vaci", "frei", "liber", "livre", "disponi",
            "空", "空着", "空き", "空いている", "空いて", "خالی", "खाली",
        }.Select(Fold).ToArray();

    private static readonly string[] BusyStems =
        new[]
        {
            "مشغول", "پر", "پره", "پرن", "busy", "occupied", "occup", "taken", "seated", "ممتلئ",
            "dolu", "ocupa", "besetzt", "занят", "有人", "使用中", "満席",
        }.Select(Fold).ToArray();

    private static readonly HashSet<string> OrderCountWords =
        new[] { "سفارش", "سفارشها", "سفارشات", "orders", "order", "طلبات", "طلب", "sipariş", "siparis", "заказы", "заказов",
                "pedidos", "pedido", "commandes", "commande", "bestellungen", "ordini", "ordine", "آرڈر", "ऑर्डर", "订单", "注文" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>"right now" words — they turn a bare "orders" into the live board.</summary>
    private static readonly HashSet<string> CurrentWords =
        new[] { "الان", "الآن", "جاری", "جاري", "فعلی", "فعلي", "حالا", "live", "current", "now", "الحالية", "الحاليه",
                "aktif", "şimdi", "simdi", "сейчас", "ahora", "actuales", "maintenant", "actuel", "jetzt", "aktuell",
                "adesso", "attuali", "agora", "atuais", "ابھی", "अभी", "现在", "今" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>"today" — with orders it means the day's ledger, not the live board.</summary>
    private static readonly HashSet<string> TodayWords =
        new[] { "امروز", "اليوم", "today", "bugün", "bugun", "bugünkü", "bugunku", "сегодня",
                "hoy", "aujourd", "hui", "heute", "oggi", "hoje", "آج", "आज", "今天", "本日", "今日" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>"تعداد میزها" — a count question wants numbers, not action chips.</summary>
    private static readonly HashSet<string> CountWords =
        new[] { "تعداد", "count", "sayısı", "sayisi", "cuántas", "cuantas", "combien",
                "anzahl", "quante", "quantas", "количество", "कितने", "کتنی" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly string[] BillStems =
        new[]
        {
            "صورتحساب", "صورت", "فاكتور", "فاکتور", "invoice", "الفاتورة", "فاتورة", "bill", "حساب", "الحساب",
            "hesap", "conto", "cuenta", "addition", "note", "rechnung", "beleg", "conta", "счет", "счёт",
            "بل", "बिल", "账单", "帳單", "会計", "伝票",
        }.Select(Fold).ToArray();

    private static readonly HashSet<string> HelpWords =
        new[] { "کمک", "کمکم", "کمکی", "راهنما", "راهنمایی", "help", "مساعدة", "مساعده", "ساعدني",
                "yardım", "yardim", "ayuda", "aide", "hilfe", "aiuto", "ajuda", "помощь", "помоги",
                "مدد", "मदद", "सहायता", "帮助", "ヘルプ", "助けて" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>Help words as stems, so «کمکم», "yardıma" and "ayúdame" all land.</summary>
    private static readonly string[] HelpStems = HelpWords.ToArray();

    /// <summary>Negators that flip a state: "رزرو نشده" is the opposite of reserved.</summary>
    private static readonly HashSet<string> NegationWords =
        new[] { "نشده", "نشدن", "نیست", "نیستن", "غیر", "بدون", "not", "non", "غير", "مش", "مو", "değil", "degil", "не",
                // English "no" is left out — "table no 5" is a label, not a negation.
                "sin", "sans", "ohne", "senza", "sem", "без" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> QuestionWords =
        new[] { "کدام", "کدوم", "چند", "چندتا", "چقدر", "چنده", "چیه", "چیست", "کی", "کجا", "چطور", "چطوره", "چیکار", "تعداد",
                "what", "which", "how", "many", "much", "كم", "أي", "اي", "ايش", "وش", "شنو", "بكم", "كيف", "شلون",
                "hangi", "kaç", "kac", "nasıl", "nasil", "ne", "mı", "mi", "mu", "что", "какие", "сколько", "как",
                "qué", "que", "cuál", "cual", "cuánto", "cuanto", "cuántas", "cuantas", "quelles", "quelle", "quel",
                "combien", "welche", "welcher", "wie", "wieviel", "quali", "quanto", "quante", "quais", "quantos",
                "कौन", "कितने", "کون", "کتنی", "哪些", "多少", "どの", "いくつ" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    // ─────────────── booking details: time and party size ───────────────

    private static readonly HashSet<string> TimeWords =
        // Bare "a" is left out — in English it is an article, not a clock word.
        new[] { "ساعت", "الساعة", "الساعه", "at", "saat", "à", "в", "um", "alle", "las", "às", "as", "بجے", "बजे" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> EveningWords =
        new[] { "شب", "امشب", "عصر", "غروب", "مساء", "الليلة", "الليله", "المساء", "pm", "tonight", "evening", "night",
                "akşam", "aksam", "вечера", "вечером", "noche", "noite", "sera", "serata", "soir", "abends", "nachmittag",
                "شام", "शाम", "晚上", "夜", "夕方" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> GuestWords =
        new[] { "نفر", "نفره", "نفري", "شخص", "اشخاص", "أشخاص", "انفار", "people", "persons", "person", "guests", "guest", "pax",
                "kişi", "kisi", "kişilik", "kisilik", "человек", "гостей", "personas", "personnes", "personne",
                "personen", "gäste", "gaste", "persone", "pessoas", "افراد", "لوگ", "लोग", "位", "名", "人" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// "ساعت ۸", "at 8 30", "8pm امشب" — the clock time a booking asks for.
    /// Evening words push small hours into the evening: ساعت ۸ شب is 20:00.
    /// </summary>
    public static (int Hour, int Minute)? ExtractTime(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var evening = tokens.Any(EveningWords.Contains);
        int? hour = null, minute = null;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!TimeWords.Contains(tokens[i]) || i + 1 >= tokens.Length) continue;
            if (!int.TryParse(tokens[i + 1], out var h) || h is < 0 or > 23) continue;
            hour = h;
            // "ساعت ۸ ۳۰" — folding turned 8:30 into two tokens.
            if (i + 2 < tokens.Length && int.TryParse(tokens[i + 2], out var m) && m is >= 0 and < 60 && !GuestWords.Contains(i + 3 < tokens.Length ? tokens[i + 3] : ""))
                minute = m;
            break;
        }
        if (hour is null) return null;
        if (evening && hour <= 11) hour += 12;
        return (hour.Value, minute ?? 0);
    }

    /// <summary>"برای ۴ نفر" / "for 4 people" — the number right before the people-word.</summary>
    public static int? ExtractGuests(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < tokens.Length; i++)
        {
            if (!GuestWords.Contains(tokens[i])) continue;
            if (int.TryParse(tokens[i - 1], out var n) && n is > 0 and <= 50) return n;
            if (WordNumber(tokens[i - 1]) is { } w) return w;
        }
        return null;
    }

    // ─────────────── revenue periods ───────────────

    public enum Period { Today, Yesterday, Week, Month }

    private static readonly string[] YesterdayStems =
        new[] { "دیروز", "دیشب", "امس", "أمس", "البارحة", "البارحه", "yesterday", "dün", "dun", "dünkü", "dunku",
                // Urdu «کل» is left out — it also means "all", and «فروش کل» is a total, not yesterday.
                "вчера", "ayer", "hier", "gestern", "ieri", "ontem", "昨天", "昨日" }
            .Select(Fold).ToArray();

    private static readonly string[] WeekStems =
        new[] { "هفته", "week", "اسبوع", "الاسبوع", "أسبوع", "hafta", "недел", "semana", "semaine", "woche",
                "settimana", "ہفتہ", "सप्ताह", "本周", "今週" }
            .Select(Fold).ToArray();

    private static readonly string[] MonthStems =
        new[] { "ماه", "month", "شهر", "الشهر", "месяц", "меся", "mes", "mois", "monat", "mese", "mês",
                "مہینہ", "महीने", "本月", "今月" }
            .Select(Fold).ToArray();

    /// <summary>Turkish "ay" is a month — but a bare two-letter token only, never inside a word.</summary>
    private static readonly HashSet<string> ShortMonthWords =
        new[] { "ay", "ayın", "ayin" }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>Which stretch of time a money/orders question is about.</summary>
    public static Period DetectPeriod(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (AnyLike(tokens, YesterdayStems)) return Period.Yesterday;
        if (AnyLike(tokens, WeekStems)) return Period.Week;
        if (AnyLike(tokens, MonthStems) || tokens.Any(ShortMonthWords.Contains)) return Period.Month;
        return Period.Today;
    }

    // ─────────────── draft editing ───────────────

    /// <summary>Best-seller talk: «پرفروش‌ترین», "bestseller", «محبوب‌ترین».</summary>
    private static readonly string[] TopStems =
        new[] { "پرفروش", "محبوب", "bestsell", "encok", "meistverkauft", "topseller", "畅销", "人気" }
            .Select(Fold).ToArray();

    /// <summary>"the most", «الأكثر», «بیشترین» — with a sales word it asks for the leaderboard.</summary>
    private static readonly HashSet<string> MostWords =
        new[] { "most", "بیشترین", "بیشتر", "الاكثر", "الأكثر", "اكثر", "top", "best", "encok", "çok",
                "más", "mas", "plus", "meist", "più", "piu", "mais", "больше", "самый" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> AppendWords =
        new[] { "اضافه", "اضاف", "أضف", "اضف", "زد", "كمان", "ايضا", "بازم", "همچنین", "دیگه", "دیگر", "هم",
                "add", "also", "more", "another", "extra", "ekle", "еще", "ещё", "más", "mas", "encore",
                "noch", "ancora", "mais", "añade", "anade", "agrega", "otra", "otro", "ajoute", "autre",
                "aggiungi", "adiciona", "outra", "hinzu", "زیادہ", "और" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> RemoveWordSet =
        new[] { "حذف", "احذف", "بردار", "پاک", "شيل", "امسح", "remove", "delete", "drop", "sil", "çıkar", "cikar",
                "убери", "удали", "quita", "quitar", "elimina", "enlève", "enleve", "supprime", "entferne", "lösche",
                "losche", "rimuovi", "togli", "remova", "retire", "ہٹاؤ", "हटाओ" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>"کل میزها" / "all tables" — the caller should not truncate the list.</summary>
    private static readonly HashSet<string> AllWords =
        new[] { "کل", "همه", "همهی", "تمام", "تمامی", "كل", "جميع", "all", "every", "hepsi", "tüm", "tum", "все",
                "todas", "todos", "toutes", "tous", "alle", "tutti", "tutte", "سب", "सभी", "所有", "全部" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    public static bool WantsAll(string raw) =>
        Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(AllWords.Contains);

    /// <summary>
    /// "چکار می‌تونی برام انجام بدی؟" — a question about the ASSISTANT itself, not
    /// about the floor and never about food. It must reach Help, not the menu matcher.
    /// </summary>
    /// <summary>Words that can ONLY be about the assistant — no self-word needed.</summary>
    private static readonly string[] StrongAbilityStems =
        new[] { "قابلیت", "توانایی", "امکانات", "دستورات", "قدراتك", "امكانياتك", "وظيفتك",
                "abilities", "capabilities", "commands", "features", "yetenek",
                "funciones", "fonctionnalit", "funktionen", "funzionalit", "funcionalidades", "возможности",
                // Second-person modals: "can YOU" is about the assistant, full stop.
                "puedes", "podés", "peux", "pouvez", "kannst", "könnt", "konnt", "puoi", "podes", "можешь",
                "yapabilirsin", "تقدر" }
            .Select(Fold).ToArray();

    /// <summary>Ability verbs that need a self-word: "چه" alone is often about food.</summary>
    private static readonly HashSet<string> AbilityWords =
        new[] { "چکار", "چیکار", "چه", "کارهایی", "کارها", "بلدی", "میتونی", "ميتوني", "میتوانی", "بتونی", "دستور",
                "تقدر", "تسوي", "تعمل",
                "can", "could", "able", "options", "use", "work", "works", "yapabilir", "yapabilirsin",
                "puedes", "peux", "pouvez", "kannst", "können", "konnen", "puoi", "pode", "можешь" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> SelfWords =
        new[] { "تو", "شما", "خودت", "برام", "برایم", "برای", "من", "انجام", "کنی", "بدی",
                "انت", "لي", "you", "u", "your", "me", "for", "do", "help", "sen", "bana",
                "tú", "tu", "usted", "toi", "vous", "du", "sie", "você", "voce", "ты", "мне" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>Is the user asking what this assistant can do?</summary>
    public static bool AsksAboutAbilities(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        // "could you tell me which tables are free" is polite English about the FLOOR.
        // Anything concrete in the sentence — a table, money, a count — wins over the
        // modal verb, so only a question with nothing else in it reaches Help.
        if (tokens.Any(t => IsTableWord(t) || TablesListWords.Contains(t) || OrderCountWords.Contains(t) ||
                            MenuWords.Contains(t) || int.TryParse(t, out _) || WordNumber(t) is not null) ||
            AnyLike(tokens, RevenueStems) || MentionsReservation(tokens) || AnyLike(tokens, BillStems) ||
            AnyLike(tokens, FreeStems) || AnyLike(tokens, BusyStems) || AnyLike(tokens, TablesListStems))
            return false;
        // "قابلیت‌هات چیه" — these words are never about food, so they stand alone.
        if (AnyLike(tokens, StrongAbilityStems)) return true;
        var ability = tokens.Count(AbilityWords.Contains);
        if (ability == 0) return false;
        // "چکار می‌تونی" needs a self-word too, so "چه غذایی داریم" stays about food.
        return tokens.Any(SelfWords.Contains) || ability >= 2;
    }

    /// <summary>"یه نوشابه هم اضافه کن" — grow the pending order instead of replacing it.</summary>
    public static bool WantsAppend(string raw) =>
        Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(AppendWords.Contains);

    /// <summary>"نوشابه رو حذف کن" — take a line off the pending order.</summary>
    public static bool WantsRemove(string raw) =>
        Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(RemoveWordSet.Contains);

    /// <summary>Does the sentence read as a question or an information request?</summary>
    public static bool LooksLikeQuestion(string raw)
    {
        if (raw.Contains('?') || raw.Contains('؟')) return true;
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(QuestionWords.Contains) || tokens.Any(RequestWords.Contains);
    }

    /// <summary>What kind of request is this sentence, before item parsing?</summary>
    public static Intent DetectIntent(string raw)
    {
        var folded = Fold(raw);
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Word-level first; the raw-substring fallbacks are only for scripts that glue
        // their suffixes on («میزهای», «الطاولات», 「テーブル」) — never for Latin, where
        // "vegetable" and "masala" would otherwise become tables.
        var mentionsTables = tokens.Any(IsTableWord) || tokens.Any(TablesListWords.Contains)
                             || AnyLike(tokens, TablesListStems)
                             || folded.Contains("ميز") || folded.Contains("طاول")
                             || folded.Contains("टेबल") || folded.Contains("テーブル") || folded.Contains("桌");

        // A pleasantry is social when every word in it is one: "thank you so much" and
        // «شكرا جزيلا» are thanks, while "soup of the day" keeps its own meaning.
        var thankish = (string t) => ThanksStems.Any(s => TokenLike(t, s));
        var polite = tokens.Where(t => !GrammarJunk.Contains(t) && !PolitenessFillers.Contains(t)).ToArray();
        if (polite.Length > 0 && polite.All(t => thankish(t) || GreetingWords.Contains(t)))
            return polite.Any(thankish) ? Intent.Thanks : Intent.Greeting;

        // "چکار می‌تونی برام انجام بدی؟" is about the assistant, not the floor or food.
        if (AsksAboutAbilities(raw)) return Intent.Help;

        // Questions about the floor and the money come before verbs — "which tables
        // are reserved?" contains no order at all. All fuzzy: typos are the normal case.
        var verbish = tokens.Any(DoVerbWords.Contains);
        var aboutOneTable = ExtractsRealTable(raw);

        // "پرفروش‌ترین غذا؟" — the leaderboard, before the ledger claims "فروش".
        if (AnyLike(tokens, TopStems)
            || ((tokens.Contains("top") || tokens.Contains("best")) &&
                tokens.Any(t => t is "seller" or "sellers" or "selling" or "item" or "items" or "dish" or "dishes" or "food" or "foods" or "products"))
            || (tokens.Any(MostWords.Contains) && AnyLike(tokens, RevenueStems)))
            return Intent.TopItems;

        // "میزهای رزرو نشده" flips the meaning: NOT reserved = the free tables.
        // "میز 6 رو رزرو کن" / "book table 5" is a booking COMMAND, not the list.
        if (MentionsReservation(tokens))
        {
            if (mentionsTables && tokens.Any(NegationWords.Contains)) return Intent.FreeTables;
            // "کنسل کن رزرو میز 2" manages the book — the list page does that.
            if (tokens.Any(CancelWordSet.Contains) || tokens.Any(RemoveWordSet.Contains)) return Intent.Reserved;
            if (verbish || (aboutOneTable && !LooksLikeQuestion(raw))) return Intent.MakeReservation;
            return Intent.Reserved;
        }
        if (AnyLike(tokens, RevenueStems)) return Intent.Revenue;
        // "how much did we make today" — a plain verb becomes a money question only
        // next to a money word or a date, and never when a table is in the sentence.
        if (!mentionsTables && tokens.Any(MoneyVerbs.Contains) &&
            (tokens.Any(MoneyWords.Contains) || tokens.Any(TodayWords.Contains) ||
             AnyLike(tokens, YesterdayStems) || AnyLike(tokens, WeekStems) || AnyLike(tokens, MonthStems)))
            return Intent.Revenue;

        // «تسویه حساب میز 5» — a settle verb outranks the bill noun that trails it.
        if (AnyLike(tokens, SettleStems) && (verbish || !LooksLikeQuestion(raw))) return Intent.CloseTable;
        // "حساب میز 2" shows the bill; "میز 2 رو حساب کن" (a do-verb) settles it.
        if (AnyLike(tokens, BillStems) && mentionsTables && !verbish) return Intent.TableBill;
        if (mentionsTables && !aboutOneTable && AnyLike(tokens, FreeStems)) return Intent.FreeTables;
        if (mentionsTables && !aboutOneTable && AnyLike(tokens, BusyStems)) return Intent.BusyTables;

        // "تعداد میزهای باز" asks for NUMBERS — and "باز" is ambiguous (open bill? open
        // for guests?). The floor summary answers with both counts and dodges the trap.
        if (mentionsTables && tokens.Any(CountWords.Contains)) return Intent.Tables;

        // "کدام میزها بسته هستن؟" asks about a STATE — بسته/باز describe tables here,
        // they are not the close/open command. A conjugated do-verb keeps it a command.
        // "open a table" is an instruction; "open tables" is a question about state.
        var leadsWithVerb = tokens.Length >= 2 && IndefiniteWords.Contains(tokens[1]) &&
                            (AnyLike(tokens[..1], OpenStems) || AnyLike(tokens[..1], CloseStems));
        if (mentionsTables && !verbish && !leadsWithVerb)
        {
            // Grammar glue stays out of the state match, and states use containment
            // only — "close" (command) must never read as "closed" (state).
            var meaty = tokens.Where(t => !GrammarJunk.Contains(t)).ToArray();
            var closedState = AnyHas(meaty, ClosedStateStems);
            var openState = AnyHas(meaty, OpenStateStems);
            if ((closedState || openState) && (LooksLikeQuestion(raw) || !aboutOneTable))
            {
                // "میز 4 بازه؟" asks about ONE table — its bill answers the question.
                if (aboutOneTable && LooksLikeQuestion(raw)) return Intent.TableBill;
                return closedState ? Intent.FreeTables : Intent.BusyTables;
            }
        }
        // "الان چند تا سفارش داریم؟" is the live board — but a quantity turns the same
        // words into an order: "یه پیتزا سفارش بده" places a pizza, it doesn't ask.
        // Only quantities left over AFTER the table number is removed count.
        var hasQty = ExtractTable(raw).Remainder
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(t => int.TryParse(t, out _) || WordNumber(t) is not null);
        // "تعداد سفارشات امروز" / "فروش دیروز" flavor of orders → the day's ledger,
        // which answers with a count AND the money. "الان" keeps the live board.
        if (tokens.Any(OrderCountWords.Contains) && !hasQty &&
            (tokens.Any(TodayWords.Contains) || AnyLike(tokens, YesterdayStems) ||
             AnyLike(tokens, WeekStems) || AnyLike(tokens, MonthStems))) return Intent.Revenue;
        if (tokens.Any(OrderCountWords.Contains) && !hasQty &&
            (LooksLikeQuestion(raw) || tokens.Any(CurrentWords.Contains))) return Intent.LiveOrders;
        if (AnyLike(tokens, HelpStems)) return Intent.Help;

        // "میز ۳ رو خالی کن" / "free up table 6" — clearing a table IS closing it.
        // A free word LEADING the sentence is the verb; trailing it, it is a question.
        if (aboutOneTable && AnyLike(tokens, FreeStems) &&
            (verbish || (!LooksLikeQuestion(raw) && AnyLike(tokens[..1], FreeStems))))
            return Intent.CloseTable;

        // Commands, matched on stems so conjugations and typos land ("ببندم", "بازکنم").
        // A question without a do-verb is never a command: "جمع میز 5 چقدره؟" reads
        // the bill, while "جمع کن میز 5" settles it.
        if (AnyLike(tokens, CloseStems) && (verbish || !LooksLikeQuestion(raw))) return Intent.CloseTable;
        if (AnyLike(tokens, OpenStems) && (verbish || !LooksLikeQuestion(raw))) return Intent.OpenTable;

        // "mesa 3 en efectivo" — a table plus a way to pay and nothing else is a
        // settle instruction; with items in it ("2 pizza table 3") it stays an order.
        if (aboutOneTable && !hasQty && !LooksLikeQuestion(raw) && DetectPay(raw) is not null)
            return Intent.CloseTable;

        // Menu talk comes after the commands so «la carte» in "ferme la table 5 par
        // carte" stays a payment word, and a quantity keeps "2 menudo" an order.
        if (!hasQty && (tokens.Any(MenuWords.Contains) || AnyLike(tokens, MenuStems))) return Intent.Menu;

        // "What do you have?" — a question with nothing in it but question words and
        // "have" words is asking for the menu, not ordering a dish called "what".
        if (!hasQty && tokens.Length > 0 && tokens.Any(QuestionWords.Contains)
            && tokens.All(t => QuestionWords.Contains(t) || HaveWords.Contains(t) || GrammarJunk.Contains(t)
                               || PolitenessFillers.Contains(t) || t.Length <= 1))
            return Intent.Menu;

        // "وضعیت سالن" / "table status" — a floor word without a table number is the
        // overview; «وضعیت میز 5» stays that one table's bill.
        if (mentionsTables && !aboutOneTable && AnyLike(tokens, TablesListStems)) return Intent.Tables;

        // "état des tables" — a floor noun next to a real table word is the overview.
        if (tokens.Any(IsTableWord) && !aboutOneTable && tokens.Any(TablesListWords.Contains))
            return Intent.Tables;

        // "لیست میزها" — a listing request about tables is the floor overview.
        if (mentionsTables && tokens.Any(RequestWords.Contains) && !aboutOneTable)
            return Intent.Tables;
        // "میز 2 چقدر شده؟" — a question aimed at a SPECIFIC table is its bill;
        // the same question with no table ("چند میز داریم؟") is the floor overview.
        // A leftover quantity ("یه پیتزا … بده میز 2") means it was an order all along.
        if (mentionsTables && LooksLikeQuestion(raw) && !hasQty)
            return aboutOneTable ? Intent.TableBill : Intent.Tables;
        // A bare "میزها" is the floor; "2 dürüm" is a wrap that folds to the same
        // letters as "durum", so a quantity keeps it an order.
        if (tokens.Length <= 2 && !hasQty &&
            (tokens.Any(TablesListWords.Contains) || AnyLike(tokens, TablesListStems)))
            return Intent.Tables;
        return Intent.Order;
    }

    private static bool ExtractsRealTable(string raw) => ExtractTable(raw).Table is not null;

    /// <summary>Cash or card, if the sentence says so.</summary>
    public static PayKind? DetectPay(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(CashWords.Contains)) return PayKind.Cash;
        if (tokens.Any(CardWords.Contains)) return PayKind.Card;
        return null;
    }

    /// <summary>Is this whole short message a typed YES to the pending card?</summary>
    public static bool IsConfirm(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length is > 0 and <= 2 && tokens.All(ConfirmWordSet.Contains);
    }

    /// <summary>Is this whole short message a typed NO?</summary>
    public static bool IsCancel(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length is > 0 and <= 2 && tokens.All(CancelWordSet.Contains);
    }

    // ───────────────────────────── parsing ─────────────────────────────

    /// <summary>Reads one utterance: items with quantities, plus the table if named.</summary>
    public static Command Parse(string raw)
    {
        var (table, rest) = ExtractTable(raw);
        var items = SplitItems(rest)
            .Select(ExtractQty)
            .Where(w => w.Query.Length > 0)
            .ToList();
        return new Command(items, table);
    }

    /// <summary>
    /// Finds "table 2 / میز ۲ / طاولة ٢" anywhere; returns the token and the rest.
    /// A grammar word after the table word ("میز های…", "table that…") is NOT a table
    /// name — the search moves on instead of inventing a table called "های".
    /// </summary>
    /// <summary>"table no 5" / "میز شماره ۵" — the label between word and number.</summary>
    private static readonly HashSet<string> NumberLabelWords =
        new[] { "شماره", "رقم", "number", "no", "num", "numara", "номер", "号", "番" }
            .Select(Fold).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A word the assistant already knows means something else can never be a table's
    /// NAME: "free tables please" has no table called "please".
    /// </summary>
    private static bool IsKeyword(string token) =>
        GrammarJunk.Contains(token) || RequestWords.Contains(token) ||
        QuestionWords.Contains(token) || DoVerbWords.Contains(token) ||
        TableFillers.Contains(token) || CountWords.Contains(token) ||
        NoiseWords.Contains(token) || AppendWords.Contains(token) || RemoveWordSet.Contains(token) ||
        ConfirmWordSet.Contains(token) || CancelWordSet.Contains(token) ||
        GreetingWords.Contains(token) || ThanksWords.Contains(token) ||
        HelpWords.Contains(token) || MenuWords.Contains(token) ||
        NegationWords.Contains(token) || CurrentWords.Contains(token) || TodayWords.Contains(token) ||
        GuestWords.Contains(token) || OrderCountWords.Contains(token) ||
        HasAny(token, FreeStems) || HasAny(token, BusyStems) ||
        HasAny(token, ClosedStateStems) || HasAny(token, OpenStateStems) ||
        HasAny(token, MenuStems) || HasAny(token, BillStems) || HasAny(token, TablesListStems) ||
        ReservedStems.Any(s => TokenLike(token, s));

    public static (string? Table, string Remainder) ExtractTable(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!IsTableWord(tokens[i])) continue;
            // Chinese puts the number first — 「3号桌」 is table 3. Look left when the
            // token to the right is missing or is a word we already know.
            if (i + 1 >= tokens.Count || IsKeyword(tokens[i + 1]))
            {
                var b = i - 1;
                var labelled = b >= 0 && NumberLabelWords.Contains(tokens[b]);
                if (labelled) b--;
                // A bare number in front only counts behind a label or a CJK table
                // word — otherwise "2 tables" would become table 2.
                if (b >= 0 && int.TryParse(tokens[b], out _) && (labelled || tokens[i].Any(IsCjk)))
                {
                    var back = tokens[b];
                    for (var k = i; k >= b; k--) tokens.RemoveAt(k);
                    return (back, string.Join(' ', tokens));
                }
                if (i + 1 >= tokens.Count) continue;
            }
            var j = i + 1;
            // "میز شماره ۵": the label points one token further along.
            if (NumberLabelWords.Contains(tokens[j]) && j + 1 < tokens.Count) j++;
            var value = tokens[j];
            // Plural markers, sentence glue and keywords can't be a table's name.
            // States are matched by STEM, not exact word: "میزها خالین" describes the
            // floor, so "خالین" must not become a table called "خالین".
            if (IsKeyword(value)) continue;
            // "میز دو" is table 2 — word numbers fold to digits like the scripts do.
            if (WordNumber(value) is { } n) value = n.ToString();
            for (var k = j; k >= i; k--) tokens.RemoveAt(k);
            if (i > 0 && TableFillers.Contains(tokens[i - 1])) tokens.RemoveAt(i - 1);
            return (value, string.Join(' ', tokens));
        }
        return (null, string.Join(' ', tokens));
    }

    /// <summary>"2 pizzas AND a cola" → separate item segments (folded input).</summary>
    public static IEnumerable<string> SplitItems(string folded)
    {
        var current = new List<string>();
        foreach (var token in folded.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token is "و" or "and" or "ve" or "et" or "y" or "и")
            {
                if (current.Count > 0) { yield return string.Join(' ', current); current.Clear(); }
                continue;
            }
            current.Add(token);
        }
        if (current.Count > 0) yield return string.Join(' ', current);
    }

    /// <summary>Reads the quantity out of one folded segment; noise words vanish.</summary>
    public static Want ExtractQty(string segment)
    {
        var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !NoiseWords.Contains(t) && !GrammarJunk.Contains(t) &&
                        !QuestionWords.Contains(t) && !AppendWords.Contains(t) && !RemoveWordSet.Contains(t))
            .ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (int.TryParse(tokens[i], out var n) && n is > 0 and <= 50)
            {
                tokens.RemoveAt(i);
                return new Want(n, string.Join(' ', tokens));
            }
            if (NumberWords.TryGetValue(tokens[i], out var w))
            {
                tokens.RemoveAt(i);
                return new Want(w, string.Join(' ', tokens));
            }
        }
        return new Want(1, string.Join(' ', tokens));
    }
}
