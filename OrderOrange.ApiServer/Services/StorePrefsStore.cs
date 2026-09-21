using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>A shop's display preferences — the things that change what staff see, not what the platform does.</summary>
public sealed class StorePrefsDoc
{
    /// <summary>The restaurant id.</summary>
    [BsonId] public int Id { get; set; }

    /// <summary>What money is called on this shop's screens: "OMR", "﷼", "$", "د.إ".</summary>
    public string Currency { get; set; } = "";
}

/// <summary>
/// Where a shop's display preferences live. Mongo rather than a new column on Restaurants:
/// the SQL schema is created by EnsureCreated, which never adds a column to a database that
/// already exists, so a new column would have to be applied to production by hand.
/// </summary>
public sealed class StorePrefsStore
{
    private readonly IMongoCollection<StorePrefsDoc> _prefs;

    public StorePrefsStore(IMongoDatabase database) =>
        _prefs = database.GetCollection<StorePrefsDoc>("storePrefs");

    public async Task<string> CurrencyAsync(int storeId)
    {
        var doc = await _prefs.Find(p => p.Id == storeId).FirstOrDefaultAsync();
        return doc?.Currency ?? "";
    }

    /// <summary>Reads many at once, for a list that shows several shops.</summary>
    public async Task<Dictionary<int, string>> CurrenciesAsync(IEnumerable<int> storeIds)
    {
        var ids = storeIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var docs = await _prefs.Find(p => ids.Contains(p.Id)).ToListAsync();
        return docs.Where(d => !string.IsNullOrWhiteSpace(d.Currency)).ToDictionary(d => d.Id, d => d.Currency);
    }

    public Task SetCurrencyAsync(int storeId, string? symbol)
    {
        var clean = (symbol ?? "").Trim();
        if (clean.Length > 8) clean = clean[..8];
        return _prefs.UpdateOneAsync(
            p => p.Id == storeId,
            Builders<StorePrefsDoc>.Update.Set(p => p.Currency, clean).SetOnInsert(p => p.Id, storeId),
            new UpdateOptions { IsUpsert = true });
    }
}
