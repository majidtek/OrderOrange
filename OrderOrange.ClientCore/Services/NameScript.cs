namespace OrderOrange.ClientCore.Services;

/// <summary>
/// English is the main language of every multi-language name (Majed 2026-10-09). When an
/// old record arrives with one bare name and no per-language map, this decides which row
/// it belongs in: Arabic-script text goes to the Arabic row, everything else to English.
/// </summary>
public static class NameScript
{
    public static string LangOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "en";
        foreach (var ch in text)
        {
            // Arabic, Arabic Supplement, Arabic Extended-A and the presentation forms.
            if (ch is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ'
                or >= 'ࢠ' and <= 'ࣿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿')
                return "ar";
        }
        return "en";
    }

    /// <summary>
    /// The language a piece of text is most likely written in, judged by its script — for a
    /// <c>lang</c> attribute so the right font is used for Arabic, Persian, Urdu, Hindi… even
    /// while the page itself is in English (Majed 2026-10-16: "for Arabic or فارسی use the
    /// related font"). Persian and Urdu share the Arabic range; their own letters tell them apart.
    /// </summary>
    public static string ScriptLang(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "en";
        var arabic = false;
        foreach (var ch in text)
        {
            switch (ch)
            {
                case 'ٹ' or 'ڈ' or 'ڑ' or 'ں' or 'ے' or 'ھ': return "ur";   // ٹ ڈ ڑ ں ے ھ
                case 'پ' or 'چ' or 'ژ' or 'گ' or 'ی' or 'ک': return "fa";   // پ چ ژ گ ی ک
            }
            if (ch is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿') arabic = true;
            else if (ch is >= 'ऀ' and <= 'ॿ') return "hi";
            else if (ch is >= 'Ѐ' and <= 'ӿ') return "ru";
            else if (ch is >= '぀' and <= 'ヿ') return "ja";
            else if (ch is >= '一' and <= '鿿') return "zh";
        }
        return arabic ? "ar" : "en";
    }
}
