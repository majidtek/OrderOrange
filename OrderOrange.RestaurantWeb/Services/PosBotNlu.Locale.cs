using System.Globalization;

namespace OrderOrange.RestaurantWeb.Services;

/// <summary>
/// Which language the owner just wrote in — so the assistant can answer in THAT language
/// rather than in whatever the portal happens to be set to.
///
/// <para>This matters more in a partner portal than it looks. A shop in Muscat may have
/// the interface in Farsi because the owner set it up, while the manager on the evening
/// shift types Arabic. Replying to Arabic in Farsi is not a translation problem, it is
/// the assistant ignoring the person in front of it.</para>
///
/// <para>The hard case is that Arabic, Persian and Urdu share one script. Script counting
/// gets you to "Perso-Arabic" and no further, so the tie is broken by letters that only
/// one of the three uses and by a short list of very common function words. When nothing
/// decides, the portal's own language wins — a wrong guess is worse than no guess.</para>
/// </summary>
public static partial class PosBotNlu
{
    /// <summary>
    /// The language of this message, or null when the text carries no signal at all
    /// (a bare number, a dish name in Latin letters, an emoji).
    /// </summary>
    public static string? DetectLocale(string raw, string? uiLocale = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        int kana = 0, han = 0, devanagari = 0, cyrillic = 0, perso = 0, latin = 0;
        foreach (var ch in raw)
        {
            if (ch is >= '぀' and <= 'ヿ') kana++;
            else if (ch is >= '一' and <= '鿿') han++;
            else if (ch is >= 'ऀ' and <= 'ॿ') devanagari++;
            else if (ch is >= 'Ѐ' and <= 'ӿ') cyrillic++;
            else if (ch is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ') perso++;
            else if (char.IsLetter(ch) && ch < 128) latin++;
        }

        // Kana settles Japanese even in a sentence that is mostly kanji; han alone is
        // Chinese, because Japanese almost never writes a whole message without kana.
        if (kana > 0) return "ja";
        if (han > 0) return "zh";
        if (devanagari > 0) return "hi";
        if (cyrillic > 0) return "ru";
        if (perso > 0) return PersoArabic(raw, uiLocale);
        if (latin > 0) return LatinLanguage(raw);
        return null;
    }

    /// <summary>Letters no Arabic keyboard produces — the strongest Persian/Urdu signal.</summary>
    private const string PersianOnlyLetters = "گچپژ";

    /// <summary>Retroflex and Urdu-only forms: ٹ ڈ ڑ ں ے ھ ۂ ۃ.</summary>
    private const string UrduOnlyLetters = "ٹڈڑںےھۂۃ";

    /// <summary>Forms an Arabic keyboard produces and a Persian one does not.</summary>
    private const string ArabicOnlyLetters = "ةىإأؤئ";

    private static string? PersoArabic(string raw, string? uiLocale)
    {
        int persian = 0, urdu = 0, arabic = 0;
        foreach (var ch in raw)
        {
            if (UrduOnlyLetters.Contains(ch)) urdu++;
            else if (PersianOnlyLetters.Contains(ch)) persian++;
            else if (ArabicOnlyLetters.Contains(ch)) arabic++;
            // The keyboard signal: Persian ی/ک against Arabic ي/ك. Weaker than a
            // distinctive letter, so it counts for less.
            else if (ch is 'ی' or 'ک') persian++;
            else if (ch is 'ي' or 'ك') arabic++;
        }

        // Urdu shares گچپژ with Persian, so its own letters have to outrank them.
        if (urdu > 0 && urdu >= persian) return "ur";

        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var persianWords = tokens.Count(t => Marks.Persian.Contains(t));
        var arabicWords = tokens.Count(t => Marks.Arabic.Contains(t));
        var urduWords = tokens.Count(t => Marks.Urdu.Contains(t));

        if (urduWords > persianWords && urduWords > arabicWords) return "ur";

        // Words are worth more than letter shapes: someone typing Persian on an Arabic
        // keyboard still writes «چقدر» and «است».
        var persianScore = persian + persianWords * 3;
        var arabicScore = arabic + arabicWords * 3;

        if (persianScore > arabicScore) return "fa";
        if (arabicScore > persianScore) return "ar";

        // A genuine tie: the script says nothing the portal does not already know.
        return uiLocale is "fa" or "ar" or "ur" ? uiLocale : null;
    }

    private static string? LatinLanguage(string raw)
    {
        var tokens = Fold(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        (string Code, string[] Words)[] candidates =
        [
            ("tr", Marks.Turkish), ("es", Marks.Spanish), ("fr", Marks.French),
            ("de", Marks.German), ("it", Marks.Italian), ("pt", Marks.Portuguese),
            ("en", Marks.English),
        ];

        var best = "";
        var bestHits = 0;
        var tie = false;
        foreach (var (code, words) in candidates)
        {
            var hits = tokens.Count(words.Contains);
            if (hits > bestHits) { best = code; bestHits = hits; tie = false; }
            else if (hits == bestHits && hits > 0 && code != best) tie = true;
        }

        // One marker word is enough for a short message; a long one needs two, because
        // in a long sentence a single shared word ("no", "la", "come") proves nothing.
        if (tie || bestHits == 0) return null;
        if (bestHits == 1 && tokens.Length > 5) return null;
        return best;
    }

    /// <summary>
    /// Function words — the cheap, high-frequency ones a person cannot avoid using. Kept
    /// in the same lazy holder pattern as the rest, for the same static-initialisation
    /// reason described in the question layer.
    /// </summary>
    private static class Marks
    {
        static Marks() { }

        private static string[] F(params string[] words) => words.Select(Fold).ToArray();

        internal static readonly string[] Persian = F(
            "است", "هست", "هستند", "میخوام", "میخواهم", "چقدر", "چند", "کجا", "کدوم", "کدام",
            "الان", "امروز", "دیروز", "برای", "رو", "تا", "بده", "بگو", "نشون", "دارم", "داریم",
            "میشه", "چطور", "یه", "خیلی", "من", "ما");

        internal static readonly string[] Arabic = F(
            "كم", "وين", "اين", "هل", "ابغى", "ابي", "عايز", "عاوز", "شو", "ايش", "وش",
            "الان", "اليوم", "امس", "على", "في", "من", "الى", "هذا", "هذه", "عندي", "عندنا",
            "ممكن", "بكم", "متى", "ليش");

        internal static readonly string[] Urdu = F(
            "کتنے", "کتنا", "کہاں", "کون", "ہے", "ہیں", "کیا", "مجھے", "میرا", "آج", "کل",
            "چاہیے", "کریں", "دکھائیں", "کیسے");

        internal static readonly string[] Turkish = F(
            "ne", "kac", "kaç", "nerede", "var", "yok", "bugun", "bugün", "dun", "dün",
            "icin", "için", "bir", "ve", "ile", "musteri", "müşteri", "siparis", "sipariş",
            "gunluk", "günlük", "nasil", "nasıl");

        internal static readonly string[] Spanish = F(
            "cuanto", "cuánto", "cuantos", "cuántos", "donde", "dónde", "hoy", "ayer",
            "para", "que", "qué", "los", "las", "una", "del", "cuales", "cuáles", "tengo",
            "cuenta", "ventas", "hay");

        internal static readonly string[] French = F(
            "combien", "ou", "où", "aujourdhui", "hier", "pour", "les", "des", "une",
            "quel", "quelle", "quels", "montre", "affiche", "jai", "ventes", "commandes");

        internal static readonly string[] German = F(
            "wie", "wieviel", "wo", "heute", "gestern", "fur", "für", "der", "die", "das",
            "ein", "eine", "zeig", "zeige", "meine", "umsatz", "bestellungen", "viele");

        internal static readonly string[] Italian = F(
            "quanto", "quanti", "dove", "oggi", "ieri", "per", "gli", "una", "del",
            "quale", "mostra", "vendite", "ordini", "sono");

        internal static readonly string[] Portuguese = F(
            "quanto", "quantos", "onde", "hoje", "ontem", "para", "uma", "dos", "das",
            "qual", "mostra", "mostre", "vendas", "pedidos", "tenho");

        internal static readonly string[] English = F(
            "how", "what", "where", "which", "who", "today", "yesterday", "show", "many",
            "much", "the", "and", "for", "my", "is", "are", "do", "did", "have", "sales",
            "orders", "give");
    }
}
