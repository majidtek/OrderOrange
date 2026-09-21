using OrderOrange.ApiServer.Data;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Services;

/// <summary>The catalog's word lists, cached in memory and rebuilt every few minutes.</summary>
/// <param name="RestaurantWords">Words that actually occur in store names/areas/cuisines —
/// a LIKE against millions of store rows is only worth running for these.</param>
/// <param name="AllWords">Everything, including dish names and the multilingual keyword
/// column — used for typo correction and "did you mean".</param>
public sealed record Vocab(HashSet<string> RestaurantWords, HashSet<string> AllWords);

/// <summary>
/// With millions of stores we can't tokenize every name per keystroke, so a strided
/// sample of store names (plus every cuisine, dish name and keyword) is cached.
/// </summary>
public static class SearchVocabCache
{
    private static Vocab? _vocab;
    private static DateTime _builtAtUtc;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<Vocab> GetAsync(AppDbContext db)
    {
        var cached = _vocab;
        if (cached is not null && DateTime.UtcNow - _builtAtUtc < TimeSpan.FromMinutes(10)) return cached;

        await Gate.WaitAsync();
        try
        {
            cached = _vocab;
            if (cached is not null && DateTime.UtcNow - _builtAtUtc < TimeSpan.FromMinutes(10)) return cached;

            var restaurantWords = new HashSet<string>();
            var allWords = new HashSet<string>();
            void AddAll(HashSet<string> set, IEnumerable<string> texts)
            {
                foreach (var text in texts)
                    foreach (var token in SmartSearch.Tokens(text))
                        if (token.Length >= 3)
                            set.Add(token);
            }

            AddAll(restaurantWords, await db.Cuisines.Select(c => c.Name).ToListAsync());
            // BOTH ends of the id range plus every ~600th row of the bulk catalog:
            // generated names reuse a fixed word pool, so a stride covers it fully —
            // while hand-created stores live at the ends (low ids before the catalog
            // merge, the very top after it) and every one of their words must be in.
            AddAll(restaurantWords, await db.Restaurants.OrderBy(r => r.Id).Take(2000).Select(r => r.Name + " " + r.Area).ToListAsync());
            AddAll(restaurantWords, await db.Restaurants.OrderByDescending(r => r.Id).Take(2000).Select(r => r.Name + " " + r.Area).ToListAsync());
            AddAll(restaurantWords, await db.Restaurants.Where(r => r.Id % 599 == 0).Select(r => r.Name + " " + r.Area).ToListAsync());
            // The newest stores in every language they publish. The column holds JSON
            // with \uXXXX escapes, so tokenizing the raw string yields "u0643" noise —
            // the inverted index already holds these words correctly decoded, so take
            // them from there instead.
            var newestIds = await db.Restaurants.OrderByDescending(r => r.Id).Take(500)
                .Select(r => r.Id).ToListAsync();
            restaurantWords.UnionWith(await db.RestaurantWords
                .Where(w => newestIds.Contains(w.RestaurantId))
                .Select(w => w.Word).Distinct().ToListAsync());

            allWords.UnionWith(restaurantWords);
            allWords.UnionWith(SearchAliases.DictionaryWords);
            AddAll(allWords, await db.MenuItems.Select(i => i.Name + " " + i.SearchKeywords).ToListAsync());

            _vocab = new Vocab(restaurantWords, allWords);
            _builtAtUtc = DateTime.UtcNow;
            return _vocab;
        }
        finally
        {
            Gate.Release();
        }
    }
}
