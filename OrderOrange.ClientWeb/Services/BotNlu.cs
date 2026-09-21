using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OrderOrange.ClientWeb.Services;

/// <summary>
/// The chatbot's offline language brain. Completely self-contained — no network,
/// no model files. Understands the 14 app languages plus romanized chat forms:
/// Unicode folding (diacritics, Arabic/Persian/Urdu letter variants, all digit
/// scripts), Damerau-Levenshtein typo tolerance, trigram rescue for heavily
/// mangled words, CJK substring matching, intent detection, quantity / order
/// number extraction and script-based language identification.
/// </summary>
public static partial class BotNlu
{
    // ───────────────────────────── Folding ─────────────────────────────

    /// <summary>
    /// Aggressive normalization: lowercase, compatibility-decompose (full-width
    /// forms, ligatures), strip accents/tashkeel, fold Arabic-script letter
    /// variants (Persian/Urdu included), fold every digit script to ASCII,
    /// non-alphanumerics to spaces. CJK ideographs/kana pass through.
    /// </summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.ToLowerInvariant().Normalize(NormalizationForm.FormKD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(raw);
            // Strip accents and tashkeel — EXCEPT Devanagari, where vowel signs
            // are real letters (stripping them collapses हाँ and है into the same
            // skeleton). Only the nukta dot is dropped, and candrabindu is
            // normalized to anusvara so कहाँ and कहां match.
            var isDevanagari = raw >= 0x0900 && raw <= 0x097F;
            if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                if (!isDevanagari) continue;
                if (raw == '़') continue;                 // nukta (ज़ → ज)
                sb.Append(raw == 'ँ' ? 'ं' : raw);   // ँ → ं
                continue;
            }
            if (raw == 'ـ') continue;                              // Arabic tatweel

            var c = raw switch
            {
                // Arabic letter variants
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                // Persian variants
                'ک' => 'ك',
                'ی' => 'ي',
                // Urdu variants
                'ہ' or 'ھ' or 'ۂ' or 'ۃ' => 'ه',
                'ے' => 'ي',
                'ں' => 'ن',
                // Latin leftovers FormKD doesn't decompose
                'ı' => 'i',
                'ø' => 'o',
                'æ' => 'a',
                'ð' => 'd',
                'þ' => 't',
                'ł' => 'l',
                'đ' => 'd',
                _ => raw
            };

            if (c == 'ß') { sb.Append("ss"); continue; }

            // Every digit script → ASCII (Arabic-Indic, Extended, Devanagari, …).
            if (char.IsDigit(c))
            {
                var value = CharUnicodeInfo.GetDecimalDigitValue(c);
                sb.Append(value >= 0 ? (char)('0' + value) : c);
                continue;
            }

            // Lowercase again: decomposition can mint new capitals ("İ" → "I" + dot).
            c = char.ToLowerInvariant(c);

            var next = char.IsLetterOrDigit(c) ? c : ' ';
            // Cap letter runs at two: chat stretching ("yesss", "طلبببي") becomes
            // an ordinary one-edit typo instead of an unmatchable word.
            if (next != ' ' && sb.Length >= 2 && sb[^1] == next && sb[^2] == next) continue;
            sb.Append(next);
        }

        // Collapse runs of spaces.
        var folded = sb.ToString();
        return string.Join(' ', folded.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static string[] Tokens(string text) =>
        Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>For the offline phrase miner: is this folded token a filler?</summary>
    public static bool IsFiller(string foldedToken) => FoldedFillers.Contains(foldedToken);

    /// <summary>For the offline phrase miner: is this folded token a dish word?</summary>
    public static bool IsFoodWord(string foldedToken) => FoodStopWords.Contains(foldedToken);

    public static bool HasCjk(string text) => text.Any(IsCjkChar);

    private static bool IsCjkChar(char c) =>
        (c >= 0x3040 && c <= 0x30FF) ||   // hiragana + katakana
        (c >= 0x4E00 && c <= 0x9FFF) ||   // CJK unified ideographs
        (c >= 0x3400 && c <= 0x4DBF);

    // ─────────────────────── Edit distance / similarity ───────────────────────

    /// <summary>Damerau-Levenshtein with early exit past <paramref name="cap"/>.</summary>
    public static int EditDistance(string a, string b, int cap)
    {
        if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            var rowBest = int.MaxValue;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                rowBest = Math.Min(rowBest, d[i, j]);
            }
            if (rowBest > cap) return cap + 1;
        }
        return d[a.Length, b.Length];
    }

    /// <summary>Typo budget for a word of this length.</summary>
    public static int CapFor(int length) => length switch
    {
        <= 2 => 0,
        <= 4 => 1,
        <= 7 => 2,
        _ => 3
    };

    /// <summary>Character-trigram Jaccard similarity — last resort for mangled words.</summary>
    public static double Trigram(string a, string b)
    {
        var ga = Grams(a);
        var gb = Grams(b);
        if (ga.Count == 0 || gb.Count == 0) return 0;
        var overlap = ga.Intersect(gb).Count();
        return (double)overlap / (ga.Count + gb.Count - overlap);

        static HashSet<string> Grams(string s)
        {
            var padded = $"  {s} ";
            var set = new HashSet<string>();
            for (var i = 0; i + 3 <= padded.Length; i++) set.Add(padded.Substring(i, 3));
            return set;
        }
    }

    /// <summary>
    /// 0–1: how well a (folded) user token matches a (folded) vocabulary word.
    /// Exact &gt; prefix &gt; small typo &gt; trigram resemblance.
    /// <paramref name="strict"/> is used for intent phrases/keywords, where the
    /// vocabulary is thousands of words across 14 languages and loose matching
    /// of short words floods everything with cross-language noise: prefixes
    /// need 5+ chars, short words allow only insert/delete/transposition typos
    /// (never a straight letter swap), and edit caps are tighter.
    /// </summary>
    public static double TokenScore(string userToken, string word, bool strict = false)
    {
        if (userToken.Length == 0 || word.Length == 0) return 0;
        if (userToken == word) return 1;

        // Prefix — covers search-as-you-type and agglutinative suffixes
        // ("siparişimi" → "sipariş", "bestellungen" → "bestellung"). The shorter
        // side must cover ≥60% of the longer one, otherwise "can" would count as
        // a prefix of "cancel" and hijack ordinary sentences.
        var minPrefix = strict ? 5 : 3;
        if (userToken.Length >= minPrefix && word.Length >= minPrefix)
        {
            var shorter = Math.Min(userToken.Length, word.Length);
            var longer = Math.Max(userToken.Length, word.Length);
            if (shorter >= longer * 0.6 &&
                (userToken.StartsWith(word, StringComparison.Ordinal) ||
                 word.StartsWith(userToken, StringComparison.Ordinal)))
                return 0.85;
        }

        // In strict mode nothing shorter than 3 chars matches fuzzily at all —
        // "3"→"3q", "me"→"ve", "ki"→"oki" are different words, not typos.
        if (strict && (userToken.Length < 3 || word.Length < 3)) return 0;

        var cap = strict
            ? Math.Min(userToken.Length, word.Length) switch { <= 6 => 1, <= 9 => 2, _ => 3 }
            : Math.Min(CapFor(word.Length), CapFor(userToken.Length));
        if (cap > 0)
        {
            var d = EditDistance(userToken, word, cap);
            if (strict && d == 1)
            {
                // Short-word guards: "روك"→"كوك", "the"→"thx", "ago"→"pago",
                // "hepsi"→"pepsi" are different words, not typos. Real typos
                // are doubled/dropped letters mid-word, swaps, or suffix
                // inflections ("करना"→"करनी").
                var sameLen = userToken.Length == word.Length;
                if (sameLen && word.Length <= 5 &&
                    !IsTransposition(userToken, word) &&
                    !(word.Length >= 4 && LastCharOnlyDiff(userToken, word)))
                    return 0;
                if (!sameLen && Math.Min(userToken.Length, word.Length) <= 3)
                {
                    var (shorter, longer) = userToken.Length < word.Length
                        ? (userToken, word) : (word, userToken);
                    // "ago"→"pago" is a different word — but only for Latin:
                    // Arabic verbs legitimately conjugate with prefixes (كرر→اكرر).
                    if (longer[0] < 128 && longer.EndsWith(shorter, StringComparison.Ordinal)) return 0;
                }
            }
            // Strict scoring is harsher per edit: a two-edit word alone must
            // not clear the intent threshold (0.72).
            if (d <= cap) return 1 - d * (strict ? 0.16 : 0.13);
        }

        if (!strict && word.Length >= 5 && userToken.Length >= 5)
        {
            var sim = Trigram(userToken, word);
            if (sim >= 0.5) return 0.45 + sim * 0.3;
        }
        return 0;
    }

    private static bool LastCharOnlyDiff(string a, string b)
    {
        for (var i = 0; i < a.Length - 1; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>Same length, differing only by two adjacent swapped letters.</summary>
    private static bool IsTransposition(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var first = -1;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            if (first < 0) { first = i; continue; }
            // second difference must be adjacent and swapped, rest equal
            if (i != first + 1 || a[first] != b[i] || a[i] != b[first]) return false;
            for (var j = i + 1; j < a.Length; j++)
                if (a[j] != b[j]) return false;
            return true;
        }
        return false;
    }

    // ─────────────────────────── Phrase matching ───────────────────────────

    /// <summary>
    /// Does this phrase appear (fuzzily) in the user's tokens?
    /// Returns (Quality, Words, Indices): Quality is the average per-word match
    /// 0–1 (0 = no match), Words the phrase length, Indices which user tokens
    /// were consumed (for subsumption comparisons — "i want cancel" covering a
    /// superset of "i want" must beat it). Phrase words must land on DISTINCT
    /// user tokens, CLOSE TOGETHER — in a 15-word sentence, "order … again"
    /// nine words apart is a coincidence, not the phrase "order again".
    /// </summary>
    public static (double Quality, int Words, HashSet<int>? Indices) PhraseScore(
        string[] userTokens, string foldedUserText, string foldedPhrase)
    {
        if (foldedPhrase.Length == 0) return (0, 0, null);

        // CJK phrases have no word boundaries — match by containment, then by
        // fuzzy substring (one edit) for phrases of 3+ chars.
        if (HasCjk(foldedPhrase))
        {
            var compact = foldedUserText.Replace(" ", "");
            var phrase = foldedPhrase.Replace(" ", "");
            if (compact.Contains(phrase, StringComparison.Ordinal))
                return (Math.Min(1, 0.92 + phrase.Length * 0.015), Math.Max(1, phrase.Length / 2), null);
            if (phrase.Length >= 3 && FuzzyContains(compact, phrase, 1))
                return (0.8, Math.Max(1, phrase.Length / 2), null);
            return (0, 0, null);
        }

        var phraseTokens = foldedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (phraseTokens.Length == 0 || userTokens.Length == 0) return (0, 0, null);
        if (phraseTokens.Length > userTokens.Length) return (0, 0, null);

        var used = new bool[userTokens.Length];
        var indices = new HashSet<int>();
        double total = 0;
        foreach (var pt in phraseTokens)
        {
            double best = 0;
            var bestIdx = -1;
            for (var i = 0; i < userTokens.Length; i++)
            {
                if (used[i]) continue;
                var s = TokenScore(userTokens[i], pt, strict: true);
                if (s > best) { best = s; bestIdx = i; }
                if (best >= 1) break;
            }
            if (best < 0.65) return (0, 0, null);   // one missing word sinks the phrase
            used[bestIdx] = true;
            indices.Add(bestIdx);
            total += best;
        }

        // Proximity: the matched words must form a tight cluster.
        if (indices.Max() - indices.Min() + 1 > phraseTokens.Length + 2) return (0, 0, null);

        return (total / phraseTokens.Length, phraseTokens.Length, indices);
    }

    /// <summary>Sliding-window edit-distance containment for unsegmented text.</summary>
    public static bool FuzzyContains(string text, string phrase, int maxEdits)
    {
        if (text.Length < phrase.Length - maxEdits) return false;
        for (var start = 0; start < text.Length; start++)
        {
            // Windows slightly shorter and longer than the phrase, so both
            // deletions and insertions inside the window can be absorbed.
            for (var len = phrase.Length - maxEdits; len <= phrase.Length + maxEdits; len++)
            {
                if (len <= 0 || start + len > text.Length) continue;
                if (EditDistance(text.Substring(start, len), phrase, maxEdits) <= maxEdits)
                    return true;
            }
        }
        return false;
    }

    // ─────────────────────────── Intent detection ───────────────────────────

    /// <summary>
    /// Dish words the lexicon uses inside example phrases ("ابغى برجر",
    /// "pizza food") — they must NEVER be treated as ordering noise or as
    /// standalone intent phrases, or "ابغى برجر" would lose the burger itself.
    /// Declared before the folded caches: static init runs in textual order.
    /// </summary>
    private static readonly HashSet<string> FoodStopWords = new[]
    {
        "pizza", "pizzas", "burger", "burgers", "hamburger", "cheeseburger", "shawarma",
        "shawarmas", "sushi", "biryani", "kebab", "kabab", "doner", "fries", "cola", "coke",
        "soda", "coffee", "tea", "sandwich", "sandwiches", "salad", "pasta", "curry", "ramen",
        "taco", "tacos", "falafel", "hummus", "samosa", "chai",
        "пицца", "пиццу", "пиццы", "бургер", "бургеры", "шаурма", "шаурму", "суши",
        "кебаб", "картошка", "кофе", "чай",
        "بيتزا", "برجر", "برقر", "همبرجر", "شاورما", "سوشي", "برياني", "كباب", "بطاطس",
        "كولا", "بيبسي", "قهوة", "شاي", "فلافل", "حمص", "ساندويش", "ساندويتش", "سمبوسة",
        "پیتزا", "ساندویچ", "چلوکباب", "بریانی",
        "पिज्जा", "बर्गर", "शावरमा", "बिरयानी", "समोसा", "चाय", "कबाब",
        // Drinks and staples. "ماء" sits one edit from "مساء" (as in مساء الخير) and
        // "مياه" from Arabic tracking words, so a thirsty customer was greeted instead
        // of being shown water. A bare product word is a SEARCH, never a command.
        "water", "juice", "milk", "bread", "rice", "chicken", "meat", "cheese", "eggs",
        "ماء", "مياه", "ماي", "عصير", "حليب", "لبن", "خبز", "رز", "أرز", "دجاج", "لحم",
        "جبن", "بيض", "ماء معدني", "عصائر",
        "آب", "شیر", "نان", "برنج", "مرغ", "گوشت", "پنیر", "تخم مرغ", "نوشابه", "آبمیوه",
        // Iranian dishes (Siraj, Nikan) — a bare dish word is a SEARCH, never a command.
        "کباب", "کوبیده", "koobideh", "kubideh", "جوجه", "jujeh", "joojeh", "برگ", "barg",
        "سلطانی", "soltani", "وزیری", "vaziri", "بختیاری", "bakhtiari", "شیشلیک", "shishlik",
        "قیمه", "gheimeh", "فسنجان", "fesenjan", "قورمه", "ghormeh", "چلو", "chelo", "chelokabab",
        "زرشک پلو", "zereshk", "باقالی پلو", "دیزی", "dizi", "abgoosht", "آبگوشت", "میرزا قاسمی",
        "بادمجان", "bademjan", "ته چین", "tahchin", "دوغ", "doogh", "فالوده", "faloodeh",
        "سنگک", "sangak", "بربری", "barbari", "لواش", "lavash",
        "پانی", "دودھ", "روٹی", "چاول", "گوشت", "انڈے", "جوس",
        "вода", "сок", "молоко", "хлеб", "рис", "курица", "мясо", "сыр",
        "पानी", "जूस", "दूध", "रोटी", "चावल", "मुर्ग़", "पनीर",
        "su", "meyve suyu", "süt", "ekmek", "pilav", "tavuk",
        "agua", "zumo", "leche", "pan", "arroz", "pollo", "queso",
        "eau", "jus", "lait", "pain", "riz", "poulet", "fromage",
        "wasser", "saft", "milch", "brot", "reis", "hähnchen", "käse",
        "acqua", "succo", "latte", "pane", "riso", "pollo", "formaggio",
        "água", "suco", "leite", "pão", "arroz", "frango", "queijo",
        "水", "果汁", "牛奶", "面包", "米饭", "鸡肉", "お水", "ジュース", "牛乳", "パン",
    }.Select(Fold).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> FoldedFillers = BuildFoldedFillers();

    private static HashSet<string> BuildFoldedFillers()
    {
        // A yes/no word must never be a filler ("sì", "ok") — a generated
        // filler list occasionally includes them, and dropping them would make
        // confirmations invisible.
        var protectedWords = BotLexicon.Phrases[BotLexicon.Intent.Yes]
            .Concat(BotLexicon.Phrases[BotLexicon.Intent.No])
            .Select(Fold)
            .Where(p => !p.Contains(' '))
            .ToHashSet(StringComparer.Ordinal);

        // Fillers are stored word-by-word: "لو سمحت" must also suppress its
        // parts, because user tokens are matched individually.
        return BotLexicon.Fillers.Concat(BotLexicon.GeneratedFillers)
            .Select(Fold)
            .SelectMany(f => HasCjk(f) ? [f] : f.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append(f))
            .Where(f => f.Length > 0 && !protectedWords.Contains(f))
            .ToHashSet();
    }
    private static readonly Dictionary<string, int> FoldedNumberWords = BuildFoldedNumbers();
    private static readonly List<(BotLexicon.Intent Intent, string Folded)> FoldedPhrases = BuildFoldedPhrases();
    private static readonly List<(BotLexicon.Intent Intent, string Folded)> FoldedKeywords = BuildFoldedKeywords();

    private static List<(BotLexicon.Intent, string)> BuildFoldedPhrases()
    {
        // Fold and strip filler words out of every phrase — user tokens are
        // filler-filtered at match time, so both sides must be comparable.
        static string Prep(string phrase)
        {
            var folded = Fold(phrase);
            if (HasCjk(folded)) return folded.Replace(" ", "");
            var kept = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => !FoldedFillers.Contains(t))
                .ToArray();
            return kept.Length == 0 ? folded : string.Join(' ', kept);
        }

        // Ban comparison uses the same Prep as phrases, so "non lo voglio più"
        // still matches after its filler "lo" is stripped.
        var banned = BotLexicon.BannedGeneratedPhrases.Select(Prep).ToHashSet(StringComparer.Ordinal);

        // Hand-written tiers (base + patches) always win; machine-expanded
        // entries are added on top, and a generated phrase claimed by two
        // different intents is ambiguous and dropped.
        var byPhrase = new Dictionary<string, BotLexicon.Intent>(StringComparer.Ordinal);
        var baseSet = new HashSet<string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (source, isBase) in new[]
                 {
                     (BotLexicon.Phrases, true),
                     (BotLexicon.PatchPhrases, true),
                     (BotLexicon.GeneratedPhrases, false),
                     (BotLexicon.MinedPhrases, false),
                 })
            foreach (var (intent, phrases) in source)
                foreach (var raw in phrases)
                {
                    var folded = Prep(raw);
                    if (folded.Length < 2) continue;
                    if (!isBase && (banned.Contains(folded) || FoldedFillers.Contains(folded))) continue;
                    // A bare dish word ("cola") is a food query, never an intent.
                    if (!isBase && !folded.Contains(' ') && FoodStopWords.Contains(folded)) continue;
                    if (byPhrase.TryGetValue(folded, out var owner))
                    {
                        if (owner != intent && !baseSet.Contains(folded)) ambiguous.Add(folded);
                        continue;
                    }
                    byPhrase[folded] = intent;
                    if (isBase) baseSet.Add(folded);
                }

        // Genericity demotion: a single word (or a 2-3-char CJK string) that
        // shows up inside phrases of 3+ different intents ("order", "cart",
        // "注文", "商品") is meaningless on its own — as a standalone phrase it
        // would hijack every sentence that mentions it. Multi-word phrases
        // containing it stay; the keyword layer still gives the bare word a
        // sensible default.
        var wordIntents = new Dictionary<string, HashSet<BotLexicon.Intent>>(StringComparer.Ordinal);
        foreach (var (folded, intent) in byPhrase)
        {
            if (HasCjk(folded)) continue;
            foreach (var token in folded.Split(' '))
            {
                if (!wordIntents.TryGetValue(token, out var set)) wordIntents[token] = set = [];
                set.Add(intent);
            }
        }
        var cjkPhrases = byPhrase.Where(p => HasCjk(p.Key)).ToList();

        bool TooGeneric(string folded, BotLexicon.Intent intent)
        {
            if (intent is BotLexicon.Intent.Yes or BotLexicon.Intent.No) return false;
            if (HasCjk(folded))
            {
                if (folded.Length > 3) return false;
                return cjkPhrases
                    .Where(p => p.Key.Length > folded.Length && p.Key.Contains(folded, StringComparison.Ordinal))
                    .Select(p => p.Value)
                    .Distinct()
                    .Count() >= 3;
            }
            return !folded.Contains(' ') &&
                   wordIntents.TryGetValue(folded, out var set) && set.Count >= 3;
        }

        return byPhrase
            .Where(p => !ambiguous.Contains(p.Key) && !TooGeneric(p.Key, p.Value))
            .Select(p => (p.Value, p.Key))
            .ToList();
    }

    private static List<(BotLexicon.Intent, string)> BuildFoldedKeywords()
    {
        // Keywords must stay unambiguous too — and never be a filler, a number
        // word, or a base phrase of a different intent, or quantity/politeness
        // text would cast stray votes.
        var singleWordBase = new Dictionary<string, BotLexicon.Intent>(StringComparer.Ordinal);
        foreach (var (intent, phrases) in BotLexicon.Phrases)
            foreach (var phrase in phrases)
            {
                var folded = Fold(phrase);
                if (folded.Length >= 2 && !folded.Contains(' ')) singleWordBase.TryAdd(folded, intent);
            }

        var banned = BotLexicon.BannedGeneratedPhrases.Select(Fold).ToHashSet(StringComparer.Ordinal);
        var byWord = new Dictionary<string, BotLexicon.Intent>(StringComparer.Ordinal);
        var patched = new HashSet<string>(StringComparer.Ordinal);
        var dropped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (source, isPatch) in new[]
                 {
                     (BotLexicon.PatchKeywords, true),
                     (BotLexicon.GeneratedKeywords, false),
                     (BotLexicon.MinedKeywords, false),
                 })
            foreach (var (intent, words) in source)
                foreach (var word in words)
                {
                    var folded = Fold(word);
                    if (folded.Length < 2 || folded.Contains(' ')) continue;
                    if (!isPatch && (folded.Length < 3 || banned.Contains(folded))) continue;
                    if (FoldedFillers.Contains(folded) || FoldedNumberWords.ContainsKey(folded)) continue;
                    if (singleWordBase.TryGetValue(folded, out var baseOwner) && baseOwner != intent) continue;
                    if (byWord.TryGetValue(folded, out var owner))
                    {
                        if (owner != intent && !patched.Contains(folded)) dropped.Add(folded);
                        continue;
                    }
                    byWord[folded] = intent;
                    if (isPatch) patched.Add(folded);
                }
        return byWord
            .Where(p => !dropped.Contains(p.Key))
            .Select(p => (p.Value, p.Key))
            .ToList();
    }

    private static Dictionary<string, int> BuildFoldedNumbers()
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (word, value) in BotLexicon.NumberWords)
        {
            var folded = Fold(word);
            if (folded.Length > 0) dict[folded] = value;
        }
        return dict;
    }

    public sealed record IntentMatch(BotLexicon.Intent Intent, double Score, string Phrase);

    /// <summary>Tie priority: action intents beat StartOrder, which beats pleasantries —
    /// greetings prefix real requests, and "order" appears in almost everything.</summary>
    private static int Priority(BotLexicon.Intent intent) => intent switch
    {
        BotLexicon.Intent.Greeting or BotLexicon.Intent.Thanks
            or BotLexicon.Intent.Yes or BotLexicon.Intent.No => 0,
        BotLexicon.Intent.StartOrder => 1,
        _ => 2,
    };

    /// <summary>
    /// Best-matching intent for a message, or Intent.None below the confidence
    /// threshold. Yes/No are included — callers decide whether they apply.
    /// </summary>
    public static IntentMatch DetectIntent(string text)
    {
        var folded = Fold(text);
        var allTokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Politeness fillers carry no intent — matching happens without them
        // (phrases were filler-stripped the same way at build time).
        var tokens = allTokens.Where(t => !FoldedFillers.Contains(t)).ToArray();
        if (tokens.Length == 0) tokens = allTokens;
        if (tokens.Length == 0 && !HasCjk(folded)) return new(BotLexicon.Intent.None, 0, "");

        // A message that is nothing but a product name is a search for that product.
        // Without this, short food words land one edit from a command ("ماء" from the
        // "مساء" in مساء الخير) and the customer gets greeted instead of served.
        if (tokens.Length == 1 && FoodStopWords.Contains(tokens[0]))
            return new(BotLexicon.Intent.None, 0, "");

        var compact = folded.Replace(" ", "");

        BotLexicon.Intent bestIntent = BotLexicon.Intent.None;
        double bestScore = 0;
        var bestWords = 0;
        HashSet<int>? bestIndices = null;
        var bestPhrase = "";
        var bestPriority = -1;
        foreach (var (intent, phrase) in FoldedPhrases)
        {
            // Yes/No words are short and collide with everything; only consider
            // them when the whole message is short (a real answer, not a sentence).
            if (intent is BotLexicon.Intent.Yes or BotLexicon.Intent.No)
            {
                if (tokens.Length > 3) continue;
                // CJK has no word boundaries, so the token guard never fires —
                // demand a short answer, or "我的订单在哪" would read as a yes
                // through a stray "好".
                if (HasCjk(phrase))
                {
                    var ok = compact == phrase ||
                             (phrase.Length >= 2 && compact.Length <= 8 &&
                              compact.Contains(phrase, StringComparison.Ordinal));
                    if (!ok) continue;
                }
            }

            var (quality, words, indices) = PhraseScore(tokens, folded, phrase);
            if (quality <= 0) continue;
            var priority = Priority(intent);

            // Subsumption first: a phrase consuming a SUPERSET of another's
            // words is the fuller reading ("i want cancel" ⊃ "i want"), even
            // when a typo dents its quality. Otherwise compare match QUALITY;
            // ties resolve by intent priority (action beats StartOrder beats
            // pleasantries), then No-over-Yes, then specificity.
            bool better;
            if (indices is not null && bestIndices is not null &&
                indices.Count != bestIndices.Count &&
                Math.Abs(quality - bestScore) <= 0.2 &&
                (indices.IsSupersetOf(bestIndices) || bestIndices.IsSupersetOf(indices)))
            {
                better = indices.Count > bestIndices.Count;
            }
            else if (quality > bestScore + 0.02) better = true;
            else if (quality < bestScore - 0.02) better = false;
            else if (priority != bestPriority) better = priority > bestPriority;
            else if (intent == BotLexicon.Intent.No && bestIntent == BotLexicon.Intent.Yes) better = true;
            else if (intent == BotLexicon.Intent.Yes && bestIntent == BotLexicon.Intent.No) better = false;
            else better = words > bestWords || (words == bestWords && phrase.Length > bestPhrase.Length);
            if (better)
            {
                bestScore = quality;
                bestWords = words;
                bestIndices = indices;
                bestIntent = intent;
                bestPhrase = phrase;
                bestPriority = priority;
            }
        }

        // A confident greeting/thanks on a LONG message usually just prefixes
        // the real request ("Merhaba, where is my order?") — give the keyword
        // layer a chance to find the substantive intent before settling.
        var phraseDecided = bestScore >= 0.72;
        if (phraseDecided && !(bestPriority == 0 && tokens.Length > 6))
            return new(bestIntent, bestScore, bestPhrase);

        // ── Keyword fallback ─────────────────────────────────────────────
        // Long free-form sentences rarely contain a full lexicon phrase, but
        // usually contain one or two telling words ("starving", "status",
        // "منتظر"). Each user token casts at most ONE vote (for its best
        // keyword); a clear winner with enough evidence decides. Ties or weak
        // evidence stay None — the dialog's food-search default.
        var votes = new Dictionary<BotLexicon.Intent, (int Hits, bool Exact, List<string> Words)>();
        void Vote(BotLexicon.Intent intent, string word, bool exact)
        {
            if (!votes.TryGetValue(intent, out var v)) v = (0, false, []);
            v.Words.Add(word);
            votes[intent] = (v.Hits + 1, v.Exact || exact, v.Words);
        }

        foreach (var token in tokens)
        {
            BotLexicon.Intent tokenIntent = BotLexicon.Intent.None;
            double tokenBest = 0;
            var tokenWord = "";
            foreach (var (intent, keyword) in FoldedKeywords)
            {
                if (HasCjk(keyword)) continue;
                var s = TokenScore(token, keyword, strict: true);
                if (s > tokenBest) { tokenBest = s; tokenIntent = intent; tokenWord = keyword; }
                if (s >= 1) break;
            }
            if (tokenBest >= 0.8) Vote(tokenIntent, tokenWord, tokenBest >= 1);
        }
        if (HasCjk(folded))
        {
            foreach (var (intent, keyword) in FoldedKeywords)
                if (HasCjk(keyword) && compact.Contains(keyword, StringComparison.Ordinal))
                    Vote(intent, keyword, exact: true);
        }

        if (votes.Count > 0)
        {
            var ranked = votes.OrderByDescending(v => v.Value.Hits).ToList();
            var (winner, evidence) = (ranked[0].Key, ranked[0].Value);
            var runnerUp = ranked.Count > 1 ? ranked[1].Value.Hits : 0;
            // Two votes always decide; a single EXACT distinctive word carries
            // even long sentences; a fuzzy-only single vote needs a short one.
            var enough = evidence.Hits >= 2 ||
                         (evidence.Hits == 1 && evidence.Exact && tokens.Length <= 14) ||
                         (evidence.Hits == 1 && tokens.Length <= 6);
            if (enough && evidence.Hits > runnerUp)
                return new(winner, 0.7, string.Join(' ', evidence.Words.Distinct()));
        }

        // Nothing better found — fall back to the phrase decision if it was
        // confident (the greeting-on-a-long-message case lands here).
        if (phraseDecided) return new(bestIntent, bestScore, bestPhrase);
        return new(BotLexicon.Intent.None, bestScore, bestPhrase);
    }

    private static readonly HashSet<string> YesWords = SingleAnswerWords(BotLexicon.Intent.Yes);
    private static readonly HashSet<string> NoWords = SingleAnswerWords(BotLexicon.Intent.No);

    private static HashSet<string> SingleAnswerWords(BotLexicon.Intent intent) =>
        BotLexicon.Phrases[intent]
            .Concat(BotLexicon.PatchPhrases.TryGetValue(intent, out var extra) ? extra : [])
            .Select(Fold)
            .Where(p => p.Length >= 2 && !p.Contains(' '))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Is this message (as a whole, short answer) a yes or a no?</summary>
    public static bool? DetectYesNo(string text)
    {
        var match = DetectIntent(text);
        if (match.Intent == BotLexicon.Intent.Yes) return true;
        if (match.Intent == BotLexicon.Intent.No) return false;

        // Longer answers ("جی نہیں، رہنے دیں", "claro que sí, dale") exceed the
        // short-answer guard — count yes/no words directly. Any no-word means
        // no (the safe reading: a wrong "no" keeps the cart, a wrong "yes"
        // places an order); two-plus yes-words mean yes.
        var tokens = Tokens(text).Where(t => !FoldedFillers.Contains(t)).ToArray();
        if (tokens.Length is 0 or > 6) return null;
        var no = tokens.Count(t => NoWords.Any(w => TokenScore(t, w, strict: true) >= 0.84));
        if (no >= 1) return false;
        var yes = tokens.Count(t => YesWords.Any(w => TokenScore(t, w, strict: true) >= 0.84));
        return yes >= 2 ? true : null;
    }

    // ─────────────────────────── Slot extraction ───────────────────────────

    [GeneratedRegex(@"(?:^|\s)[x×]\s*(\d{1,2})(?:\s|$)|(?:^|\s)(\d{1,2})\s*[x×](?:\s|$)")]
    private static partial Regex QtyMarker();

    /// <summary>
    /// Number words too risky to read as a quantity inside a sentence: Turkish
    /// "on" (10) vs English "on", Hindi "do"/"दो" and Persian/Urdu "دو" (2) which
    /// double as everyday particles ("मंगवा दो"), German "das" (Hindi 10 "das").
    /// Folding strips Devanagari vowel signs, so "दो" arrives as "द".
    /// </summary>
    // "دو" is out: in Perso-Arabic script it only ever means two, and treating it as
    // ambiguous made "دو پیتزا" (two pizzas) lose its quantity entirely.
    private static readonly HashSet<string> AmbiguousQtyWords = ["on", "do", "das", "no", "द", "दो"];

    /// <summary>CJK measure words that must follow a numeral for it to be a quantity.</summary>
    private static readonly HashSet<char> CjkMeasureWords =
        ['个', '份', '杯', '只', '块', '盒', '瓶', '碗', '张', '客', 'つ', '個', '本', '枚', '点'];

    /// <summary>
    /// Pulls a quantity out of a message. In <paramref name="bareAnswer"/> mode
    /// (the bot just asked "how many?") everything goes; in free text a few
    /// dangerously ambiguous words are ignored. Returns the quantity and the
    /// message with the quantity tokens removed.
    /// </summary>
    public static (int? Qty, string Remainder) ExtractQuantity(string text, bool bareAnswer = false)
    {
        var folded = Fold(text);

        // "x2" / "2x" / "×2"
        var marker = QtyMarker().Match(" " + folded + " ");
        if (marker.Success)
        {
            var g = marker.Groups[1].Success ? marker.Groups[1] : marker.Groups[2];
            if (int.TryParse(g.Value, out var mq) && mq is > 0 and <= 50)
                return (mq, folded.Replace(marker.Value.Trim(), " ").Trim());
        }

        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var isDigit = int.TryParse(tokens[i], out var n) && n is > 0 and <= 50;
            var isWord = FoldedNumberWords.TryGetValue(tokens[i], out var wordValue);
            // Customers misspell their numbers too — "ثنين" for "اثنين", "thre" for "three".
            if (!isDigit && !isWord && FuzzyNumberWord(tokens[i]) is { } near)
            {
                isWord = true;
                wordValue = near;
            }
            if (!isDigit && !isWord) continue;

            // A counter word right after the number is part of the count, not the product:
            // "دو عدد پیتزا", "سه تا نوشابه", "2 pieces chicken".
            var counterFollows = i + 1 < tokens.Count && FoldedCounters.Contains(tokens[i + 1]);

            // A word like "دو" or "no" only counts as a number when something backs it up —
            // and a counter word right behind it is exactly that.
            if (isWord && !isDigit && !bareAnswer && !counterFollows &&
                AmbiguousQtyWords.Contains(tokens[i])) continue;

            if (counterFollows) tokens.RemoveAt(i + 1);
            tokens.RemoveAt(i);
            return (isDigit ? n : wordValue, string.Join(' ', tokens));
        }

        if (HasCjk(folded))
        {
            // Digits glued into unsegmented text: "ピザ2つ" → 2.
            var digit = Regex.Match(folded, @"\d{1,2}");
            if (digit.Success && int.TryParse(digit.Value, out var dq) && dq is > 0 and <= 50)
                return (dq, folded.Remove(digit.Index, digit.Length));

            // CJK numerals count only with a measure word ("两个披萨") or as the
            // whole answer — never mid-word ("三明治" is a sandwich, not qty 3).
            var compact = folded.Replace(" ", "");
            for (var i = 0; i < compact.Length; i++)
            {
                if (!BotLexicon.CjkNumbers.TryGetValue(compact[i], out var cjk)) continue;
                var hasMeasure = i + 1 < compact.Length && CjkMeasureWords.Contains(compact[i + 1]);
                if (hasMeasure)
                    return (cjk, compact.Remove(i, 2));
                if (bareAnswer && compact.Length == 1)
                    return (cjk, "");
            }
        }

        return (null, folded);
    }

    /// <summary>
    /// A misspelled number word, or null. Kept strict — 4+ letters and a single edit —
    /// so a real product never turns into a quantity.
    /// </summary>
    private static int? FuzzyNumberWord(string token)
    {
        if (token.Length < 4 || FoodStopWords.Contains(token)) return null;
        foreach (var (word, value) in FoldedNumberWords)
        {
            if (word.Length < 4 || Math.Abs(word.Length - token.Length) > 1) continue;
            if (EditDistance(token, word, 1) <= 1) return value;
        }
        return null;
    }

    private static readonly HashSet<string> FoldedCounters =
        BotLexicon.CounterWords.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    // A word that introduces a topping is never a list connector, whatever the native
    // lists say: splitting "برجر مع جبن" would order cheese as a separate product.
    private static readonly HashSet<string> FoldedConnectors =
        BotLexicon.ItemConnectors.Concat(BotLexicon.GeneratedConnectors)
            .Select(Fold)
            .Where(w => w.Length > 0 && !BotLexicon.ModifierParticles.Select(Fold).Contains(w))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>One product the customer asked for, with the quantity that belongs to it.</summary>
    public sealed record OrderPhrase(int? Qty, string Query);

    /// <summary>
    /// Splits one sentence into the separate products it orders: "ثنين بيترا و واحد عصير"
    /// becomes two pizzas plus one juice. Splitting happens on list connectors and on
    /// punctuation only — never on "with"-type words, because "tea with milk" is one drink.
    /// A single-product message simply comes back as one phrase.
    /// </summary>
    /// <param name="modifiersJoinItems">
    /// True for budget questions, where "which pizza can I get WITH a pepsi" pairs two
    /// products. When ordering it means the opposite — a topping on the dish just named.
    /// </param>
    public static List<OrderPhrase> SplitOrderItems(string text, bool modifiersJoinItems = false)
    {
        var result = new List<OrderPhrase>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        if (HasCjk(text))
        {
            // No spaces to split on: cut at the characters that mean "and" instead.
            foreach (var piece in Fold(text).Split(CjkConnectors, StringSplitOptions.RemoveEmptyEntries))
            {
                var (q, rest) = ExtractQuantity(piece);
                var only = CleanQuery(rest);
                if (only.Length > 0) result.Add(new OrderPhrase(q, only));
            }
            return result;
        }

        // Punctuation has to be cut BEFORE folding — folding throws it away, and a
        // comma is the plainest "next item" signal a customer has.
        foreach (var rawChunk in text.Split([',', '،', ';', '؛', ':', '+', '&', '\n'],
                                            StringSplitOptions.RemoveEmptyEntries))
        {
            // A dish whose own name contains "and" must survive whole.
            var chunk = Fold(rawChunk);
            foreach (var dish in FoldedCompoundDishes)
                chunk = chunk.Replace(dish.Spaced, dish.Glued, StringComparison.Ordinal);

            var tokens = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new List<string>();
            // Once a "with"/"extra" appears, a following connector is probably joining
            // toppings ("with cheese and tomato") — unless what follows carries its own
            // quantity, which makes it a real second product ("with cheese and 2 cokes").
            var modifierSeen = false;

            for (var i = 0; i < tokens.Length;)
            {
                var token = tokens[i];

                // Arabic writes "and" glued to the next word: "وبيبسي" = "and a Pepsi",
                // "و٣" = "and 3". Everyday waw-words ("وجبة", "وسط") are left alone.
                if (token.Length >= 4 && token[0] == 'و' &&
                    !BotLexicon.WawWords.Contains(token) &&
                    !FoldedNumberWords.ContainsKey(token) &&
                    (!modifierSeen || StartsWithQuantity(tokens, i, token[1..])))
                {
                    Flush(current, result);
                    current.Clear();
                    modifierSeen = false;
                    token = token[1..];
                    if (ConnectorLength([token], 0) > 0) { i++; continue; }   // "وكمان" = "and also"
                    current.Add(token);
                    i++;
                    continue;
                }

                if (FoldedModifiers.Contains(token))
                {
                    if (modifiersJoinItems)
                    {
                        Flush(current, result);
                        current.Clear();
                        i++;
                        continue;
                    }
                    modifierSeen = true;
                    current.Add(token);
                    i++;
                    continue;
                }

                var connector = ConnectorLength(tokens, i);
                if (connector > 0 && (!modifierSeen || SegmentHasQuantity(tokens, i + connector)))
                {
                    Flush(current, result);
                    current.Clear();
                    modifierSeen = false;
                    i += connector;
                    continue;
                }

                if (FoldedModifiers.Contains(token)) modifierSeen = true;
                current.Add(token);
                i++;
            }
            Flush(current, result);
        }
        return result;

        static void Flush(List<string> tokens, List<OrderPhrase> into)
        {
            if (tokens.Count == 0) return;
            var (qty, rest) = ExtractQuantity(string.Join(' ', tokens));
            var query = CleanQuery(rest);
            if (query.Length > 0) into.Add(new OrderPhrase(qty, Unglue(query)));
        }
    }

    private static readonly HashSet<string> FoldedCurrency =
        BotLexicon.CurrencyWords.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> FoldedOr =
        BotLexicon.OrConnectors.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    // ─────────────── Talking about what was said a moment ago ───────────────

    private static readonly Dictionary<string, int> FoldedOrdinals = BuildOrdinals();

    private static Dictionary<string, int> BuildOrdinals()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (word, index) in BotLexicon.OrdinalWords)
        {
            var folded = Fold(word);
            if (folded.Length > 0) map[folded] = index;
        }
        return map;
    }

    private static readonly HashSet<string> FoldedCheapest =
        BotLexicon.CheapestWords.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> FoldedPriciest =
        BotLexicon.PriciestWords.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> FoldedItPronouns =
        BotLexicon.ItPronouns.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>How a follow-up message points back at the list already on screen.</summary>
    public enum Reference { None, Ordinal, Cheapest, Priciest }

    /// <summary>
    /// Reads "the first one", "the last one", "the cheapest" against a list of
    /// <paramref name="optionCount"/> choices. Returns the zero-based index, or -1.
    /// </summary>
    public static (Reference Kind, int Index) ResolveReference(string text, int optionCount)
    {
        if (optionCount <= 0) return (Reference.None, -1);

        foreach (var token in Tokens(text))
        {
            if (FoldedOrdinals.TryGetValue(token, out var ordinal))
            {
                var index = ordinal == -1 ? optionCount - 1 : ordinal - 1;
                if (index >= 0 && index < optionCount) return (Reference.Ordinal, index);
            }
            if (FoldedCheapest.Contains(token)) return (Reference.Cheapest, -1);
            if (FoldedPriciest.Contains(token)) return (Reference.Priciest, -1);
        }

        // Multi-word forms ("en ucuz", "más barato", "the last one").
        var folded = Fold(text);
        if (FoldedCheapest.Any(w => w.Contains(' ') && folded.Contains(w, StringComparison.Ordinal)))
            return (Reference.Cheapest, -1);
        if (FoldedPriciest.Any(w => w.Contains(' ') && folded.Contains(w, StringComparison.Ordinal)))
            return (Reference.Priciest, -1);

        return (Reference.None, -1);
    }

    private static readonly HashSet<string> FoldedChangeQty =
        BotLexicon.ChangeQtyMarkers.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// "Make it 3" — a correction to the quantity of whatever was just added, not a new
    /// order for three. Returns the new quantity, or null when the message isn't one.
    /// </summary>
    public static int? ReadQuantityCorrection(string text)
    {
        var folded = Fold(text);
        var hasMarker = FoldedChangeQty.Any(m => m.Contains(' ')
            ? folded.Contains(m, StringComparison.Ordinal)
            : Tokens(text).Contains(m, StringComparer.Ordinal));
        if (!hasMarker) return null;

        var (qty, _) = ExtractQuantity(text, bareAnswer: true);
        return qty;
    }

    /// <summary>True when the message is only pointing at something ("remove it").</summary>
    public static bool IsBarePronoun(string text)
    {
        var tokens = Tokens(text).Where(t => !FoldedFillers.Contains(t)).ToList();
        // Some pronouns double as fillers ("یہ"), so stripping can empty the list — in
        // that case judge the raw words instead of concluding there was nothing there.
        if (tokens.Count == 0) tokens = [.. Tokens(text)];
        return tokens.Count > 0 && tokens.All(FoldedItPronouns.Contains);
    }

    private static readonly HashSet<string> FoldedBudgetWords =
        BotLexicon.BudgetQuestionWords.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A spending limit and what is left of the sentence: "I have 5 rials, which pizza can
    /// I buy with a pepsi" → 5, "which pizza can i buy with a pepsi". The amount only counts
    /// when a money word sits next to it, so "2 pizzas" is never mistaken for a budget.
    /// </summary>
    public static (decimal? Amount, string Remainder) ExtractBudget(string text)
    {
        var tokens = Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!FoldedCurrency.Contains(tokens[i])) continue;

            // The number may sit on either side: "5 rials" or "ريال 5" or "rials 5".
            foreach (var j in (int[])[i - 1, i + 1])
            {
                if (j < 0 || j >= tokens.Count) continue;
                if (!decimal.TryParse(tokens[j], System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var amount) &&
                    !(FoldedNumberWords.TryGetValue(tokens[j], out var word) && (amount = word) > 0)) continue;
                if (amount is <= 0 or > 10000) continue;

                tokens.RemoveAt(Math.Max(i, j));
                tokens.RemoveAt(Math.Min(i, j));
                return (amount, string.Join(' ', tokens));
            }
        }
        return (null, Fold(text));
    }

    /// <summary>
    /// The products a budget question mentions, as slots of interchangeable options:
    /// "which pizza with a pepsi or water" → [[pizza], [pepsi, water]].
    /// </summary>
    /// <summary>A budget slot: how many are wanted, and the interchangeable ways to fill it.</summary>
    public sealed record BudgetSlot(int Qty, List<string> Options);

    public static List<BudgetSlot> SplitAlternatives(string text)
    {
        var slots = new List<BudgetSlot>();
        foreach (var phrase in SplitOrderItems(text, modifiersJoinItems: true))
        {
            var options = new List<string>();
            var current = new List<string>();
            foreach (var token in phrase.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (FoldedOr.Contains(token))
                {
                    if (current.Count > 0) options.Add(string.Join(' ', current));
                    current.Clear();
                    continue;
                }
                // "what can I buy" is the question, not the food.
                if (FoldedBudgetWords.Contains(token)) continue;
                current.Add(token);
            }
            if (current.Count > 0) options.Add(string.Join(' ', current));
            // "2 pizzas with 2 drinks" costs twice each â€” a dropped quantity understates the basket.
            if (options.Count > 0) slots.Add(new BudgetSlot(Math.Max(1, phrase.Qty ?? 1), options));
        }
        return slots;
    }

    /// <summary>How many tokens at <paramref name="i"/> form a connector ("and", "bir de"), or 0.</summary>
    private static int ConnectorLength(string[] tokens, int i)
    {
        for (var len = Math.Min(3, tokens.Length - i); len >= 1; len--)
            if (FoldedConnectors.Contains(string.Join(' ', tokens.Skip(i).Take(len))))
                return len;
        return 0;
    }

    /// <summary>Does the run of tokens up to the next connector carry a quantity of its own?</summary>
    private static bool SegmentHasQuantity(string[] tokens, int start)
    {
        for (var i = start; i < tokens.Length; i++)
        {
            if (i > start && ConnectorLength(tokens, i) > 0) break;
            if (IsQuantityToken(tokens[i])) return true;
        }
        return false;
    }

    private static bool StartsWithQuantity(string[] tokens, int i, string firstToken) =>
        IsQuantityToken(firstToken) ||
        (i + 1 < tokens.Length && IsQuantityToken(tokens[i + 1]));

    private static bool IsQuantityToken(string token) =>
        (int.TryParse(token, out var n) && n is > 0 and <= 50) ||
        FoldedCounters.Contains(token) ||
        (FoldedNumberWords.ContainsKey(token) && !AmbiguousQtyWords.Contains(token));

    private static string Unglue(string text)
    {
        foreach (var dish in FoldedCompoundDishes)
            text = text.Replace(dish.Glued, dish.Spaced, StringComparison.Ordinal);
        return text;
    }

    private static readonly HashSet<string> FoldedModifiers =
        BotLexicon.ModifierParticles.Select(Fold).Where(w => w.Length > 0).ToHashSet(StringComparer.Ordinal);

    /// <summary>Compound dish names, folded, with a glued form used to hide them from the splitter.</summary>
    private static readonly (string Spaced, string Glued)[] FoldedCompoundDishes =
        BotLexicon.CompoundDishNames
            .Select(Fold)
            .Where(d => d.Contains(' '))
            .Select(d => (d, d.Replace(' ', '')))
            .ToArray();

    private static readonly string[] CjkConnectors =
        ["と", "そして", "それと", "あと", "и", "和", "还有", "再来", "再要", "加上", "、", "以及", "外加", "还要"];

    /// <summary>Drops ordering noise and any stray counter word left without a number.</summary>
    private static string CleanQuery(string foldedText) =>
        string.Join(' ', StripStartOrderNoise(foldedText)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !FoldedCounters.Contains(t)));

    [GeneratedRegex(@"(?:mf\s*-?\s*)?#?(?<!\d)(\d{3,8})(?!\d)")]
    private static partial Regex OrderNumber();

    /// <summary>Explicit order number in the message ("MF-1023", "#1023", "1023").</summary>
    public static string? ExtractOrderNumber(string text)
    {
        var m = OrderNumber().Match(Fold(text));
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Removes the tokens that triggered the intent plus filler words; what's
    /// left is the food-search query ("please i want 2 pizzas" → "pizzas").
    /// </summary>
    /// <summary>
    /// The folded question frames, longest first so "do you have" is taken off before
    /// "you have" can match half of it.
    /// </summary>
    private static readonly Lazy<string[]> FoldedAskForms = new(() =>
        BotLexicon.AskForms.Select(Fold)
                           .Where(p => p.Length > 0)
                           .Distinct(StringComparer.Ordinal)
                           .OrderByDescending(p => p.Length)
                           .ToArray());

    /// <summary>
    /// Takes the question off the front of a request: "do you have pizza" is a search for
    /// pizza. Never returns nothing — if the sentence was ONLY a question frame there is
    /// no dish in it, and the caller should see the original words rather than an empty
    /// string it would treat as silence.
    /// </summary>
    public static string StripAskForms(string folded)
    {
        if (string.IsNullOrWhiteSpace(folded)) return folded;
        var work = folded;

        foreach (var form in FoldedAskForms.Value)
        {
            if (work.Length == 0) break;
            if (HasCjk(form))
            {
                // No word boundaries to respect — cut it wherever it sits.
                if (work.Contains(form, StringComparison.Ordinal))
                    work = work.Replace(form, " ", StringComparison.Ordinal).Trim();
                continue;
            }

            // Anywhere in the sentence, but only on whole words: "hay" must not eat
            // the "hay" inside "hayashi".
            var padded = " " + work + " ";
            var needle = " " + form + " ";
            if (padded.Contains(needle, StringComparison.Ordinal))
                work = padded.Replace(needle, " ", StringComparison.Ordinal).Trim();
        }

        work = string.Join(' ', work.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return work.Length == 0 ? folded : work;
    }

    public static string StripToQuery(string text, string matchedPhrase)
    {
        var folded = StripAskForms(Fold(text));

        if (HasCjk(folded))
        {
            // No word boundaries: cut the matched phrase and fillers straight out.
            var result = folded.Replace(" ", "").Replace(matchedPhrase.Replace(" ", ""), "");
            foreach (var filler in FoldedFillers.Where(HasCjk))
                result = result.Replace(filler, "");
            return result.Trim();
        }

        var phraseTokens = matchedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>();
        foreach (var token in folded.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (phraseTokens.Any(pt => TokenScore(token, pt) >= 0.65)) continue;
            if (FoldedFillers.Contains(token)) continue;
            kept.Add(token);
        }
        return string.Join(' ', kept);
    }

    private static readonly Lazy<HashSet<string>> StartOrderWords = new(() =>
    {
        // Some lexicon phrases carry example dishes ("ابغى برجر", "ピザ食べたい").
        // The ordering VERBS repeat across many phrases — so only words used in
        // 3+ phrases count as noise, plus the curated keywords (verbs by
        // construction), and known dish words are excluded outright.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (intent, folded) in FoldedPhrases)
        {
            if (intent != BotLexicon.Intent.StartOrder || HasCjk(folded)) continue;
            foreach (var w in folded.Split(' ').Distinct())
                counts[w] = counts.GetValueOrDefault(w) + 1;
        }
        return counts.Where(c => c.Value >= 3 && c.Key.Length >= 2).Select(c => c.Key)
            .Concat(FoldedKeywords.Where(k => k.Intent == BotLexicon.Intent.StartOrder).Select(k => k.Folded))
            .Where(w => !FoodStopWords.Contains(w))
            .ToHashSet(StringComparer.Ordinal);
    });

    /// <summary>
    /// Removes ordering-vocabulary words from a food query: "im starving get me
    /// somthing to eat" must search for nothing (ask what to eat), not for
    /// "starving". Dish names survive — they are not in the intent lexicon.
    /// </summary>
    public static string StripStartOrderNoise(string foldedQuery)
    {
        if (HasCjk(foldedQuery)) return foldedQuery;
        // Fuzzy stripping only for 5+-letter words — "meat" is one edit from
        // "eat" but must survive; "somthing" → "something" must not. Leftover
        // single letters ("i") are never a dish either.
        var kept = foldedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => (t.Length > 1 || char.IsDigit(t[0])) &&
                        !StartOrderWords.Value.Contains(t) &&
                        !(t.Length >= 5 && StartOrderWords.Value.Any(w =>
                            w.Length >= 5 && TokenScore(t, w, strict: true) >= 0.8)))
            .ToArray();
        return string.Join(' ', kept);
    }

    // ───────────────────────── Option matching ─────────────────────────

    /// <summary>Words like "option"/"number" that precede a picked index.</summary>
    private static readonly HashSet<string> SelectorWords =
    [
        "option", "number", "num", "choice", "item", "رقم", "خيار", "الخيار", "شماره", "گزینه",
        "نمبر", "नंबर", "numara", "seçenek", "secenek", "numéro", "numero", "número", "opción",
        "opcion", "opção", "opcao", "nummer", "номер", "вариант", "opzione", "番", "番号", "选项",
    ];

    /// <summary>
    /// Which of the offered options did the user pick? Accepts an index number
    /// ("2", "option 2") or a fuzzy name match ("margarita" → "Margherita Pizza").
    /// Returns the option index or -1.
    /// </summary>
    public static int MatchOption(string text, IReadOnlyList<string> optionLabels)
    {
        var folded = Fold(text);
        // Politeness and selector words never carry meaning here.
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !FoldedFillers.Contains(t) && !SelectorWords.Contains(t))
            .ToArray();

        // Bare index (after stripping: "2", "option 2", "number 2 please").
        if (tokens.Length == 1 && int.TryParse(tokens[0], out var index) &&
            index >= 1 && index <= optionLabels.Count)
            return index - 1;

        if (tokens.Length == 0) return -1;

        var bestIdx = -1;
        double bestScore = 0;
        for (var i = 0; i < optionLabels.Count; i++)
        {
            var label = Fold(optionLabels[i]);
            double score;
            if (HasCjk(label) || HasCjk(folded))
            {
                var a = label.Replace(" ", "");
                var b = folded.Replace(" ", "");
                score = a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)
                    ? 0.9 : Trigram(a, b);
            }
            else
            {
                var labelTokens = label.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                // Every user token should land somewhere in the label.
                double total = 0;
                var matched = 0;
                foreach (var ut in tokens)
                {
                    double best = 0;
                    foreach (var lt in labelTokens)
                        best = Math.Max(best, TokenScore(ut, lt));
                    if (best >= 0.65) matched++;
                    total += best;
                }
                score = tokens.Length == 0 ? 0 : total / tokens.Length * ((double)matched / tokens.Length);
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }
        return bestScore >= 0.6 ? bestIdx : -1;
    }

    // ───────────────────────── Language detection ─────────────────────────

    /// <summary>Only fa/ar/ur share the Perso-Arabic script — any other UI language is no help.</summary>
    private static string? PersoArabic(string? locale) =>
        locale is "fa" or "ar" or "ur" ? locale : null;

    private static readonly Dictionary<string, string> FoldedScriptMarkers = BuildScriptMarkers();

    private static Dictionary<string, string> BuildScriptMarkers()
    {
        // Folding erases the ک/ی vs ك/ي distinction, so a word claimed by two of the
        // three languages after folding is evidence for neither and is dropped.
        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (locale, words) in BotLexicon.ScriptMarkers)
            foreach (var word in words)
            {
                var folded = Fold(word);
                if (folded.Length < 2) continue;
                if (claims.TryGetValue(folded, out var owner) && owner != locale) conflicts.Add(folded);
                else claims[folded] = locale;
            }
        foreach (var word in conflicts) claims.Remove(word);
        return claims;
    }

    /// <summary>
    /// Which of Persian/Arabic/Urdu owns the most words in this message, or null on a tie
    /// or no evidence.
    /// </summary>
    private static string? ScriptVote(string text)
    {
        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in Tokens(text))
            if (FoldedScriptMarkers.TryGetValue(token, out var locale))
                votes[locale] = votes.GetValueOrDefault(locale) + 1;

        if (votes.Count == 0) return null;
        var best = votes.MaxBy(v => v.Value);
        return votes.Count(v => v.Value == best.Value) == 1 ? best.Key : null;
    }

    /// <summary>
    /// Best-effort language of a message, or null when unsure (caller keeps the
    /// current UI language). Script-based decisions are confident; Latin-script
    /// languages need distinctive characters or two marker-word hits.
    /// </summary>
    /// <param name="uiLocale">
    /// The language the app is currently showing. Persian, Arabic and Urdu share one
    /// script and plenty of real messages ("سفارش", "آب", "سلام") contain no letter that
    /// separates them — for those the language the customer picked in the app is the best
    /// evidence available, and guessing Arabic at a Persian customer is the wrong default.
    /// </param>
    public static string? DetectLocale(string text, string? uiLocale = null)
    {
        var arabic = 0; var latin = 0; var cyrillic = 0; var devanagari = 0;
        var han = 0; var kana = 0;
        var urduHits = 0; var strongPersianHits = 0; var sharedPersianHits = 0;
        var arabicOnlyHits = 0; var arabicFormHits = 0;

        foreach (var c in text)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin++;
            else if (c >= 0x0600 && c <= 0x06FF)
            {
                arabic++;
                if (BotLexicon.UrduChars.Contains(c)) urduHits++;
                else if (c is 'پ' or 'چ' or 'ژ' or 'گ') strongPersianHits++;
                else if (BotLexicon.PersianChars.Contains(c)) sharedPersianHits++;
                else if (BotLexicon.ArabicOnlyChars.Contains(c)) arabicOnlyHits++;
                // ك U+0643 and ي U+064A come off an ARABIC keyboard; Persian and Urdu
                // keyboards produce ک U+06A9 and ی U+06CC for the same two letters.
                else if (c is 'ك' or 'ي') arabicFormHits++;
            }
            else if (c >= 0x0400 && c <= 0x04FF) cyrillic++;
            else if (c >= 0x0900 && c <= 0x097F) devanagari++;
            else if (c >= 0x3040 && c <= 0x30FF) kana++;
            else if (c >= 0x4E00 && c <= 0x9FFF) han++;
        }

        if (kana > 0) return "ja";
        if (han > 0) return "zh";                     // han without kana → Chinese
        if (devanagari > 0) return "hi";
        if (cyrillic > 0) return "ru";
        if (arabic > 0)
        {
            if (urduHits > 0) return "ur";

            // پ چ ژ گ ک ی belong to Persian AND Urdu — what they rule out is Arabic,
            // nothing more. Urdu without its own letters ("پارسل کب تک") looks exactly
            // like Persian here, so only vocabulary or the app's language can separate them.
            var notArabic = strongPersianHits + sharedPersianHits > 0 && arabicFormHits == 0;

            // Same script, different vocabulary: let the words vote. An "ar" vote on text
            // written in Persian/Urdu letters is folding noise (ک folds to ك) — ignore it.
            if (ScriptVote(text) is { } voted && !(notArabic && voted == "ar")) return voted;

            if (notArabic) return uiLocale == "ur" ? "ur" : "fa";
            if (arabicOnlyHits > 0) return "ar";

            // ي and ك come off an ARABIC keyboard. Persian and Urdu type ی and ک for those
            // same two letters and use them constantly, so a message carrying ي/ك without a
            // single ی/ک was written in Arabic — even when the app is set to Persian.
            if (arabicFormHits > 0) return "ar";

            // Nothing in the message separates the three. Answer in the language the
            // customer chose in the app rather than defaulting everyone to Arabic.
            return PersoArabic(uiLocale) ?? "ar";
        }
        if (latin == 0) return null;

        // Latin script: distinctive characters pin it immediately.
        foreach (var (ch, locale) in BotLexicon.LatinHintChars)
            if (text.Contains(ch)) return locale;

        // Otherwise count exact marker-word hits per language.
        var tokens = Tokens(text);
        string? best = null;
        var bestHits = 0;
        foreach (var (locale, markers) in BotLexicon.LatinMarkers)
        {
            var hits = tokens.Count(t => markers.Contains(t));
            if (hits > bestHits)
            {
                bestHits = hits;
                best = locale;
            }
        }
        // Romanized Hindi/Urdu is detected so it isn't mistaken for English,
        // but there is no roman-Hindi reply set — the caller maps it sensibly.
        return bestHits >= 2 ? best : null;
    }
}
