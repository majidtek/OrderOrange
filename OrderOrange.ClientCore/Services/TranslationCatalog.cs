using System.Net.Http.Json;
using System.Text.Json;

namespace OrderOrange.ClientCore.Services;

/// <summary>
/// The process-wide store of translations, one dictionary per language. Filled either
/// from the JSON files on disk (server apps, once, on first use) or over HTTP one
/// language at a time (the WebAssembly partner app, which must not download all
/// fourteen files to show one).
/// </summary>
public static class TranslationCatalog
{
    public sealed record LanguageOption(string Code, string NativeName, string Flag, bool IsRtl, int Order);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LanguageOption> LanguagesByCode = new(StringComparer.OrdinalIgnoreCase);
    private static bool _filesTried;

    public const string DefaultLocale = "en";

    /// <summary>Every language the app can show, in menu order.</summary>
    public static LanguageOption[] Languages
    {
        get
        {
            EnsureFiles();
            lock (Gate)
            {
                if (LanguagesByCode.Count == 0) return [new LanguageOption("en", "English", "🇬🇧", false, 10)];
                return LanguagesByCode.Values.OrderBy(l => l.Order)
                    .ThenBy(l => l.NativeName, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
    }

    public static bool Has(string locale)
    {
        EnsureFiles();
        lock (Gate) return Translations.ContainsKey(locale);
    }

    public static bool TryGet(string locale, string key, out string value)
    {
        EnsureFiles();
        lock (Gate)
        {
            if (Translations.TryGetValue(locale, out var dict) && dict.TryGetValue(key, out var v)) { value = v; return true; }
        }
        value = "";
        return false;
    }

    public static void AddLanguage(LanguageOption option)
    {
        lock (Gate) LanguagesByCode[option.Code] = option;
    }

    /// <summary>Parses one language file (the "$meta" + flat keys shape) into the catalog.</summary>
    public static void AddFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("$meta", out var meta) || meta.ValueKind != JsonValueKind.Object) return;

        var code = meta.TryGetProperty("code", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(code)) return;

        var name = meta.TryGetProperty("name", out var n) ? n.GetString() ?? code : code;
        var flag = meta.TryGetProperty("flag", out var f) ? f.GetString() ?? "" : "";
        var rtl = meta.TryGetProperty("rtl", out var r) && r.ValueKind == JsonValueKind.True;
        var order = meta.TryGetProperty("order", out var o) && o.TryGetInt32(out var oi) ? oi : 100;

        // Case-insensitive: data keys ("product.Karak Tea") must still match when the
        // stored row is spelled "Karak tea". Assignment lets a later duplicate win.
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name == "$meta") continue;
            if (prop.Value.ValueKind == JsonValueKind.String)
                dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }

        lock (Gate)
        {
            Translations[code] = dict;
            LanguagesByCode[code] = new LanguageOption(code, name, flag, rtl, order);
        }
    }

    /// <summary>
    /// Server apps: the files sit beside the binaries (ClientCore copies them there).
    /// Runs once; a browser app has no such folder and simply gets nothing from here.
    /// </summary>
    private static void EnsureFiles()
    {
        if (_filesTried) return;
        lock (Gate)
        {
            if (_filesTried) return;
            _filesTried = true;
        }
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Localization");
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                if (Path.GetFileName(file).StartsWith('_')) continue;   // the index, not a language
                try { AddFromJson(File.ReadAllText(file)); }
                catch { /* one broken file must not break every language */ }
            }
        }
        catch { /* no file system (browser) — the HTTP loader fills the catalog instead */ }
    }
}

/// <summary>Fetches languages on demand — the WebAssembly app's way into the catalog.</summary>
public interface ITranslationLoader
{
    Task EnsureAsync(string locale);
}

/// <summary>
/// Loads <c>{basePath}_index.json</c> (every language's name/flag/direction, so the picker
/// can list them without downloading them) and then one <c>{code}.json</c> per language
/// actually shown.
/// </summary>
public sealed class HttpTranslationLoader(HttpClient http, string basePath = "i18n/") : ITranslationLoader
{
    private sealed record IndexEntry(string Code, string Name, string Flag, bool Rtl, int Order);
    private readonly Dictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);

    public async Task LoadIndexAsync()
    {
        try
        {
            var entries = await http.GetFromJsonAsync<List<IndexEntry>>(basePath + "_index.json") ?? [];
            foreach (var e in entries)
                TranslationCatalog.AddLanguage(new TranslationCatalog.LanguageOption(e.Code, e.Name, e.Flag, e.Rtl, e.Order));
        }
        catch { /* the picker falls back to whatever languages get loaded */ }
    }

    public Task EnsureAsync(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale) || TranslationCatalog.Has(locale)) return Task.CompletedTask;
        lock (_inFlight)
        {
            if (!_inFlight.TryGetValue(locale, out var task))
                _inFlight[locale] = task = FetchAsync(locale);
            return task;
        }
    }

    private async Task FetchAsync(string locale)
    {
        try
        {
            var json = await http.GetStringAsync($"{basePath}{locale}.json");
            TranslationCatalog.AddFromJson(json);
        }
        catch { /* stays in the fallback language; the next switch tries again */ }
        finally { lock (_inFlight) _inFlight.Remove(locale); }
    }
}
