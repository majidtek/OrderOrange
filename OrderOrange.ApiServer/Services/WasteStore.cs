using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>One write-off, with what it was worth at the moment it was thrown away.</summary>
public sealed class WasteDoc
{
    [BsonId] public int Id { get; set; }
    public int StoreId { get; set; }
    /// <summary>"material" or "product".</summary>
    public string Kind { get; set; } = "material";
    public int RefId { get; set; }
    /// <summary>Snapshotted: the record must still read correctly after a rename or a deletion.</summary>
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Cost { get; set; }
    public string Reason { get; set; } = "";
    public string? Notes { get; set; }
    public int UserId { get; set; }
    public string By { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>
/// Where write-offs live. Mongo, like the platform's other append-only trails: a small
/// document per event that nothing joins against, and a new SQL table could not be added
/// to a live database anyway because the schema is created by EnsureCreated.
/// </summary>
public sealed class WasteStore
{
    private readonly IMongoCollection<WasteDoc> _waste;
    private readonly IMongoCollection<BsonDocument> _counters;

    public WasteStore(IMongoDatabase database)
    {
        _waste = database.GetCollection<WasteDoc>("wasteLog");
        _counters = database.GetCollection<BsonDocument>("counters");
        // _id is indexed by Mongo itself and may not be re-declared; this is the page's
        // only access path: one shop, newest first.
        _waste.Indexes.CreateOne(new CreateIndexModel<WasteDoc>(
            Builders<WasteDoc>.IndexKeys.Ascending(w => w.StoreId).Descending(w => w.At)));
    }

    public async Task<WasteDoc> AddAsync(WasteDoc doc)
    {
        var seq = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", "wasteLog"),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument>
            { IsUpsert = true, ReturnDocument = ReturnDocument.After });
        doc.Id = seq["seq"].ToInt32();
        await _waste.InsertOneAsync(doc);
        return doc;
    }

    public Task<List<WasteDoc>> SinceAsync(int storeId, DateTime since) =>
        _waste.Find(w => w.StoreId == storeId && w.At >= since)
              .SortByDescending(w => w.Id).ToListAsync();

    public Task<WasteDoc?> GetAsync(int storeId, int id) =>
        _waste.Find(w => w.Id == id && w.StoreId == storeId).FirstOrDefaultAsync()!;

    /// <summary>Undo: the entry goes, and the caller puts the stock back.</summary>
    public Task DeleteAsync(int storeId, int id) =>
        _waste.DeleteOneAsync(w => w.Id == id && w.StoreId == storeId);
}
