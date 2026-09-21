namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Ranks how well a haystack answers a typed query, and says which characters matched
/// so the UI can underline them.
///
/// This is deliberately not a plain <c>Contains</c>. A search box that only finds exact
/// substrings makes the user type the label they were trying to avoid typing: "salrep"
/// should reach "Salary report", "tq" should reach "Table QR codes". Equally it must not
/// be so loose that every entry matches everything — so a subsequence hit scores far
/// below a prefix hit, and gaps between matched letters cost points.
/// </summary>
public static class Fuzzy
{
    /// <summary>A match, its score, and where it landed. Higher scores sort first.</summary>
    public readonly record struct Match(bool Ok, int Score, int[] Positions)
    {
        public static readonly Match None = new(false, 0, []);
    }

    private const int ScoreExact = 1000;
    private const int ScorePrefix = 700;
    private const int ScoreWordStart = 500;
    private const int ScoreContains = 300;
    private const int ScoreInitials = 260;
    private const int ScoreSubsequence = 100;

    /// <summary>An empty query matches everything, neutrally — that is what makes the
    /// palette show a sensible default list before a key is pressed.</summary>
    public static Match Score(string? query, string? haystack)
    {
        if (string.IsNullOrWhiteSpace(haystack)) return Match.None;
        var text = haystack.Trim();
        if (string.IsNullOrWhiteSpace(query)) return new Match(true, 1, []);

        var q = Normalize(query.Trim());
        var t = Normalize(text);
        if (q.Length == 0) return new Match(true, 1, []);
        if (q.Length > t.Length) return Match.None;

        if (t == q)
            return new Match(true, ScoreExact, Range(0, q.Length));

        if (t.StartsWith(q, StringComparison.Ordinal))
            // Shorter haystacks win: typing "tab" should offer "Tables" above "Table QR codes".
            return new Match(true, ScorePrefix + LengthBonus(t.Length), Range(0, q.Length));

        // A hit at the start of any word reads as intentional — "report" finding
        // "Salary report" is exactly what the user meant.
        var wordStart = WordStartIndex(t, q);
        if (wordStart >= 0)
            return new Match(true, ScoreWordStart + LengthBonus(t.Length), Range(wordStart, q.Length));

        var contains = t.IndexOf(q, StringComparison.Ordinal);
        if (contains >= 0)
            return new Match(true, ScoreContains + LengthBonus(t.Length) - contains, Range(contains, q.Length));

        // Initials: "sr" → "Salary report", "tqr" → "Table QR codes".
        var initials = InitialsMatch(t, q);
        if (initials is not null)
            return new Match(true, ScoreInitials + LengthBonus(t.Length), initials);

        return Subsequence(t, q);
    }

    /// <summary>The best score across several fields — a customer found by phone should
    /// rank as well as one found by name.</summary>
    public static Match Best(string? query, params string?[] fields)
    {
        var best = Match.None;
        foreach (var field in fields)
        {
            var m = Score(query, field);
            if (m.Ok && m.Score > best.Score) best = m;
        }
        return best;
    }

    /// <summary>Splits a label into matched and unmatched runs, in order, so a component
    /// can render the matched parts in bold without re-deriving the positions.</summary>
    public static List<(string Text, bool Hit)> Highlight(string text, int[] positions)
    {
        var parts = new List<(string, bool)>();
        if (string.IsNullOrEmpty(text)) return parts;
        if (positions.Length == 0) return [(text, false)];

        var hits = new HashSet<int>(positions);
        var start = 0;
        var current = hits.Contains(0);

        for (var i = 1; i <= text.Length; i++)
        {
            var isHit = i < text.Length && hits.Contains(i);
            if (i == text.Length || isHit != current)
            {
                parts.Add((text[start..i], current));
                start = i;
                current = isHit;
            }
        }
        return parts;
    }

    // ---------- internals ----------

    /// <summary>Lower-cases, and folds Arabic/Persian letter variants and digits so that
    /// searching "کباب" finds "كباب" and "٣" finds "3". Same length in, same length out —
    /// highlight positions index the ORIGINAL string, so this must never add or drop a
    /// character.</summary>
    private static string Normalize(string s)
    {
        Span<char> buffer = s.Length <= 128 ? stackalloc char[s.Length] : new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var ch = char.ToLowerInvariant(s[i]);
            buffer[i] = ch switch
            {
                'ي' or 'ى' => 'ی',                        // Arabic yeh → Persian yeh
                'ك' => 'ک',                                // Arabic kaf → Persian kaf
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',          // hamza forms → plain alef
                'ة' => 'ه',
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                _ => ch,
            };
        }
        return new string(buffer);
    }

    private static int LengthBonus(int length) => Math.Max(0, 60 - length);

    private static int[] Range(int start, int count)
    {
        var result = new int[count];
        for (var i = 0; i < count; i++) result[i] = start + i;
        return result;
    }

    private static bool IsBreak(char ch) => ch is ' ' or '-' or '_' or '/' or '·' or '(' or '،' or ',';

    private static int WordStartIndex(string text, string query)
    {
        for (var i = 1; i < text.Length; i++)
        {
            if (!IsBreak(text[i - 1])) continue;
            if (i + query.Length <= text.Length
                && string.CompareOrdinal(text, i, query, 0, query.Length) == 0)
                return i;
        }
        return -1;
    }

    /// <summary>Matches the query against the first letter of each word.</summary>
    private static int[]? InitialsMatch(string text, string query)
    {
        var starts = new List<int>();
        if (text.Length > 0 && !IsBreak(text[0])) starts.Add(0);
        for (var i = 1; i < text.Length; i++)
            if (IsBreak(text[i - 1]) && !IsBreak(text[i])) starts.Add(i);

        if (starts.Count < query.Length) return null;

        // Only a run of consecutive words counts — "sr" must not match the S of word one
        // and the R of word nine.
        for (var offset = 0; offset + query.Length <= starts.Count; offset++)
        {
            var ok = true;
            for (var k = 0; k < query.Length; k++)
            {
                if (text[starts[offset + k]] != query[k]) { ok = false; break; }
            }
            if (ok) return starts.Skip(offset).Take(query.Length).ToArray();
        }
        return null;
    }

    /// <summary>Letters in order but not adjacent. Scored by how tightly they cluster, so
    /// a scattered accidental match sinks below anything more direct.</summary>
    private static Match Subsequence(string text, string query)
    {
        var positions = new int[query.Length];
        var qi = 0;
        var gapPenalty = 0;
        var last = -1;

        for (var ti = 0; ti < text.Length && qi < query.Length; ti++)
        {
            if (text[ti] != query[qi]) continue;
            positions[qi] = ti;
            if (last >= 0) gapPenalty += ti - last - 1;
            last = ti;
            qi++;
        }

        if (qi < query.Length) return Match.None;

        var score = ScoreSubsequence + LengthBonus(text.Length) - Math.Min(90, gapPenalty * 3) - positions[0];
        return new Match(true, Math.Max(1, score), positions);
    }
}
