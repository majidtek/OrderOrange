using System.Text.Json;
using Microsoft.Maui.Storage;
using OrderOrange.ClientCore.Services;

namespace OrderOrange.PartnerApp.Services;

/// <summary>
/// The native twin of <see cref="HttpTranslationLoader"/>: reads <c>i18n/_index.json</c> (every
/// language's name, flag and direction, for the picker) and then one <c>i18n/{code}.json</c> per
/// language actually shown — from the app package instead of over HTTP.
/// </summary>
public sealed class PackageTranslationLoader : ITranslationLoader
{
    private sealed record IndexEntry(string Code, string Name, string Flag, bool Rtl, int Order);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, Task> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private Task? _index;

    public Task LoadIndexAsync() => _index ??= LoadIndexCoreAsync();

    private static async Task LoadIndexCoreAsync()
    {
        try
        {
            var entries = JsonSerializer.Deserialize<List<IndexEntry>>(await ReadAsync("i18n/_index.json"), Json) ?? [];
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
                _inFlight[locale] = task = LoadAsync(locale);
            return task;
        }
    }

    private async Task LoadAsync(string locale)
    {
        try { TranslationCatalog.AddFromJson(await ReadAsync($"i18n/{locale}.json")); }
        catch { /* stays in the fallback language; the next switch tries again */ }
        finally { lock (_inFlight) _inFlight.Remove(locale); }
    }

    private static async Task<string> ReadAsync(string path)
    {
        await using var stream = await FileSystem.OpenAppPackageFileAsync(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
