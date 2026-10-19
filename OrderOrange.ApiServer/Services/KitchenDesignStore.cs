using System.Text.Json;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Each store's kitchen-ticket design (partner Invoice designer → "Kitchen ticket"), kept in
/// Mongo <c>kitchenDesigns</c> as the DTO's JSON — no SQL column, because the SQL schema is
/// EnsureCreated and never grows a column on an existing database.
/// </summary>
public sealed class KitchenDesignStore(IMongoDatabase database)
{
    public sealed class Doc
    {
        [BsonId] public int Id { get; set; }
        public string Json { get; set; } = "";
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime UpdatedAt { get; set; }
    }

    private IMongoCollection<Doc> Col => database.GetCollection<Doc>("kitchenDesigns");

    public async Task<KitchenDesignDto> GetAsync(int storeId)
    {
        var doc = await Col.Find(d => d.Id == storeId).FirstOrDefaultAsync();
        if (doc is null || string.IsNullOrWhiteSpace(doc.Json)) return KitchenDesignDto.Default;
        try { return JsonSerializer.Deserialize<KitchenDesignDto>(doc.Json) ?? KitchenDesignDto.Default; }
        catch (JsonException) { return KitchenDesignDto.Default; }
    }

    public Task SaveAsync(int storeId, KitchenDesignDto design) =>
        Col.ReplaceOneAsync(d => d.Id == storeId,
            new Doc { Id = storeId, Json = JsonSerializer.Serialize(design), UpdatedAt = DateTime.Now },
            new ReplaceOptions { IsUpsert = true });
}