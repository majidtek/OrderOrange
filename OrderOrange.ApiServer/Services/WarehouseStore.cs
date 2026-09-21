using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>A place a shop keeps stock.</summary>
public sealed class WarehouseDoc
{
    [BsonId] public int Id { get; set; }
    public int StoreId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "main";
    public string? Notes { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The picture on the map: factory, truck, warehouse, store, restaurant, and so on.</summary>
    public string Icon { get; set; } = "warehouse";
    /// <summary>Where it sits on the map, in percent of the canvas, so any screen size agrees.</summary>
    public double X { get; set; } = 20;
    public double Y { get; set; } = 30;
    /// <summary>Ids of the places this one is drawn as feeding: the lines.</summary>
    public List<int> Links { get; set; } = [];
    /// <summary>The owner's central warehouse: every branch of the same owner can draw from it. One per owner.</summary>
    public bool IsCentral { get; set; }
}

/// <summary>How much of one material sits in one place.</summary>
public sealed class WarehouseStockDoc
{
    /// <summary>"&lt;store&gt;:&lt;warehouse&gt;:&lt;material&gt;" — one row per material per place.</summary>
    [BsonId] public string Id { get; set; } = "";
    public int StoreId { get; set; }
    public int WarehouseId { get; set; }
    public int MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A move that happened, kept so it can be read back.</summary>
public sealed class TransferDoc
{
    [BsonId] public int Id { get; set; }
    public int StoreId { get; set; }
    /// <summary>Null when the stock came from the unassigned pool.</summary>
    public int? FromId { get; set; }
    public string FromName { get; set; } = "";
    public int ToId { get; set; }
    public string ToName { get; set; } = "";
    public List<TransferLineDoc> Lines { get; set; } = [];
    public decimal TotalValue { get; set; }
    public string? Note { get; set; }
    public string By { get; set; } = "";
    public DateTime At { get; set; }
}

public sealed class TransferLineDoc
{
    public int MaterialId { get; set; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

/// <summary>
/// Where the shop's places, their balances and the moves between them live.
///
/// Mongo rather than SQL for the usual reason — the schema is created by EnsureCreated and
/// a new table could never reach a live database — but also because of what these rows
/// ARE: the authoritative quantity of a material stays on the materials page, and this is
/// a BREAKDOWN of it. A transfer moves stock between two places and leaves the shop's
/// total untouched, so it never has to write to SQL at all.
/// </summary>
public sealed class WarehouseStore
{
    private readonly IMongoCollection<WarehouseDoc> _places;
    private readonly IMongoCollection<WarehouseStockDoc> _stock;
    private readonly IMongoCollection<TransferDoc> _moves;
    private readonly IMongoCollection<BsonDocument> _counters;

    public WarehouseStore(IMongoDatabase database)
    {
        _places = database.GetCollection<WarehouseDoc>("warehouses");
        _stock = database.GetCollection<WarehouseStockDoc>("warehouseStock");
        _moves = database.GetCollection<TransferDoc>("warehouseTransfers");
        _counters = database.GetCollection<BsonDocument>("counters");

        _places.Indexes.CreateOne(new CreateIndexModel<WarehouseDoc>(
            Builders<WarehouseDoc>.IndexKeys.Ascending(w => w.StoreId)));
        _stock.Indexes.CreateOne(new CreateIndexModel<WarehouseStockDoc>(
            Builders<WarehouseStockDoc>.IndexKeys.Ascending(s => s.StoreId).Ascending(s => s.WarehouseId)));
        _moves.Indexes.CreateOne(new CreateIndexModel<TransferDoc>(
            Builders<TransferDoc>.IndexKeys.Ascending(m => m.StoreId).Descending(m => m.At)));
    }

    private async Task<int> NextAsync(string name)
    {
        var seq = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", name),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument>
            { IsUpsert = true, ReturnDocument = ReturnDocument.After });
        return seq["seq"].ToInt32();
    }

    // ───────────────────────────── places ─────────────────────────────

    public Task<List<WarehouseDoc>> PlacesAsync(int storeId) =>
        _places.Find(w => w.StoreId == storeId).SortBy(w => w.Id).ToListAsync();

    public Task<WarehouseDoc?> PlaceAsync(int storeId, int id) =>
        _places.Find(w => w.Id == id && w.StoreId == storeId).FirstOrDefaultAsync()!;

    public async Task<WarehouseDoc> AddPlaceAsync(WarehouseDoc doc)
    {
        doc.Id = await NextAsync("warehouses");
        doc.CreatedAt = DateTime.Now;
        // The first place a shop makes is the one everything else defaults to.
        if (!await _places.Find(w => w.StoreId == doc.StoreId).AnyAsync()) doc.IsDefault = true;
        if (doc.IsDefault) await ClearDefaultAsync(doc.StoreId);
        await _places.InsertOneAsync(doc);
        return doc;
    }

    public Task ClearDefaultAsync(int storeId) =>
        _places.UpdateManyAsync(w => w.StoreId == storeId,
            Builders<WarehouseDoc>.Update.Set(w => w.IsDefault, false));

    public Task PlaceNodeAsync(int storeId, int id, double x, double y) =>
        _places.UpdateOneAsync(w => w.Id == id && w.StoreId == storeId,
            Builders<WarehouseDoc>.Update.Set(w => w.X, x).Set(w => w.Y, y));

    public Task LinkAsync(int storeId, int fromId, int toId) =>
        _places.UpdateOneAsync(w => w.Id == fromId && w.StoreId == storeId,
            Builders<WarehouseDoc>.Update.AddToSet(w => w.Links, toId));

    public Task UnlinkAsync(int storeId, int fromId, int toId) =>
        _places.UpdateOneAsync(w => w.Id == fromId && w.StoreId == storeId,
            Builders<WarehouseDoc>.Update.Pull(w => w.Links, toId));

    // ----- the central place: found and cleared across ALL of an owner's stores
    public Task<WarehouseDoc?> CentralAsync(List<int> storeIds) =>
        _places.Find(w => storeIds.Contains(w.StoreId) && w.IsCentral).FirstOrDefaultAsync()!;

    public async Task MakeCentralAsync(List<int> storeIds, int storeId, int id)
    {
        await _places.UpdateManyAsync(w => storeIds.Contains(w.StoreId) && w.IsCentral,
            Builders<WarehouseDoc>.Update.Set(w => w.IsCentral, false));
        await _places.UpdateOneAsync(w => w.Id == id && w.StoreId == storeId,
            Builders<WarehouseDoc>.Update.Set(w => w.IsCentral, true));
    }

    public Task ClearCentralAsync(int storeId, int id) =>
        _places.UpdateOneAsync(w => w.Id == id && w.StoreId == storeId,
            Builders<WarehouseDoc>.Update.Set(w => w.IsCentral, false));

    public async Task UpdatePlaceAsync(int storeId, int id, string name, string kind, string? notes, bool makeDefault, string icon)
    {
        if (makeDefault) await ClearDefaultAsync(storeId);
        var update = Builders<WarehouseDoc>.Update
            .Set(w => w.Name, name).Set(w => w.Kind, kind).Set(w => w.Notes, notes).Set(w => w.Icon, icon);
        if (makeDefault) update = update.Set(w => w.IsDefault, true);
        await _places.UpdateOneAsync(w => w.Id == id && w.StoreId == storeId, update);
    }

    public async Task RemovePlaceAsync(int storeId, int id)
    {
        await _places.DeleteOneAsync(w => w.Id == id && w.StoreId == storeId);
        await _stock.DeleteManyAsync(s => s.StoreId == storeId && s.WarehouseId == id);
        // and every line that pointed at it
        await _places.UpdateManyAsync(w => w.StoreId == storeId,
            Builders<WarehouseDoc>.Update.Pull(w => w.Links, id));
    }

    // ───────────────────────────── balances ─────────────────────────────

    public Task<List<WarehouseStockDoc>> StockAsync(int storeId) =>
        _stock.Find(s => s.StoreId == storeId && s.Quantity > 0).ToListAsync();

    public Task<List<WarehouseStockDoc>> StockAsync(int storeId, int warehouseId) =>
        _stock.Find(s => s.StoreId == storeId && s.WarehouseId == warehouseId && s.Quantity > 0).ToListAsync();

    private static string Key(int storeId, int warehouseId, int materialId) =>
        $"{storeId}:{warehouseId}:{materialId}";

    public async Task<decimal> BalanceAsync(int storeId, int warehouseId, int materialId)
    {
        var row = await _stock.Find(s => s.Id == Key(storeId, warehouseId, materialId)).FirstOrDefaultAsync();
        return row?.Quantity ?? 0m;
    }

    /// <summary>Adds to a balance, or takes away with a negative amount. Never goes below zero.</summary>
    public async Task MoveAsync(int storeId, int warehouseId, int materialId, decimal delta)
    {
        var id = Key(storeId, warehouseId, materialId);
        var existing = await _stock.Find(s => s.Id == id).FirstOrDefaultAsync();
        var quantity = Math.Max(0m, (existing?.Quantity ?? 0m) + delta);

        await _stock.ReplaceOneAsync(s => s.Id == id, new WarehouseStockDoc
        {
            Id = id,
            StoreId = storeId,
            WarehouseId = warehouseId,
            MaterialId = materialId,
            Quantity = quantity,
            UpdatedAt = DateTime.Now,
        }, new ReplaceOptions { IsUpsert = true });
    }

    /// <summary>Forgets a material everywhere — used when the material itself is gone.</summary>
    public Task ForgetMaterialAsync(int storeId, int materialId) =>
        _stock.DeleteManyAsync(s => s.StoreId == storeId && s.MaterialId == materialId);

    // ───────────────────────────── moves ─────────────────────────────

    public async Task<TransferDoc> AddTransferAsync(TransferDoc doc)
    {
        doc.Id = await NextAsync("warehouseTransfers");
        doc.At = DateTime.Now;
        await _moves.InsertOneAsync(doc);
        return doc;
    }

    public Task<List<TransferDoc>> TransfersAsync(int storeId, int take = 60) =>
        _moves.Find(m => m.StoreId == storeId).SortByDescending(m => m.Id).Limit(take).ToListAsync();
}
