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

    /// <summary>ISO 3166 country the shop trades in ("OM"); empty = never answered.</summary>
    public string Country { get; set; } = "";

    /// <summary>IANA time zone ("Asia/Muscat"); empty = the server's own.</summary>
    public string TimeZone { get; set; } = "";

    /// <summary>"dmy" | "mdy" | "ymd"; empty = dmy.</summary>
    public string DateFormat { get; set; } = "";

    /// <summary>"24" | "12"; empty = 24.</summary>
    public string TimeFormat { get; set; } = "";
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

    /// <summary>Everything at once (currency and country) — one read for the owner's own store.</summary>
    public async Task<StorePrefsDoc> GetAsync(int storeId) =>
        await _prefs.Find(p => p.Id == storeId).FirstOrDefaultAsync() ?? new StorePrefsDoc { Id = storeId };

    /// <summary>Time zone, date order and clock in one write; a blank value leaves that one unchanged.</summary>
    public Task SetClockAsync(int storeId, string? timeZone, string? dateFormat, string? timeFormat)
    {
        var updates = new List<UpdateDefinition<StorePrefsDoc>> { Builders<StorePrefsDoc>.Update.SetOnInsert(p => p.Id, storeId) };
        if (!string.IsNullOrWhiteSpace(timeZone)) updates.Add(Builders<StorePrefsDoc>.Update.Set(p => p.TimeZone, timeZone.Trim()));
        if (dateFormat is "dmy" or "mdy" or "ymd") updates.Add(Builders<StorePrefsDoc>.Update.Set(p => p.DateFormat, dateFormat));
        if (timeFormat is "24" or "12") updates.Add(Builders<StorePrefsDoc>.Update.Set(p => p.TimeFormat, timeFormat));
        return _prefs.UpdateOneAsync(p => p.Id == storeId, Builders<StorePrefsDoc>.Update.Combine(updates), new UpdateOptions { IsUpsert = true });
    }

    public Task SetCountryAsync(int storeId, string? code)
    {
        var clean = (code ?? "").Trim().ToUpperInvariant();
        if (clean.Length > 2) clean = clean[..2];
        return _prefs.UpdateOneAsync(
            p => p.Id == storeId,
            Builders<StorePrefsDoc>.Update.Set(p => p.Country, clean).SetOnInsert(p => p.Id, storeId),
            new UpdateOptions { IsUpsert = true });
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
