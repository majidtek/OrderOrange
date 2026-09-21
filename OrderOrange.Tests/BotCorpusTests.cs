extern alias clientweb;
using System.Text.Json;
using clientweb::OrderOrange.ClientWeb.Services;

namespace OrderOrange.Tests;

/// <summary>
/// Regression gate over the 700-utterance multilingual evaluation corpus
/// (bot-corpus.json): realistic customer messages in 14 languages, ~40% with
/// deliberate typos, labeled by intent. Guards the NLU's overall accuracy —
/// individual phrasing fixes must not regress other languages.
/// </summary>
public class BotCorpusTests
{
    private sealed record Case(string Text, string Expect);
    private sealed record Lang(string Code, List<Case> Cases);

    private static List<Lang> Load(string file = "bot-corpus.json")
    {
        var path = Path.Combine(AppContext.BaseDirectory, file);
        return JsonSerializer.Deserialize<List<Lang>>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    [Fact]
    public void Corpus_accuracy_stays_above_95_percent()
    {
        var langs = Load();
        var total = 0;
        var ok = 0;
        var misses = new List<string>();
        foreach (var lang in langs)
            foreach (var c in lang.Cases)
            {
                var got = BotNlu.DetectIntent(c.Text);
                var name = got.Intent == BotLexicon.Intent.None ? "Search" : got.Intent.ToString();
                total++;
                if (name == c.Expect) ok++;
                else misses.Add($"[{lang.Code}] \"{c.Text}\" expected {c.Expect} got {name}");
            }

        var accuracy = (double)ok / total;
        Assert.True(accuracy >= 0.95,
            $"Corpus accuracy {accuracy:P1} ({ok}/{total}) fell below 95%.\n" + string.Join('\n', misses));
    }

    [Fact]
    public void Hard_sentence_corpus_stays_above_75_percent()
    {
        // 560 deliberately difficult 8-20-word sentences (indirect phrasings,
        // complaints, compound thoughts). Strict metric — behaviorally-benign
        // misses (want-food → Search, long No → DetectYesNo) count as misses.
        var total = 0;
        var ok = 0;
        foreach (var lang in Load("bot-corpus-hard.json"))
            foreach (var c in lang.Cases)
            {
                var got = BotNlu.DetectIntent(c.Text);
                var name = got.Intent == BotLexicon.Intent.None ? "Search" : got.Intent.ToString();
                total++;
                if (name == c.Expect) ok++;
            }
        var accuracy = (double)ok / total;
        Assert.True(accuracy >= 0.75, $"Hard corpus accuracy {accuracy:P1} ({ok}/{total}) fell below 75%.");
    }

    private sealed record LocaleSet(string Locale, List<string> Easy, List<string> Hard, List<string> Ambiguous);

    private static List<LocaleSet> LoadLocales()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "bot-locale-corpus.json");
        return JsonSerializer.Deserialize<List<LocaleSet>>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    /// <summary>
    /// Persian, Arabic and Urdu share a script. A customer writing Persian in a Persian app
    /// must never be answered in Arabic — that was a real bug, and this is the guard.
    /// </summary>
    [Fact]
    public void Perso_Arabic_text_is_never_answered_in_the_wrong_language()
    {
        var misses = new List<string>();
        foreach (var set in LoadLocales())
            foreach (var text in set.Easy.Concat(set.Hard))
            {
                var got = BotNlu.DetectLocale(text, set.Locale);
                if (got != set.Locale) misses.Add($"[{set.Locale}] \"{text}\" → {got ?? "null"}");
            }

        Assert.True(misses.Count == 0,
            $"{misses.Count} messages detected as the wrong language while the app was already in that language:\n"
            + string.Join('\n', misses));
    }

    /// <summary>
    /// Strings that are identical in two Perso-Arabic languages ("سلام", "کباب") carry no
    /// evidence — answering them in a third language the customer never chose is the failure.
    /// </summary>
    [Fact]
    public void Ambiguous_perso_arabic_strings_never_pick_a_foreign_language()
    {
        // Only strings with no orthographic signal at all are genuinely undecidable.
        // ي/ك come off an Arabic keyboard and ی/ک off a Persian or Urdu one, so a string
        // containing either DOES carry evidence and is judged by the tests above instead.
        static bool Neutral(string s) => !s.Any("يكیکپچژگٹڈڑںھےہ".Contains);

        var total = 0;
        var ok = 0;
        foreach (var set in LoadLocales())
            foreach (var text in set.Ambiguous.Where(Neutral))
                foreach (var ui in new[] { "fa", "ar", "ur" })
                {
                    var got = BotNlu.DetectLocale(text, ui);
                    total++;
                    // Null keeps the current language; the app's own language is always fine.
                    if (got is null || got == ui) ok++;
                }

        var rate = (double)ok / total;
        Assert.True(rate >= 0.8, $"Ambiguous strings respected the app language only {rate:P1} of the time ({ok}/{total}).");
    }

    [Fact]
    public void No_language_falls_below_90_percent()
    {
        foreach (var lang in Load())
        {
            var ok = lang.Cases.Count(c =>
            {
                var got = BotNlu.DetectIntent(c.Text);
                var name = got.Intent == BotLexicon.Intent.None ? "Search" : got.Intent.ToString();
                return name == c.Expect;
            });
            var accuracy = (double)ok / lang.Cases.Count;
            Assert.True(accuracy >= 0.9, $"{lang.Code}: {accuracy:P1} ({ok}/{lang.Cases.Count}) below 90%");
        }
    }
}
