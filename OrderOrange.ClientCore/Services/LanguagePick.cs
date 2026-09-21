using System.Globalization;
using System.Text;

namespace OrderOrange.ClientCore.Services;

/// <summary>
/// "Switch to Arabic." — reading a request to change the app's language out of a sentence.
///
/// <para>Both assistants need this and they live in different projects, so it sits in
/// ClientCore where the customer app and the partner portal can each reach it.</para>
///
/// <para>A language can be named three ways and people use all of them: in its own tongue
/// («العربية»), in the speaker's tongue («عربی» from a Persian speaker), or in English
/// ("arabic"). The table below carries all three for every language the app speaks,
/// because the one thing a person cannot do is name a language in a language they do not
/// yet have the interface in.</para>
/// </summary>
public static class LanguagePick
{
    /// <summary>The word "language" itself — what turns a name into an instruction.</summary>
    private static readonly string[] LanguageWords =
    [
        "language", "lang", "locale",
        "زبان", "زبون",
        "لغة", "اللغة", "لغه",
        "dil", "lisan",
        "язык", "языке",
        "idioma", "lengua", "langue", "sprache", "lingua", "idioma",
        "زبان", "भाषा", "语言", "語言", "言語",
    ];

    /// <summary>
    /// Verbs that make it a request rather than a mention.
    ///
    /// Deliberately only the strong ones. "to", "in", "add", "make", "use" and Persian
    /// «به»/«کن» were here first, and they turned "add a turkish coffee to table 3" into
    /// a request to switch the portal to Turkish — a language name is an ordinary word,
    /// and these are the words that surround every other sentence too.
    /// </summary>
    private static readonly string[] ChangeWords =
    [
        "change", "switch", "speak", "talk", "set",
        "عوض", "تغییر", "بذار", "بگو",
        "غير", "غيّر", "بدل", "حول", "اجعل", "تكلم",
        "degistir", "değiştir", "yap", "konus", "konuş",
        "смени", "измени", "переключ",
        "cambia", "cambiar", "mets", "parle", "andere", "ändere", "sprich",
        "parla", "muda", "fala",
    ];

    /// <summary>
    /// Every language the app speaks, under every name it is likely to be called. The
    /// first entry of each row is the code; the rest are what a person might type.
    /// </summary>
    private static readonly (string Code, string[] Names)[] Table =
    [
        ("en", ["english", "eng", "انگلیسی", "انگلیس", "الانجليزية", "الإنجليزية", "انجليزي", "إنجليزي",
                "ingilizce", "английский", "ingles", "inglés", "anglais", "englisch", "inglese",
                "انگریزی", "अंग्रेज़ी", "अंग्रेजी", "英语", "英文", "英語"]),

        ("ar", ["arabic", "arab", "العربية", "عربية", "عربي", "العربي", "عربی",
                "arapca", "arapça", "арабский", "arabe", "árabe", "arabisch", "arabo",
                "عربی", "अरबी", "阿拉伯语", "アラビア語"]),

        ("fa", ["persian", "farsi", "فارسی", "پارسی", "الفارسية", "فارسي",
                "farsca", "farsça", "персидский", "фарси", "persa", "perse", "persisch", "persiano",
                "فارسی", "फ़ारसी", "波斯语", "ペルシア語"]),

        ("ur", ["urdu", "اردو", "الأردية", "الاردية", "اردوی",
                "urduca", "urduca", "урду", "urdu",
                "اردو", "उर्दू", "乌尔都语", "ウルドゥー語"]),

        ("hi", ["hindi", "هندی", "الهندية", "هندي",
                "hintce", "hintçe", "хинди", "hindi",
                "ہندی", "हिंदी", "हिन्दी", "印地语", "ヒンディー語"]),

        ("tr", ["turkish", "turkce", "türkçe", "ترکی", "التركية", "تركي",
                "турецкий", "turco", "turc", "turkisch", "türkisch",
                "ترکی", "तुर्की", "土耳其语", "トルコ語"]),

        ("fr", ["french", "francais", "français", "فرانسوی", "الفرنسية", "فرنسي",
                "fransizca", "fransızca", "французский", "frances", "francés", "franzosisch",
                "französisch", "francese", "فرانسیسی", "फ़्रेंच", "法语", "フランス語"]),

        ("es", ["spanish", "espanol", "español", "اسپانیایی", "الإسبانية", "الاسبانية", "اسباني",
                "ispanyolca", "İspanyolca", "испанский", "espagnol", "spanisch", "spagnolo",
                "ہسپانوی", "स्पेनिश", "西班牙语", "スペイン語"]),

        ("de", ["german", "deutsch", "آلمانی", "الألمانية", "الالمانية", "الماني",
                "almanca", "немецкий", "aleman", "alemán", "allemand", "tedesco",
                "جرمن", "जर्मन", "德语", "ドイツ語"]),

        ("ru", ["russian", "russkiy", "русский", "روسی", "الروسية", "روسي",
                "rusca", "rusça", "ruso", "russe", "russisch", "russo",
                "روسی", "रूसी", "俄语", "ロシア語"]),

        ("it", ["italian", "italiano", "ایتالیایی", "الإيطالية", "الايطالية", "ايطالي",
                "italyanca", "итальянский", "italien", "italienisch",
                "اطالوی", "इतालवी", "意大利语", "イタリア語"]),

        ("pt", ["portuguese", "portugues", "português", "پرتغالی", "البرتغالية", "برتغالي",
                "portekizce", "португальский", "portugues", "portugais", "portugiesisch",
                "portoghese", "پرتگالی", "पुर्तगाली", "葡萄牙语", "ポルトガル語"]),

        ("zh", ["chinese", "mandarin", "چینی", "الصينية", "صيني",
                "cince", "çince", "китайский", "chino", "chinois", "chinesisch", "cinese",
                "中文", "汉语", "漢語", "中国語", "چینی", "चीनी"]),

        ("ja", ["japanese", "ژاپنی", "اليابانية", "ياباني",
                "japonca", "японский", "japones", "japonés", "japonais", "japanisch",
                "giapponese", "日本語", "日语", "جاپانی", "जापानी"]),
    ];

    /// <summary>
    /// The locale this message is asking for, or null when it is not asking for one.
    ///
    /// <para>Strict on purpose. A bare language name only counts when it is the ENTIRE
    /// message — otherwise "the french fries" would switch the portal to French.</para>
    /// </summary>
    public static string? Resolve(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var tokens = Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;

        var named = Table.FirstOrDefault(row => row.Names.Any(n => tokens.Contains(Fold(n))));
        if (named.Code is null) return null;

        // A language word ("change the LANGUAGE to arabic") or a verb ("switch to arabic")
        // makes it an instruction however long the sentence is.
        var asksForIt = tokens.Any(t => LanguageWords.Any(w => t == Fold(w)))
                        || tokens.Any(t => ChangeWords.Any(w => t == Fold(w)));
        if (asksForIt) return named.Code;

        // Otherwise the name has to BE the message. One or two words: "arabic", «العربية».
        var meaningful = tokens.Where(t => t.Length > 1).ToArray();
        return meaningful.Length <= 2 ? named.Code : null;
    }

    /// <summary>How this language calls itself — for confirming the change in it.</summary>
    public static string NativeName(string code) =>
        LanguageService.SupportedLanguages.FirstOrDefault(l => l.Code == code)?.NativeName ?? code;

    /// <summary>
    /// Case, accents and the Arabic/Persian letter variants folded away, so «عربى» and
    /// «عربي» are one word and "Français" matches "francais". Kept local rather than
    /// borrowed from either bot's parser: this has to behave identically in both apps.
    /// </summary>
    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var raw in s.ToLowerInvariant().Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) is UnicodeCategory.NonSpacingMark) continue;
            var c = raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' or 'ی' => 'ي',
                'ک' => 'ك',
                'ة' => 'ه',
                'ۀ' or 'ہ' or 'ھ' => 'ه',
                'ے' => 'ي',
                'ı' => 'i',
                _ => raw,
            };
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
