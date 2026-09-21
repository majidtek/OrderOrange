using System.Text;
using System.Text.RegularExpressions;

namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Localizes the generated store text at render time.
///
/// The five million bulk stores are named and described from templates —
/// "Golden Samosa House - Bawshar", "The home of samosa lovers in Bawshar, Muscat" —
/// so instead of translating five million rows, we take the English apart and rebuild
/// it from a small translated vocabulary: ~170 name words, ~70 places and a handful of
/// sentence patterns. Anything we don't recognise falls through unchanged.
///
/// A REAL shop must not go through any of that. It named itself, in its own languages,
/// and those names are on the card — so the overloads that take them return the owner's
/// wording and never touch the vocabulary. Rebuilding "Saffron Catering" word by word
/// produced «زعفران Catering», and "Amiran Cafe &amp; Restaurant" became
/// «Amiran کافه و رستوران», while the correct Persian name sat unused in the database.
/// </summary>
public static class StoreText
{
    private const string Sep = " - ";

    // The chatbot answers in the language the user typed, which is not always the UI
    // language — hence an explicit-locale overload beside every convenience one.
    public static string Name(string? name, LanguageService lang) => Name(name, lang.Locale);

    /// <summary>
    /// The shop's own name in the reader's language when the owner wrote one, and only
    /// otherwise the rebuilt generated name. Prefer this overload wherever a card,
    /// detail or info DTO is in hand — those all carry the owner's names.
    /// </summary>
    public static string Name(string? name, Dictionary<string, string>? names, LanguageService lang) =>
        Name(name, names, lang.Locale);

    /// <summary>
    /// The cuisine line under a store's name. One type reads as before; a store that
    /// carries several shows them all — "ایرانی · عربی و مشویات" — each localized on
    /// its own, which a pre-joined string could never be.
    /// </summary>
    public static string Cuisines(string cuisine, IReadOnlyList<string>? all, LanguageService lang) =>
        all is { Count: > 0 }
            ? string.Join(" · ", all.Select(c => lang.Data("cuisine", c)))
            : lang.Data("cuisine", cuisine);

    public static string Name(string? name, Dictionary<string, string>? names, string locale)
    {
        if (names is not null &&
            names.TryGetValue(locale, out var own) && !string.IsNullOrWhiteSpace(own))
            return own;
        return Name(name, locale);
    }
    public static string Description(string? text, LanguageService lang) => Description(text, lang.Locale);
    public static string Area(string? area, LanguageService lang) => Area(area, lang.Locale);

    /// <summary>"Golden Samosa House - Bawshar" → the same name in the reader's language.</summary>
    public static string Name(string? name, string locale)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        if (IsSource(locale)) return name;

        var dash = name.IndexOf(Sep, StringComparison.Ordinal);
        var head = dash > 0 ? name[..dash] : name;
        var place = dash > 0 ? name[(dash + Sep.Length)..] : null;

        var localized = Phrase(head, locale);
        if (localized is null) return name;                       // not one of ours — leave it be

        return place is null ? localized : $"{localized}{Sep}{Place(place, locale)}";
    }

    /// <summary>The generated one-liner under a store name.</summary>
    public static string Description(string? text, string locale)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (IsSource(locale)) return text;

        // The whole-sentence store-type blurbs (grocery, electronics, flowers, pharmacy).
        var whole = Look($"sd.{Slug(text)}", locale);
        if (whole is not null) return whole;

        foreach (var (rx, key) in Patterns)
        {
            var m = rx.Match(text);
            if (!m.Success) continue;
            var template = Look(key, locale);
            if (template is null) return text;
            return Fill(template,
                Word(m.Groups["d"].Value, locale),
                Place(m.Groups["p"].Value, locale),
                m.Groups["p2"].Success ? Place(m.Groups["p2"].Value, locale) : "");
        }
        return text;
    }

    /// <summary>"Bawshar, Muscat" → "بوشر، مسقط". Also handles a bare town or region.</summary>
    public static string Area(string? area, string locale)
    {
        if (string.IsNullOrWhiteSpace(area)) return "";
        if (IsSource(locale)) return area;
        var comma = area.IndexOf(',');
        if (comma < 0) return Place(area.Trim(), locale);
        var town = Place(area[..comma].Trim(), locale);
        var region = Place(area[(comma + 1)..].Trim(), locale);
        // The separator carries its own trailing space — CJK punctuation already includes one.
        return $"{town}{Look("sp.join", locale) ?? ", "}{region}";
    }

    // ───────────────────────────── internals ─────────────────────────────

    private static bool IsSource(string locale) => locale == "en";

    private static readonly (Regex Rx, string Key)[] Patterns =
    [
        (Rx(@"^The home of (?<d>[\w'-]+) lovers in (?<p>[^,]+), (?<p2>[^.]+)\.$"), "sd.homeOf"),
        (Rx(@"^A modern taste of (?<d>[\w'-]+) from the heart of (?<p>[^.]+)\.$"), "sd.modern"),
        (Rx(@"^Authentic (?<d>[\w'-]+) made fresh daily in (?<p>[^.]+)\.$"), "sd.authentic"),
        (Rx(@"^Family recipes and the best (?<d>[\w'-]+) in (?<p>[^.]+)\.$"), "sd.family"),
        (Rx(@"^(?<d>[\w'-]+) done right . fast delivery across (?<p>[^.]+)\.$"), "sd.doneRight"),
    ];

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.CultureInvariant);

    /// <summary>A translation, or null when the key isn't in the files.</summary>
    private static string? Look(string key, string locale)
    {
        var value = LanguageService.Translate(locale, key);
        return value == key ? null : value;
    }

    /// <summary>
    /// Rebuilds "&lt;adjective&gt; &lt;dish…&gt; &lt;venue&gt;" in the target language's word
    /// order. Returns null when not a single word is known, which means it isn't a
    /// generated name and must be shown exactly as the owner typed it.
    /// </summary>
    private static string? Phrase(string head, string locale)
    {
        var words = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;

        var parts = new string[words.Length];
        var known = 0;
        for (var i = 0; i < words.Length; i++)
        {
            parts[i] = Word(words[i], locale);
            if (parts[i] != words[i]) known++;
        }
        if (known == 0) return null;

        return words.Length switch
        {
            1 => parts[0],
            2 => Fill(Look("sn.pair", locale) ?? "{0} {1}", parts[0], parts[1], ""),
            _ => Fill(Look("sn.full", locale) ?? "{0} {1} {2}",
                    parts[0],                                        // adjective
                    string.Join(Glue(locale), parts[1..^1]),         // dish (may be several words)
                    parts[^1]),                                      // venue
        };
    }

    /// <summary>CJK writes compounds without spaces; everyone else uses one.</summary>
    private static string Glue(string locale) => locale is "zh" or "ja" ? "" : " ";

    private static string Word(string word, string locale) =>
        word is "&" or "—" or "-" ? word : Look($"sw.{Slug(word)}", locale) ?? word;

    private static string Place(string place, string locale) =>
        Look($"sp.{Slug(place)}", locale) ?? place;

    /// <summary>"Al Khuwair" → "alkhuwair"; keeps keys flat, stable and case-insensitive.</summary>
    private static string Slug(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>
    /// Fills {0}/{1}/{2} without string.Format — a translator's stray brace should
    /// never throw inside a render loop.
    /// </summary>
    private static string Fill(string template, string a, string b, string c) =>
        template.Replace("{0}", a).Replace("{1}", b).Replace("{2}", c);
}
