using System.Globalization;
using System.Text;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Google/Temu-style search brain: normalization (case, diacritics, Arabic letter
/// forms), typo tolerance (Damerau-Levenshtein with transpositions), trigram
/// similarity for badly mangled words, weighted scoring for ranking, and a
/// "did you mean" suggestion built from the catalog's own vocabulary.
/// </summary>
public static class SmartSearch
{
    // ---------- Normalization ----------

    /// <summary>Lowercase, strip diacritics/tashkeel, fold Arabic letter variants.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var raw in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(raw);
            if (category == UnicodeCategory.NonSpacingMark) continue; // accents + tashkeel
            var c = raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => raw
            };
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString();
    }

    public static string[] Tokens(string text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ---------- Scoring ----------

    /// <summary>
    /// 0–100: how well the query matches this text. Exact substring beats prefix
    /// beats small typo beats trigram resemblance. Multi-word queries average
    /// their best per-token scores, so one wrong word doesn't kill the match.
    /// </summary>
    public static double Score(string text, string[] queryTokens)
    {
        var textNorm = Normalize(text);
        var textTokens = textNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryTokens.Length == 0 || textTokens.Length == 0) return 0;

        double total = 0;
        double globalBest = 0;
        var covered = 0;
        foreach (var q in queryTokens)
        {
            double best = 0;
            if (textTokens.Contains(q)) best = 100;          // whole word — the real thing
            else if (textNorm.Contains(q)) best = 92;        // inside a word ("water"→"watermelon")
            else
            {
                foreach (var t in textTokens)
                {
                    double s = 0;
                    if (t.StartsWith(q) || q.StartsWith(t)) s = 78;
                    else
                    {
                        var cap = q.Length <= 3 ? 1 : q.Length <= 6 ? 2 : 3;
                        var d = Damerau(t, q, cap);
                        if (d <= cap) s = 72 - d * 12;
                        else if (q.Length >= 5)
                        {
                            // Trigram is a last resort — a loose gate floods results
                            // with lookalike junk, so demand real resemblance.
                            var sim = Trigram(t, q);
                            if (sim >= .45) s = 38 + sim * 25;
                        }
                    }
                    if (s > best) best = s;
                }
            }
            total += best;
            if (best > globalBest) globalBest = best;
            if (best >= 75) covered++;
        }

        // Query tokens often include the same word in several languages — a perfect
        // hit on one of them must survive even when the others contribute nothing.
        // The coverage bonus then ranks texts matching MORE of the query above texts
        // riding a single homonym ("مكواة بخار" is a steam iron, not a fever pill).
        return Math.Max(total / queryTokens.Length, globalBest * 0.6) + covered * 2;
    }

    /// <summary>Damerau-Levenshtein (handles swapped letters like "piZZa"→"piZaZ") with early exit.</summary>
    public static int Damerau(string a, string b, int cap)
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
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1); // transposition
                rowBest = Math.Min(rowBest, d[i, j]);
            }
            if (rowBest > cap) return cap + 1;
        }
        return d[a.Length, b.Length];
    }

    /// <summary>Character-trigram Jaccard similarity — survives heavy mangling.</summary>
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

    /// <summary>Nearest vocabulary word for one token, or null when nothing is close enough.</summary>
    public static string? Closest(string token, IEnumerable<string> vocab)
    {
        var cap = token.Length <= 4 ? 1 : token.Length <= 7 ? 2 : 3;
        string? best = null;
        var bestDistance = cap + 1;
        var bestPrefix = -1;
        foreach (var word in vocab)
        {
            var d = Damerau(word, token, cap);
            if (d > cap) continue;
            // Ties go to the word sharing the longest prefix with what was typed —
            // "piza" must mean "pizza", not "pina".
            var prefix = CommonPrefix(word, token);
            if (d < bestDistance || (d == bestDistance && (prefix > bestPrefix ||
                    (prefix == bestPrefix && best is not null && word.Length < best.Length))))
            {
                bestDistance = d;
                bestPrefix = prefix;
                best = word;
            }
        }
        return bestDistance <= cap ? best : null;
    }

    private static int CommonPrefix(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    // ---------- Query understanding (the "smart" layer) ----------

    // Arabizi: Latin chat-alphabet digits standing in for Arabic letters ("3asir" → عصير).
    private static readonly Dictionary<char, char> Arabizi = new()
    {
        ['2'] = 'ء', ['3'] = 'ع', ['5'] = 'خ', ['6'] = 'ط', ['7'] = 'ح', ['8'] = 'غ', ['9'] = 'ص'
    };

    // Windows Arabic-101 layout: what each Latin key prints when the keyboard is in Arabic
    // mode. Lets us recover "pizza" from "حهققش" (typed with the wrong layout active).
    private static readonly Dictionary<char, char> EnKeyToAr = new()
    {
        ['q'] = 'ض', ['w'] = 'ص', ['e'] = 'ث', ['r'] = 'ق', ['t'] = 'ف', ['y'] = 'غ', ['u'] = 'ع',
        ['i'] = 'ه', ['o'] = 'خ', ['p'] = 'ح', ['a'] = 'ش', ['s'] = 'س', ['d'] = 'ي', ['f'] = 'ب',
        ['g'] = 'ل', ['h'] = 'ا', ['j'] = 'ت', ['k'] = 'ن', ['l'] = 'م', ['z'] = 'ئ', ['x'] = 'ء',
        ['c'] = 'ؤ', ['v'] = 'ر', ['n'] = 'ى', ['m'] = 'ة'
    };
    private static readonly Dictionary<char, char> ArKeyToEn = BuildReverse();

    private static Dictionary<char, char> BuildReverse()
    {
        var map = new Dictionary<char, char>();
        foreach (var (en, ar) in EnKeyToAr) map[ar] = en;
        return map;
    }

    private static string? Translate(string token, Dictionary<char, char> map)
    {
        var chars = new char[token.Length];
        for (var i = 0; i < token.Length; i++)
        {
            if (!map.TryGetValue(token[i], out var c)) return null; // mixed input — not a layout slip
            chars[i] = c;
        }
        return new string(chars);
    }

    /// <summary>
    /// Full query understanding: typo correction, Arabizi digits, wrong-keyboard-layout
    /// recovery, merged/split word repair and prefix (search-as-you-type) expansion.
    /// Returns every token worth matching plus a "did you mean" when the query changed.
    /// </summary>
    public static (string[] Tokens, string? DidYouMean) Analyze(string query, ISet<string> vocab)
    {
        var raw = Tokens(query);
        if (raw.Length == 0) return ([], null);

        var expanded = new List<string>(raw);
        var display = (string[])raw.Clone();
        var changed = false;

        void Accept(int i, string word)
        {
            expanded.Add(word);
            if (display[i] == raw[i]) { display[i] = word; changed = true; }
        }

        for (var i = 0; i < raw.Length; i++)
        {
            var t = raw[i];
            if (t.Length < 2 || vocab.Contains(t)) continue;

            // Arabizi ("3asir", "7aleeb") → full phonetic Arabic: both readings of '8',
            // and with/without long vowels ("3asir" can be عصير or عصر).
            if (t.Any(char.IsDigit) && t.Any(char.IsLetter))
            {
                foreach (var variant in new[]
                         {
                             ArabiziToArabic(t, 'غ', false), ArabiziToArabic(t, 'ق', false),
                             ArabiziToArabic(t, 'غ', true), ArabiziToArabic(t, 'ق', true)
                         })
                {
                    if (variant.Length < 2 || variant == t) continue;
                    if (vocab.Contains(variant)) { Accept(i, variant); }
                    else if (Closest(variant, vocab) is { } fixedUp) Accept(i, fixedUp);
                }
                if (display[i] != raw[i]) continue;
            }

            // Wrong keyboard layout, both directions — accept only clean vocabulary hits.
            foreach (var map in new[] { ArKeyToEn, EnKeyToAr })
            {
                var swapped = Translate(t, map);
                if (swapped is null) continue;
                if (vocab.Contains(swapped)) { Accept(i, swapped); break; }
                if (swapped.Length >= 4 && Closest(swapped, vocab) is { } near && Damerau(near, swapped, 1) <= 1)
                {
                    Accept(i, near);
                    break;
                }
            }
            if (display[i] != raw[i]) continue;

            // Search-as-you-type first: a short partial word with real prefix matches
            // ("piz" → pizza) is not a typo and must not trigger a weak correction.
            var prefixHits = 0;
            if (t.Length >= 3)
            {
                foreach (var word in vocab)
                {
                    if (word.Length <= t.Length || !word.StartsWith(t, StringComparison.Ordinal)) continue;
                    expanded.Add(word);
                    if (++prefixHits == 3) break;
                }
            }

            // Plain typo correction — only when the prefix theory found nothing.
            // In multi-word queries allow a single edit only: aggressive correction
            // of one word hijacks the whole query ("عطر رجالي" must stay perfume).
            if ((prefixHits == 0 || t.Length >= 5) && Closest(t, vocab) is { } corrected &&
                (raw.Length == 1 || Damerau(corrected, t, 1) <= 1))
                Accept(i, corrected);

            // Split a merged word: "pizzaburger" → pizza + burger.
            if (display[i] == raw[i] && t.Length >= 6)
            {
                for (var cut = 3; cut <= t.Length - 3; cut++)
                {
                    var left = t[..cut];
                    var right = t[cut..];
                    if (vocab.Contains(left) && vocab.Contains(right))
                    {
                        expanded.Add(left);
                        expanded.Add(right);
                        display[i] = $"{left} {right}";
                        changed = true;
                        break;
                    }
                }
            }

        }

        // Merge split words: "بيت زا" → "بيتزا".
        for (var i = 0; i < raw.Length - 1; i++)
        {
            if (vocab.Contains(raw[i]) && vocab.Contains(raw[i + 1])) continue;
            var merged = raw[i] + raw[i + 1];
            if (vocab.Contains(merged))
            {
                expanded.Add(merged);
                display[i] = merged;
                display[i + 1] = "";
                changed = true;
            }
        }

        var didYouMean = changed
            ? string.Join(' ', display.Where(d => d.Length > 0))
            : null;
        return (expanded.Distinct().ToArray(), didYouMean);
    }

    /// <summary>Full Arabizi transliteration: digits → their letters, Latin consonants →
    /// Arabic phonetics, short vowels dropped (unwritten in Arabic). "5ubz" → "خبز".</summary>
    private static string ArabiziToArabic(string token, char eightMapped, bool keepLongVowels = false)
    {
        var sb = new System.Text.StringBuilder(token.Length);
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            if (i + 1 < token.Length)
            {
                var digraph = $"{c}{token[i + 1]}" switch
                {
                    "sh" => "ش", "kh" => "خ", "gh" => "غ", "th" => "ث", "ch" => "تش", _ => null
                };
                if (digraph is not null) { sb.Append(digraph); i++; continue; }
            }
            if (c == '8') { sb.Append(eightMapped); continue; }
            if (Arabizi.TryGetValue(c, out var mapped)) { sb.Append(mapped); continue; }
            sb.Append(c switch
            {
                'b' => "ب", 't' => "ت", 'j' or 'g' => "ج", 'd' => "د", 'r' => "ر", 'z' => "ز",
                's' => "س", 'f' or 'v' => "ف", 'q' => "ق", 'k' or 'c' => "ك", 'l' => "ل",
                'm' => "م", 'n' => "ن", 'h' => "ه", 'w' => "و", 'y' => "ي", 'p' => "ب", 'x' => "كس",
                'i' or 'e' => sb.Length == 0 ? "ا" : keepLongVowels ? "ي" : "",
                'o' or 'u' => sb.Length == 0 ? "ا" : keepLongVowels ? "و" : "",
                'a' => sb.Length == 0 ? "ا" : "",
                _ => c.ToString()
            });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Corrects a whole query against a prebuilt vocabulary set. Returns the
    /// corrected tokens plus a "did you mean" string when anything changed.
    /// </summary>
    public static (string[] Tokens, string? DidYouMean) Correct(string query, ISet<string> vocab)
    {
        var tokens = Tokens(query);
        var corrected = new string[tokens.Length];
        var changed = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Length < 3 || vocab.Contains(tokens[i])) { corrected[i] = tokens[i]; continue; }
            var best = Closest(tokens[i], vocab);
            corrected[i] = best ?? tokens[i];
            if (best is not null && best != tokens[i]) changed = true;
        }
        return (corrected, changed ? string.Join(' ', corrected) : null);
    }

    // ---------- "Did you mean" ----------

    /// <summary>
    /// Rebuilds the query from the catalog's own vocabulary: each query token is
    /// replaced by its closest known word when it's a near-miss. Returns null when
    /// the query is already fine.
    /// </summary>
    public static string? DidYouMean(string query, IEnumerable<string> vocabularyTexts)
    {
        var vocab = new HashSet<string>();
        foreach (var text in vocabularyTexts)
            foreach (var token in Tokens(text))
                if (token.Length >= 3) vocab.Add(token);

        var queryTokens = Tokens(query);
        if (queryTokens.Length == 0) return null;

        var corrected = new List<string>();
        var changed = false;
        foreach (var q in queryTokens)
        {
            if (vocab.Contains(q)) { corrected.Add(q); continue; }
            var cap = q.Length <= 4 ? 1 : q.Length <= 7 ? 2 : 3;
            string? best = null;
            var bestDistance = cap + 1;
            foreach (var word in vocab)
            {
                var d = Damerau(word, q, cap);
                if (d < bestDistance || (d == bestDistance && best is not null && word.Length < best.Length))
                {
                    bestDistance = d;
                    best = word;
                }
            }
            if (best is not null && bestDistance <= cap)
            {
                corrected.Add(best);
                changed = true;
            }
            else corrected.Add(q);
        }
        return changed ? string.Join(' ', corrected) : null;
    }
}
