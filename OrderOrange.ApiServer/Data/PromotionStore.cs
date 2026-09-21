using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>A store's own discount code. See <see cref="OrderOrange.Shared.PromoCatalog"/> for the vocabularies.</summary>
public sealed class PromotionDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public int RestaurantId { get; set; }
    /// <summary>Upper-case, unique within the store.</summary>
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Type { get; set; } = "percent";
    public decimal Value { get; set; }
    /// <summary>Cap for a percent discount; 0 = no cap.</summary>
    public decimal MaxDiscount { get; set; }
    public decimal MinOrder { get; set; }
    public string Rule { get; set; } = "any";
    public int RuleValue { get; set; }
    public decimal RuleAmount { get; set; }
    public string OrderTypes { get; set; } = "all";
    /// <summary>DayOfWeek numbers (0 = Sunday); empty = every day.</summary>
    public List<int> Days { get; set; } = [];
    public int? StartHour { get; set; }
    public int? EndHour { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime? StartsAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime? EndsAt { get; set; }
    public int MaxUses { get; set; }
    public int MaxUsesPerCustomer { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPublic { get; set; } = true;
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime CreatedAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Promotions live in MongoDB beside surveys and tickets. Usage is NOT counted here —
/// every order already records the code it used (Orders.CouponCode), and the order book
/// is the truth about what was redeemed and what it saved.
/// </summary>
public sealed class PromotionStore(IMongoDatabase database)
{
    private readonly IMongoCollection<PromotionDoc> _promos =
        database.GetCollection<PromotionDoc>(CollectionNames.StorePromotions);

    public async Task EnsureIndexesAsync()
    {
        await _promos.Indexes.CreateOneAsync(new CreateIndexModel<PromotionDoc>(
            Builders<PromotionDoc>.IndexKeys.Ascending(p => p.RestaurantId).Ascending(p => p.Code),
            new CreateIndexOptions { Unique = true }));
    }

    public Task<List<PromotionDoc>> ListAsync(int restaurantId) =>
        _promos.Find(p => p.RestaurantId == restaurantId).SortByDescending(p => p.IsActive).ThenByDescending(p => p.CreatedAt).ToListAsync();

    public Task<PromotionDoc?> GetAsync(int restaurantId, ObjectId id) =>
        _promos.Find(p => p.Id == id && p.RestaurantId == restaurantId).FirstOrDefaultAsync()!;

    /// <summary>The store's code, if it has one by that name — codes are stored upper-case.</summary>
    public Task<PromotionDoc?> FindAsync(int restaurantId, string code) =>
        _promos.Find(p => p.RestaurantId == restaurantId && p.Code == code).FirstOrDefaultAsync()!;

    public Task<bool> CodeTakenAsync(int restaurantId, string code, ObjectId? except) =>
        _promos.Find(p => p.RestaurantId == restaurantId && p.Code == code && (except == null || p.Id != except)).AnyAsync();

    public Task<long> CountAsync(int restaurantId) => _promos.CountDocumentsAsync(p => p.RestaurantId == restaurantId);

    public Task InsertAsync(PromotionDoc promo) => _promos.InsertOneAsync(promo);

    public Task ReplaceAsync(PromotionDoc promo) =>
        _promos.ReplaceOneAsync(p => p.Id == promo.Id && p.RestaurantId == promo.RestaurantId, promo);

    public Task DeleteAsync(int restaurantId, ObjectId id) =>
        _promos.DeleteOneAsync(p => p.Id == id && p.RestaurantId == restaurantId);

    /// <summary>What guests may see: live, public, inside its dates.</summary>
    public async Task<List<PromotionDoc>> PublicAsync(int restaurantId)
    {
        var now = DateTime.Now;
        var list = await _promos.Find(p => p.RestaurantId == restaurantId && p.IsActive && p.IsPublic).ToListAsync();
        return list.Where(p => (p.StartsAt is null || p.StartsAt <= now) && (p.EndsAt is null || p.EndsAt >= now))
                   .OrderByDescending(p => p.Type == "percent" ? p.Value : 0).ThenBy(p => p.Code).ToList();
    }
}
