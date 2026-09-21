using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// What the kitchen has done to a ticket. An online order already has a real status the
/// whole platform understands, so only its ticked lines live here; a round of food sent
/// from a table has no status anywhere else, so its whole state does.
/// </summary>
public sealed class KdsTicketDoc
{
    /// <summary>"&lt;storeId&gt;|&lt;ticket key&gt;" — one row per ticket per shop.</summary>
    [BsonId] public string Id { get; set; } = "";
    public int StoreId { get; set; }
    public string Key { get; set; } = "";
    /// <summary>"new", "cooking" or "done". Authoritative for a tab round only.</summary>
    public string State { get; set; } = "new";
    public DateTime? StartedAt { get; set; }
    public DateTime? DoneAt { get; set; }
    /// <summary>The lines a cook has ticked off. Shared, so two screens agree.</summary>
    public List<int> DoneLines { get; set; } = [];
    public DateTime SeenAt { get; set; }
}

/// <summary>
/// The kitchen's own memory. Mongo, like the other write-heavy trails: a handful of tiny
/// documents per service that nothing joins against, and a new SQL table would have to be
/// created by hand because the schema is EnsureCreated.
/// </summary>
public sealed class KdsStore
{
    private readonly IMongoCollection<KdsTicketDoc> _tickets;

    public KdsStore(IMongoDatabase database)
    {
        _tickets = database.GetCollection<KdsTicketDoc>("kdsTickets");
        _tickets.Indexes.CreateOne(new CreateIndexModel<KdsTicketDoc>(
            Builders<KdsTicketDoc>.IndexKeys.Ascending(t => t.StoreId).Descending(t => t.SeenAt)));
    }

    private static string RowId(int storeId, string key) => $"{storeId}|{key}";

    /// <summary>Everything this shop's kitchen has touched recently, by ticket key.</summary>
    public async Task<Dictionary<string, KdsTicketDoc>> ForStoreAsync(int storeId, DateTime since)
    {
        var docs = await _tickets.Find(t => t.StoreId == storeId && t.SeenAt >= since).ToListAsync();
        return docs.ToDictionary(d => d.Key, d => d);
    }

    public Task<KdsTicketDoc?> GetAsync(int storeId, string key) =>
        _tickets.Find(t => t.Id == RowId(storeId, key)).FirstOrDefaultAsync()!;

    public async Task<KdsTicketDoc> SetStateAsync(int storeId, string key, string state)
    {
        var now = DateTime.UtcNow;
        var update = Builders<KdsTicketDoc>.Update
            .SetOnInsert(t => t.StoreId, storeId)
            .SetOnInsert(t => t.Key, key)
            .Set(t => t.State, state)
            .Set(t => t.SeenAt, now);

        // The two stamps are what the board shows as "cooking for 6 minutes" and what a
        // recall has to clear, so they move with the state rather than being set blindly.
        update = state switch
        {
            "cooking" => update.Set(t => t.StartedAt, now).Set(t => t.DoneAt, (DateTime?)null),
            "done" => update.Set(t => t.DoneAt, now),
            _ => update.Set(t => t.StartedAt, (DateTime?)null).Set(t => t.DoneAt, (DateTime?)null),
        };

        return await _tickets.FindOneAndUpdateAsync(
            Builders<KdsTicketDoc>.Filter.Eq(t => t.Id, RowId(storeId, key)), update,
            new FindOneAndUpdateOptions<KdsTicketDoc, KdsTicketDoc>
            { IsUpsert = true, ReturnDocument = ReturnDocument.After });
    }

    /// <summary>Ticks a line off, or un-ticks it. Returns the lines now done.</summary>
    public async Task<List<int>> ToggleLineAsync(int storeId, string key, int lineId)
    {
        var existing = await GetAsync(storeId, key);
        var on = existing?.DoneLines.Contains(lineId) == true;
        var update = on
            ? Builders<KdsTicketDoc>.Update.Pull(t => t.DoneLines, lineId)
            : Builders<KdsTicketDoc>.Update.AddToSet(t => t.DoneLines, lineId);

        var doc = await _tickets.FindOneAndUpdateAsync(
            Builders<KdsTicketDoc>.Filter.Eq(t => t.Id, RowId(storeId, key)),
            update.SetOnInsert(t => t.StoreId, storeId).SetOnInsert(t => t.Key, key).Set(t => t.SeenAt, DateTime.UtcNow),
            new FindOneAndUpdateOptions<KdsTicketDoc, KdsTicketDoc>
            { IsUpsert = true, ReturnDocument = ReturnDocument.After });
        return doc.DoneLines;
    }

    /// <summary>Housekeeping: the kitchen has no use for last week's tickets.</summary>
    public Task ForgetOlderThanAsync(DateTime cutoff) =>
        _tickets.DeleteManyAsync(t => t.SeenAt < cutoff);
}
