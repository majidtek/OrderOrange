using System.Globalization;

namespace OrderOrange.RestaurantWeb.Services;

/// <summary>
/// The assistant's arithmetic. A shop owner reaching for a calculator mid-conversation
/// should not have to leave the conversation: "2+2", «۱۲ × ۳٫۵», "15% of 240", "200 minus
/// 12.5" all belong here.
///
/// <para>It is a real parser — tokenise, shunting-yard, evaluate — and never anything
/// resembling <c>eval</c>. The input is a sentence typed by a stranger to the code; the
/// only safe way to compute it is to understand it.</para>
///
/// <para>The hard part is not the maths, it is knowing when NOT to do maths. This bot's
/// main job is taking orders, and an order is full of bare numbers: "2 pizza table 2".
/// So a message is only a calculation when it is ENTIRELY a calculation — every token
/// accounted for as a number, an operator or a bracket. One unexplained word and the
/// sentence goes back to the order parser where it belongs.</para>
/// </summary>
public static partial class PosBotNlu
{
    /// <summary>A computed answer, with the expression as it was understood.</summary>
    /// <param name="Ok">False when the text was not arithmetic, or could not be computed.</param>
    /// <param name="Value">The result.</param>
    /// <param name="Expression">The normalised sum, for echoing back: "15% of 240".</param>
    public sealed record Sum(bool Ok, double Value, string Expression)
    {
        public static readonly Sum No = new(false, 0, "");
    }

    /// <summary>Symbols for the operators, in the languages the portal speaks. Multiplied
    /// out of words because "12 times 3" and «۱۲ ضربدر ۳» are how people actually type.</summary>
    private static class Ops
    {
        static Ops() { }

        internal static readonly (string Word, char Symbol)[] Words =
        [
            // plus
            ("plus", '+'), ("add", '+'), ("and", '+'),
            ("زائد", '+'), ("زايد", '+'), ("جمع", '+'), ("بعلاوه", '+'), ("بهعلاوه", '+'), ("علاوه", '+'),
            ("arti", '+'), ("плюс", '+'), ("mas", '+'), ("più", '+'), ("piu", '+'), ("plus", '+'),
            // minus
            ("minus", '-'), ("less", '-'), ("subtract", '-'),
            ("ناقص", '-'), ("منها", '-'), ("منهای", '-'), ("منهاي", '-'), ("كسر", '-'),
            ("eksi", '-'), ("минус", '-'), ("menos", '-'), ("moins", '-'), ("meno", '-'),
            // times
            ("times", '*'), ("multiplied", '*'), ("multiply", '*'),
            ("ضرب", '*'), ("ضربدر", '*'), ("في", '*'), ("×", '*'),
            ("carpi", '*'), ("çarpı", '*'), ("умнож", '*'), ("por", '*'), ("fois", '*'), ("mal", '*'),
            // divided
            ("divided", '/'), ("divide", '/'), ("over", '/'),
            ("تقسیم", '/'), ("تقسيم", '/'), ("علی", '/'),
            ("bolu", '/'), ("bölü", '/'), ("дел", '/'), ("entre", '/'), ("diviso", '/'), ("geteilt", '/'),
        ];

        /// <summary>"20% OF 300" — the word that turns a percentage into a multiplication.</summary>
        internal static readonly string[] OfWords =
        [
            "of", "from", "از", "من", "على", "de", "del", "di", "von", "от", "nin", "nın",
        ];

        /// <summary>Prepositions that belong to the operator before them and mean nothing
        /// alone: "divided BY", "multiplied BY". Dropping them is what lets the two-word
        /// forms parse without "by" also having to be an operator in its own right.</summary>
        internal static readonly string[] PairWords = ["by", "par", "durch", "per", "на"];
    }

    /// <summary>
    /// Moves a percent sign that sits in FRONT of its number to behind it. Arabic and
    /// Persian write «٪۱۰» where English writes "10%"; both mean the same thing, and the
    /// parser should only have to know one of them.
    /// </summary>
    private static string MovePrefixPercent(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '%' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                var start = ++i;
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.')) i++;
                sb.Append(text[start..i]).Append('%');
                continue;
            }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Reads a message as a sum. Returns <see cref="Sum.No"/> — the overwhelmingly common
    /// case — for anything that is not, from end to end, arithmetic.
    /// </summary>
    public static Sum TryCalculate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Sum.No;

        var text = Normalise(raw);
        if (text.Length == 0) return Sum.No;

        var tokens = Tokenise(text);
        if (tokens is null) return Sum.No;

        // A single number is not a sum — "5" is an answer to a question, or a quantity.
        var numbers = tokens.Count(t => t.Kind == TokKind.Number);
        var operators = tokens.Count(t => t.Kind == TokKind.Operator);
        var percents = tokens.Count(t => t.Kind == TokKind.Percent);
        if (numbers < 2 && percents == 0) return Sum.No;
        if (operators == 0 && percents == 0) return Sum.No;

        var rpn = ToPostfix(tokens);
        if (rpn is null) return Sum.No;

        var value = Evaluate(rpn);
        if (value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return Sum.No;

        return new Sum(true, value.Value, Render(tokens));
    }

    // ─────────────────────────── normalising ───────────────────────────

    /// <summary>
    /// Everything that can stand for an operator becomes the plain ASCII one, and every
    /// digit script becomes ASCII. What comes out is a string of numbers, the five
    /// operator characters, brackets and percent — or a word this is not going to touch.
    /// </summary>
    private static string Normalise(string raw)
    {
        var digits = AsciiDigits(raw);
        var sb = new System.Text.StringBuilder(digits.Length);

        foreach (var ch in digits)
        {
            var c = ch switch
            {
                '×' or '✕' or '✖' or '⋅' or '·' => '*',
                '÷' or '∕' => '/',
                '−' or '–' or '—' => '-',
                '٪' or '﹪' or '％' => '%',
                // Arabic decimal separator, and its thousands mark which simply goes.
                '٫' => '.',
                '٬' or ',' => ' ',
                'ـ' => ' ',
                _ => ch,
            };
            sb.Append(c);
        }

        // A percent written in front of its number — «٪۱۰», the normal order in Arabic
        // and Persian — is moved behind it, so one rule handles both conventions.
        var moved = MovePrefixPercent(sb.ToString());

        // Word operators, matched whole so "and" inside a dish name cannot become a plus.
        //
        // Only words are considered. Fold() strips punctuation, so "+" and "×" both fold
        // to the empty string — comparing folded forms would make every symbolic operator
        // equal to every other one, and the whole expression would turn into multiplies.
        var output = new List<string>();
        foreach (var part in moved.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.Any(char.IsLetter)) { output.Add(part); continue; }

            var folded = Fold(part);
            var mapped = Ops.Words.FirstOrDefault(w => folded == Fold(w.Word));
            if (mapped.Symbol != '\0') { output.Add(mapped.Symbol.ToString()); continue; }

            // "divided BY four", "multiplied BY three" — the preposition belongs to the
            // operator in front of it and carries no meaning of its own.
            if (Ops.PairWords.Any(p => folded == Fold(p))
                && output.Count > 0 && output[^1].Length == 1 && "+-*/^".Contains(output[^1]))
                continue;

            if (Ops.OfWords.Any(o => folded == Fold(o))) { output.Add("*"); continue; }

            output.Add(part);
        }

        return string.Join(' ', output);
    }

    // ─────────────────────────── tokenising ───────────────────────────

    private enum TokKind { Number, Operator, Percent, Open, Close }

    private readonly record struct Tok(TokKind Kind, double Number, char Op);

    /// <summary>
    /// Splits the normalised text into numbers, operators and brackets. Returns null the
    /// moment it meets anything else — that "anything else" is what keeps an order from
    /// being mistaken for a sum.
    /// </summary>
    private static List<Tok>? Tokenise(string text)
    {
        var tokens = new List<Tok>();
        var i = 0;

        while (i < text.Length)
        {
            var ch = text[i];

            if (char.IsWhiteSpace(ch)) { i++; continue; }

            if (char.IsAsciiDigit(ch) || (ch == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                var start = i;
                while (i < text.Length && (char.IsAsciiDigit(text[i]) || text[i] == '.')) i++;
                var slice = text[start..i];
                if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    return null;
                tokens.Add(new Tok(TokKind.Number, n, '\0'));
                continue;
            }

            switch (ch)
            {
                case '+' or '-' or '*' or '/' or '^':
                    tokens.Add(new Tok(TokKind.Operator, 0, ch));
                    i++;
                    continue;
                case '%':
                    tokens.Add(new Tok(TokKind.Percent, 0, '%'));
                    i++;
                    continue;
                case '(' or '[':
                    tokens.Add(new Tok(TokKind.Open, 0, '('));
                    i++;
                    continue;
                case ')' or ']':
                    tokens.Add(new Tok(TokKind.Close, 0, ')'));
                    i++;
                    continue;
                case '=' or '?' or '!' or '؟':
                    // Trailing punctuation is fine: "2+2=?" is still a sum.
                    i++;
                    continue;
                default:
                    // A letter, a currency word, a dish — this was never arithmetic.
                    return null;
            }
        }

        return tokens.Count == 0 ? null : tokens;
    }

    // ─────────────────────────── shunting yard ───────────────────────────

    private static int Precedence(char op) => op switch
    {
        '+' or '-' => 1,
        '*' or '/' => 2,
        '^' => 3,
        _ => 0,
    };

    /// <summary>
    /// Infix to postfix. A percent sign is folded into the number it follows — "15%"
    /// becomes 0.15 — which makes "15% of 240" fall out as a plain multiplication once
    /// "of" has already become "*".
    /// </summary>
    private static List<Tok>? ToPostfix(List<Tok> tokens)
    {
        var output = new List<Tok>();
        var stack = new Stack<Tok>();
        Tok? previous = null;

        foreach (var token in tokens)
        {
            switch (token.Kind)
            {
                case TokKind.Number:
                    output.Add(token);
                    break;

                case TokKind.Percent:
                    // Only meaningful straight after a number.
                    if (output.Count == 0 || output[^1].Kind != TokKind.Number) return null;
                    output[^1] = output[^1] with { Number = output[^1].Number / 100.0 };
                    break;

                case TokKind.Operator:
                    // A leading "-" is a sign, not a subtraction: "-5 + 12".
                    var unary = previous is null
                                || previous.Value.Kind == TokKind.Open
                                || previous.Value.Kind == TokKind.Operator;
                    if (unary && token.Op is '-' or '+')
                    {
                        output.Add(new Tok(TokKind.Number, 0, '\0'));
                        stack.Push(token);
                        break;
                    }
                    while (stack.Count > 0 && stack.Peek().Kind == TokKind.Operator
                           && Precedence(stack.Peek().Op) >= Precedence(token.Op)
                           && token.Op != '^')
                        output.Add(stack.Pop());
                    stack.Push(token);
                    break;

                case TokKind.Open:
                    stack.Push(token);
                    break;

                case TokKind.Close:
                    while (stack.Count > 0 && stack.Peek().Kind != TokKind.Open)
                        output.Add(stack.Pop());
                    if (stack.Count == 0) return null;      // unbalanced
                    stack.Pop();
                    break;
            }
            previous = token;
        }

        while (stack.Count > 0)
        {
            var top = stack.Pop();
            if (top.Kind == TokKind.Open) return null;      // unbalanced
            output.Add(top);
        }

        return output;
    }

    private static double? Evaluate(List<Tok> rpn)
    {
        var stack = new Stack<double>();
        foreach (var token in rpn)
        {
            if (token.Kind == TokKind.Number) { stack.Push(token.Number); continue; }
            if (token.Kind != TokKind.Operator) return null;
            if (stack.Count < 2) return null;

            var b = stack.Pop();
            var a = stack.Pop();
            switch (token.Op)
            {
                case '+': stack.Push(a + b); break;
                case '-': stack.Push(a - b); break;
                case '*': stack.Push(a * b); break;
                // Dividing by zero is a question with no answer, not a crash.
                case '/': if (b == 0) return null; stack.Push(a / b); break;
                case '^': stack.Push(Math.Pow(a, b)); break;
                default: return null;
            }
        }
        return stack.Count == 1 ? stack.Pop() : null;
    }

    /// <summary>The sum written back out, so the reply can show its working.</summary>
    private static string Render(List<Tok> tokens)
    {
        var parts = tokens.Select(t => t.Kind switch
        {
            TokKind.Number => t.Number.ToString("0.####", CultureInfo.InvariantCulture),
            TokKind.Operator => $" {t.Op} ",
            TokKind.Percent => "%",
            TokKind.Open => "(",
            _ => ")",
        });
        return string.Join("", parts).Replace("  ", " ").Trim();
    }
}
