// Per-scope language state + translation lookup, shared by all four OrderOrange apps.
//
// Translations live in EXTERNAL JSON FILES under /Localization/<code>.json, shipped by
// OrderOrange.ClientCore. Server apps read them from disk; the WebAssembly partner app
// fetches them over HTTP one language at a time (see TranslationCatalog). To add a
// language: drop a new <code>.json file in ClientCore/Localization and add it to _index.json.
//
// JSON shape:
// {
//   "$meta": { "code": "en", "name": "English", "flag": "🇬🇧", "rtl": false, "order": 10 },
//   "nav.restaurants": "Restaurants",
//   ...
// }
using Microsoft.JSInterop;

namespace OrderOrange.ClientCore.Services;

public sealed class LanguageService
{
    // ── Public API ──────────────────────────────────────────────────────────
    public sealed record LanguageOption(string Code, string NativeName, string Flag, bool IsRtl, int Order);

    public static LanguageOption[] SupportedLanguages =>
        TranslationCatalog.Languages.Select(l => new LanguageOption(l.Code, l.NativeName, l.Flag, l.IsRtl, l.Order)).ToArray();

    // ── Per-scope state ─────────────────────────────────────────────────────
    private readonly ILocaleStore _store;
    private readonly IJSRuntime _js;
    private readonly ITranslationLoader? _loader;
    private bool _loaded;

    public LanguageService(ILocaleStore store, IJSRuntime js, ITranslationLoader? loader = null)
    {
        _store = store;
        _js = js;
        _loader = loader;
        Locale = TranslationCatalog.DefaultLocale;
    }

    public string Locale { get; private set; }

    /// <summary>
    /// The language for THIS server-side render only — no storage, no JS, no event.
    /// Used when a crawler asks for ?lang=ar: the prerendered HTML must already be Arabic,
    /// and there is no browser on the other end to remember anything.
    /// </summary>
    public void UseLocaleForThisRender(string locale)
    {
        if (!SupportedLanguages.Any(l => l.Code == locale)) return;
        Locale = locale;
        HasChosen = true;
    }

    /// <summary>
    /// True only once a language has been CHOSEN — loaded from storage or picked on the
    /// welcome screen. Until then the UI still renders in the fallback locale (something
    /// must paint the choice screen itself), but the app knows not to pretend the
    /// default was a decision the customer made.
    /// </summary>
    public bool HasChosen { get; private set; }

    /// <summary>True once storage has been consulted — gates run after this, never before.</summary>
    public bool IsLoaded => _loaded;
    public bool IsRtl => SupportedLanguages.FirstOrDefault(l => l.Code == Locale)?.IsRtl ?? false;
    public string NativeName => SupportedLanguages.FirstOrDefault(l => l.Code == Locale)?.NativeName ?? Locale;
    public string FlagEmoji => SupportedLanguages.FirstOrDefault(l => l.Code == Locale)?.Flag ?? "🌐";

    /// <summary>Raised after a locale change so layouts re-render.</summary>
    public event Action? Changed;

    /// <summary>Lookup: active locale → default locale → the key itself.</summary>
    public string this[string key] => T(key);

    /// <summary>
    /// Translates a value that comes from the database (a cuisine or category name).
    /// Falls back to the stored text when no translation exists, so new data still shows.
    /// </summary>
    public string Data(string prefix, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var trimmed = value.Trim();
        var key = $"{prefix}.{trimmed}";
        var translated = T(key);
        return translated == key ? value : translated;
    }

    /// <summary>
    /// A product name in the customer's language. Most products in this app are virtual
    /// template items with no database row, so their names live in the localization files
    /// like cuisines do; anything unknown (a partner's own product) shows as stored.
    /// </summary>
    public string Product(string? name) => Data("product", name);

    /// <summary>True while the UI is showing the source language — lets callers skip work.</summary>
    public bool IsEnglish => Locale == TranslationCatalog.DefaultLocale;

    /// <summary>
    /// Unpacks a server refusal. Coded ones arrive as "§code§arg§arg…" (see
    /// ApiClient.SafeReadError) and come out in the customer's language; anything
    /// else — old servers, uncoded messages — passes through untouched.
    /// </summary>
    public string? ServerError(string? raw)
    {
        if (raw is null || !raw.StartsWith('§')) return raw;
        var parts = raw.Split('§', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return raw;
        var template = T(parts[0]);
        if (template == parts[0]) return string.Join(" ", parts); // no translation shipped yet
        try { return string.Format(template, parts.Skip(1).Cast<object>().ToArray()); }
        catch (FormatException) { return template; }
    }

    public string T(string key) => Translate(Locale, key);

    /// <summary>
    /// Lookup in an explicit locale (same fallback chain as <see cref="T"/>).
    /// Lets the chatbot answer in the language the user typed, regardless of
    /// the UI language.
    /// </summary>
    public static string Translate(string locale, string key)
    {
        if (TranslationCatalog.TryGet(locale, key, out var value)) return value;
        if (TranslationCatalog.TryGet(TranslationCatalog.DefaultLocale, key, out var fallback)) return fallback;
        return key;
    }

    /// <summary>Lazy-load the saved locale after first render (JS becomes available).</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var saved = await _store.GetAsync();
            if (!string.IsNullOrEmpty(saved) && SupportedLanguages.Any(l => l.Code == saved))
            {
                if (_loader is not null) await _loader.EnsureAsync(saved);
                Locale = saved;
                HasChosen = true;       // a stored value IS an earlier choice
            }
        }
        catch { /* storage not ready during prerender — keep default */ }
        _loaded = true;
        await SyncHtmlAttributesAsync();
        Changed?.Invoke();
    }

    public async Task SetLocaleAsync(string newLocale)
    {
        if (!SupportedLanguages.Any(l => l.Code == newLocale)) return;

        // Even re-picking the current locale counts: on the welcome screen, tapping
        // "English" while English happens to be the fallback is still a choice, and
        // it must dismiss the screen and be remembered like any other.
        if (newLocale == Locale && HasChosen) return;
        // A browser app fetches the language before showing it, so nothing flashes in English.
        if (_loader is not null) await _loader.EnsureAsync(newLocale);
        HasChosen = true;
        Locale = newLocale;
        await _store.SetAsync(newLocale);
        await SyncHtmlAttributesAsync();
        Changed?.Invoke();
    }

    private async Task SyncHtmlAttributesAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("mfLang.set", Locale, IsRtl);
        }
        catch { /* JS not ready yet */ }
    }
}
