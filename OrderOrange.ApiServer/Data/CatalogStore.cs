using OrderOrange.Shared;
using MongoDB.Bson;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>One pending product waiting on the admin queue, with its store's name.</summary>
public sealed record PendingProduct(
    int Id, int RestaurantId, string RestaurantName, string CategoryName,
    string Name, string Description, decimal Price, string ImageEmoji,
    string? Photo, DateTime SubmittedAt);

/// <summary>
/// The product catalog in MongoDB: categories, items and their photos.
///
/// Customers only ever see <see cref="ProductStatus.Approved"/> items — that filter is
/// applied here rather than left to each caller, so a new endpoint cannot accidentally
/// leak a product that is still waiting for review.
/// </summary>
public sealed class CatalogStore
{
    private readonly IMongoCollection<MenuItemDoc> _items;
    private readonly IMongoCollection<MenuCategoryDoc> _categories;
    private readonly IMongoCollection<MenuItemPhotoDoc> _itemPhotos;
    private readonly IMongoCollection<RestaurantDoc> _restaurants;
    private readonly IMongoCollection<BsonDocument> _counters;

    private readonly IMongoDatabase _database;

    public CatalogStore(IMongoDatabase database)
    {
        _database = database;
        _items = database.GetCollection<MenuItemDoc>(CollectionNames.MenuItems);
        _categories = database.GetCollection<MenuCategoryDoc>(CollectionNames.MenuCategories);
        _itemPhotos = database.GetCollection<MenuItemPhotoDoc>(CollectionNames.MenuItemPhotos);
        _restaurants = database.GetCollection<RestaurantDoc>(CollectionNames.Restaurants);
        _counters = database.GetCollection<BsonDocument>("counters");
    }

    /// <summary>What the catalog holds for one store — counted for the delete warning.</summary>
    public async Task<(int Categories, int Items, int TableChats)> StoreFootprintAsync(int restaurantId)
    {
        var categories = (int)await _categories.CountDocumentsAsync(c => c.RestaurantId == restaurantId);
        var items = (int)await _items.CountDocumentsAsync(i => i.RestaurantId == restaurantId);
        var chats = (int)await _database.GetCollection<BsonDocument>("tableChats")
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("RestaurantId", restaurantId));
        return (categories, items, chats);
    }

    /// <summary>
    /// The hard-delete broom: every trace of one store leaves the catalog — menu,
    /// photos, its restaurant document, and its table chats.
    /// </summary>
    public async Task DeleteStoreAsync(int restaurantId)
    {
        var itemIds = await _items.Find(i => i.RestaurantId == restaurantId)
            .Project(i => i.Id).ToListAsync();
        if (itemIds.Count > 0)
            await _itemPhotos.DeleteManyAsync(p => itemIds.Contains(p.MenuItemId));
        await _items.DeleteManyAsync(i => i.RestaurantId == restaurantId);
        await _categories.DeleteManyAsync(c => c.RestaurantId == restaurantId);
        await _restaurants.DeleteManyAsync(r => r.Id == restaurantId);

        var byStore = Builders<BsonDocument>.Filter.Eq("RestaurantId", restaurantId);
        await _database.GetCollection<BsonDocument>("tableChats").DeleteManyAsync(byStore);
        await _database.GetCollection<BsonDocument>("tableChatReads").DeleteManyAsync(byStore);
    }

    /// <summary>Not flagged in-store only (older documents have no field at all, so "not true").</summary>
    private static FilterDefinition<MenuItemDoc> PublicOnly =>
        Builders<MenuItemDoc>.Filter.Ne(i => i.InStoreOnly, true);

    private static FilterDefinition<MenuItemDoc> Visible =>
        Builders<MenuItemDoc>.Filter.Eq(i => i.Status, ProductStatus.Approved);

    // ---------- Customer reads (approved only) ----------

    /// <summary>The menu a customer sees: approved items grouped into their categories.</summary>
    /// <param name="includeInStoreOnly">True for the table-QR menu: guests at the table may order
    /// what the public web page hides.</param>
    public async Task<List<MenuCategoryDto>> PublicMenuAsync(int restaurantId, bool includeInStoreOnly = false)
    {
        var categories = await _categories.Find(c => c.RestaurantId == restaurantId)
            .SortBy(c => c.SortOrder).ToListAsync();
        var filter = Builders<MenuItemDoc>.Filter.And(
            Builders<MenuItemDoc>.Filter.Eq(i => i.RestaurantId, restaurantId), Visible);
        if (!includeInStoreOnly) filter &= PublicOnly;
        var items = await _items.Find(filter).ToListAsync();

        var byCategory = items.GroupBy(i => i.CategoryId).ToDictionary(g => g.Key, g => g.ToList());
        return categories
            .Select(c => new MenuCategoryDto(c.Id, c.Name, c.SortOrder,
                (byCategory.GetValueOrDefault(c.Id) ?? [])
                    .OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(i => i.ToDto()).ToList(), c.Names))
            .Where(c => c.Items.Count > 0)
            .ToList();
    }

    public Task<List<MenuItemDoc>> ApprovedItemsAsync(IReadOnlyCollection<int> ids) =>
        _items.Find(Builders<MenuItemDoc>.Filter.And(
            Builders<MenuItemDoc>.Filter.In(i => i.Id, ids), Visible)).ToListAsync();

    public Task<MenuItemDoc?> ApprovedItemAsync(int id) =>
        _items.Find(Builders<MenuItemDoc>.Filter.And(
            Builders<MenuItemDoc>.Filter.Eq(i => i.Id, id), Visible)).FirstOrDefaultAsync()!;

    /// <summary>Discounted approved items, richest saving first — the home Offers strip.</summary>
    public Task<List<MenuItemDoc>> DealsAsync(int take) =>
        _items.Find(Builders<MenuItemDoc>.Filter.And(
                Visible, PublicOnly,
                Builders<MenuItemDoc>.Filter.Gt(i => i.DiscountPercent, 0),
                Builders<MenuItemDoc>.Filter.Eq(i => i.IsAvailable, true)))
            .SortByDescending(i => i.DiscountPercent).ThenBy(i => i.Id)
            .Limit(take)
            .ToListAsync();

    /// <summary>
    /// Approved, available items from every store, paged — the tail of the customer home,
    /// which carries on into individual dishes once the shop grid runs out. Ordered by id
    /// so paging is stable: any ordering that could change between two requests would show
    /// the same dish twice and skip another.
    /// </summary>
    /// <param name="minPrice">Dishes cheaper than this stay off the home page (water, bags, sauces) — filtered here so the pages stay whole.</param>
    public Task<List<MenuItemDoc>> AllItemsAsync(int skip, int take, decimal minPrice = 0m) =>
        _items.Find(Builders<MenuItemDoc>.Filter.And(
                Visible, PublicOnly,
                Builders<MenuItemDoc>.Filter.Eq(i => i.IsAvailable, true),
                Builders<MenuItemDoc>.Filter.Gte(i => i.Price, minPrice)))
            .SortBy(i => i.Id)
            .Skip(skip)
            .Limit(take)
            .ToListAsync();

    /// <summary>Approved items whose name or multilingual keywords match a search term.</summary>
    public Task<List<MenuItemDoc>> SearchAsync(string term, int take)
    {
        var lowered = term.ToLowerInvariant();
        return _items.Find(Builders<MenuItemDoc>.Filter.And(
                Visible, PublicOnly,
                Builders<MenuItemDoc>.Filter.Or(
                    Builders<MenuItemDoc>.Filter.Regex(i => i.Name,
                        new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(lowered), "i")),
                    Builders<MenuItemDoc>.Filter.Regex(i => i.SearchKeywords,
                        new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(lowered), "i")))))
            .Limit(take)
            .ToListAsync();
    }

    // ---------- Partner reads (their own menu, pending included) ----------

    /// <summary>
    /// The owner's full menu including anything still pending or rejected — they need
    /// to see what they submitted and why it was turned down.
    /// </summary>
    public async Task<List<MenuCategoryDto>> OwnerMenuAsync(int restaurantId)
    {
        var categories = await _categories.Find(c => c.RestaurantId == restaurantId)
            .SortBy(c => c.SortOrder).ToListAsync();
        var items = await _items.Find(i => i.RestaurantId == restaurantId).ToListAsync();
        var byCategory = items.GroupBy(i => i.CategoryId).ToDictionary(g => g.Key, g => g.ToList());

        return categories.Select(c => new MenuCategoryDto(c.Id, c.Name, c.SortOrder,
            (byCategory.GetValueOrDefault(c.Id) ?? [])
                .OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(i => i.ToDto()).ToList(), c.Names)).ToList();
    }

    public Task<MenuItemDoc?> OwnedItemAsync(int id, int restaurantId) =>
        _items.Find(i => i.Id == id && i.RestaurantId == restaurantId).FirstOrDefaultAsync()!;

    public Task<bool> OwnsCategoryAsync(int categoryId, int restaurantId) =>
        _categories.Find(c => c.Id == categoryId && c.RestaurantId == restaurantId).AnyAsync();

    public Task<int> PendingCountAsync(int restaurantId) =>
        _items.CountDocumentsAsync(i => i.RestaurantId == restaurantId && i.Status == ProductStatus.Pending)
            .ContinueWith(t => (int)t.Result);

    // ---------- Partner writes ----------

    /// <summary>Adds a product. Always starts pending — this is the review gate.</summary>
    public async Task<MenuItemDoc> AddItemAsync(MenuItemDoc item)
    {
        item.Id = await NextIdAsync(CollectionNames.MenuItems);
        item.Status = ProductStatus.Pending;
        item.SubmittedAt = DateTime.Now;
        item.ReviewedAt = null;
        item.ReviewedByUserId = null;
        item.RejectionReason = null;
        await _items.InsertOneAsync(item);
        return item;
    }

    /// <summary>
    /// Applies an edit. The status is deliberately untouched: an approved product stays
    /// live, and a pending one stays in the queue.
    /// </summary>
    public Task UpdateItemAsync(MenuItemDoc item) =>
        _items.ReplaceOneAsync(i => i.Id == item.Id, item);

    /// <summary>
    /// The rows an order is priced from: this store's, approved, by id. Ordering reads
    /// the SAME store the menus render from — a product a customer can see is a product
    /// they can buy, and one they can't see never sneaks into a basket by id.
    /// </summary>
    public Task<List<MenuItemDoc>> ItemsForOrderAsync(int restaurantId, List<int> ids) =>
        _items.Find(i => i.RestaurantId == restaurantId && ids.Contains(i.Id) && i.Status == ProductStatus.Approved)
              .ToListAsync();

    public Task SetAvailabilityAsync(int id, bool available) =>
        _items.UpdateOneAsync(i => i.Id == id,
            Builders<MenuItemDoc>.Update.Set(i => i.IsAvailable, available));

    public async Task DeleteItemAsync(int id)
    {
        await _items.DeleteOneAsync(i => i.Id == id);
        await _itemPhotos.DeleteManyAsync(p => p.MenuItemId == id);
    }

    // ---------- Categories ----------

    public Task<List<MenuCategoryDoc>> CategoriesAsync(int restaurantId) =>
        _categories.Find(c => c.RestaurantId == restaurantId).SortBy(c => c.SortOrder).ToListAsync();

    public async Task<MenuCategoryDoc> AddCategoryAsync(MenuCategoryDoc category)
    {
        category.Id = await NextIdAsync(CollectionNames.MenuCategories);
        await _categories.InsertOneAsync(category);
        return category;
    }

    public Task UpdateCategoryAsync(MenuCategoryDoc category) =>
        _categories.ReplaceOneAsync(c => c.Id == category.Id, category);

    public async Task DeleteCategoryAsync(int categoryId, int restaurantId)
    {
        var items = await _items.Find(i => i.CategoryId == categoryId && i.RestaurantId == restaurantId)
            .Project(i => i.Id).ToListAsync();
        if (items.Count > 0)
        {
            await _items.DeleteManyAsync(i => items.Contains(i.Id));
            await _itemPhotos.DeleteManyAsync(p => items.Contains(p.MenuItemId));
        }
        await _categories.DeleteOneAsync(c => c.Id == categoryId && c.RestaurantId == restaurantId);
    }

    // ---------- Item photos ----------

    public Task<List<MenuItemPhotoDoc>> ItemPhotosAsync(int menuItemId) =>
        _itemPhotos.Find(p => p.MenuItemId == menuItemId)
            .SortByDescending(p => p.IsMain).ThenBy(p => p.Id).ToListAsync();

    public async Task ReplaceItemPhotosAsync(int menuItemId, List<string> photos, int mainIndex)
    {
        await _itemPhotos.DeleteManyAsync(p => p.MenuItemId == menuItemId);
        if (mainIndex < 0 || mainIndex >= photos.Count) mainIndex = 0;

        if (photos.Count > 0)
        {
            var docs = new List<MenuItemPhotoDoc>(photos.Count);
            for (var i = 0; i < photos.Count; i++)
                docs.Add(new MenuItemPhotoDoc
                {
                    Id = await NextIdAsync(CollectionNames.MenuItemPhotos),
                    MenuItemId = menuItemId,
                    Data = photos[i],
                    IsMain = i == mainIndex,
                    CreatedAt = DateTime.Now,
                });
            await _itemPhotos.InsertManyAsync(docs);
        }

        await _items.UpdateOneAsync(i => i.Id == menuItemId,
            Builders<MenuItemDoc>.Update.Set(i => i.PhotoData, photos.Count == 0 ? null : photos[mainIndex]));
    }

    // ---------- Admin moderation ----------

    /// <summary>The review queue, oldest submission first so nobody waits forever.</summary>
    public async Task<List<PendingProduct>> PendingAsync(int skip, int take)
    {
        var pending = await _items.Find(i => i.Status == ProductStatus.Pending)
            .SortBy(i => i.SubmittedAt).Skip(Math.Max(0, skip)).Limit(Math.Clamp(take, 1, 100))
            .ToListAsync();
        if (pending.Count == 0) return [];

        var storeIds = pending.Select(i => i.RestaurantId).Distinct().ToList();
        var stores = (await _restaurants.Find(r => storeIds.Contains(r.Id))
                .Project(r => new { r.Id, r.Name }).ToListAsync())
            .ToDictionary(r => r.Id, r => r.Name);

        var categoryIds = pending.Select(i => i.CategoryId).Distinct().ToList();
        var categories = (await _categories.Find(c => categoryIds.Contains(c.Id))
                .Project(c => new { c.Id, c.Name }).ToListAsync())
            .ToDictionary(c => c.Id, c => c.Name);

        return pending.Select(i => new PendingProduct(
            i.Id, i.RestaurantId, stores.GetValueOrDefault(i.RestaurantId) ?? $"#{i.RestaurantId}",
            categories.GetValueOrDefault(i.CategoryId) ?? "—",
            i.Name, i.Description, i.Price, i.ImageEmoji, i.PhotoData,
            i.SubmittedAt ?? DateTime.Now)).ToList();
    }

    public Task<int> PendingTotalAsync() =>
        _items.CountDocumentsAsync(i => i.Status == ProductStatus.Pending).ContinueWith(t => (int)t.Result);

    public async Task<bool> ApproveAsync(int itemId, int adminUserId)
    {
        var result = await _items.UpdateOneAsync(
            i => i.Id == itemId && i.Status == ProductStatus.Pending,
            Builders<MenuItemDoc>.Update
                .Set(i => i.Status, ProductStatus.Approved)
                .Set(i => i.ReviewedAt, DateTime.Now)
                .Set(i => i.ReviewedByUserId, adminUserId)
                .Set(i => i.RejectionReason, null));
        return result.ModifiedCount > 0;
    }

    public async Task<bool> RejectAsync(int itemId, int adminUserId, string? reason)
    {
        var result = await _items.UpdateOneAsync(
            i => i.Id == itemId && i.Status == ProductStatus.Pending,
            Builders<MenuItemDoc>.Update
                .Set(i => i.Status, ProductStatus.Rejected)
                .Set(i => i.ReviewedAt, DateTime.Now)
                .Set(i => i.ReviewedByUserId, adminUserId)
                .Set(i => i.RejectionReason, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()));
        return result.ModifiedCount > 0;
    }

    // ---------- ids ----------

    /// <summary>Same sequence trick the chat uses, so ids stay the integers the apps expect.</summary>
    // ---------- The default shelf ----------

    private static readonly Dictionary<string, string> DrinksNames = new()
    {
        ["en"] = "Drinks", ["ar"] = "المشروبات", ["fa"] = "نوشیدنی‌ها", ["ur"] = "مشروبات",
        ["hi"] = "पेय", ["tr"] = "İçecekler", ["fr"] = "Boissons", ["es"] = "Bebidas",
        ["de"] = "Getränke", ["ru"] = "Напитки", ["it"] = "Bevande", ["pt"] = "Bebidas",
        ["zh"] = "饮品", ["ja"] = "ドリンク",
    };

    private static readonly Dictionary<string, string> WaterNames = new()
    {
        ["en"] = "Water", ["ar"] = "ماء", ["fa"] = "آب", ["ur"] = "پانی",
        ["hi"] = "पानी", ["tr"] = "Su", ["fr"] = "Eau", ["es"] = "Agua",
        ["de"] = "Wasser", ["ru"] = "Вода", ["it"] = "Acqua", ["pt"] = "Água",
        ["zh"] = "水", ["ja"] = "水",
    };

    /// <summary>
    /// Every store opens with one shelf already stocked: a Drinks category
    /// holding a bottle of water, named in all fourteen tongues. Idempotent —
    /// a store that already has a Drinks shelf is left exactly as it stands.
    /// </summary>
    public async Task SeedDefaultMenuAsync(int restaurantId)
    {
        var existing = await _categories.Find(c => c.RestaurantId == restaurantId).ToListAsync();
        if (existing.Any(c => c.Name == "Drinks" ||
                (c.Names is not null && c.Names.TryGetValue("ar", out var arabic) && arabic == "المشروبات")))
            return;

        var category = await AddCategoryAsync(new MenuCategoryDoc
        {
            RestaurantId = restaurantId,
            Name = "Drinks",
            SortOrder = 99, // the food a store adds later stands in front of the fridge
            Names = new Dictionary<string, string>(DrinksNames),
        });

        // Inserted directly, NOT via AddItemAsync: the house water is the
        // platform's own bottle and needs no admin review.
        await _items.InsertOneAsync(new MenuItemDoc
        {
            Id = await NextIdAsync(CollectionNames.MenuItems),
            RestaurantId = restaurantId,
            CategoryId = category.Id,
            Name = "Water",
            Names = new Dictionary<string, string>(WaterNames),
            Description = "",
            Price = 0.100m,
            IsAvailable = true,
            ImageEmoji = "💧",
            SearchKeywords = Services.SearchAliases.KeywordsFor("Water"),
            Status = ProductStatus.Approved,
        });
    }

    private async Task<int> NextIdAsync(string collection)
    {
        var updated = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", collection),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After,
            });
        return updated["seq"].ToInt32();
    }

    /// <summary>
    /// Seeds each id sequence past the highest id that came over from SQL, so a newly
    /// created product can never collide with a migrated one.
    /// </summary>
    public async Task EnsureSequencesAsync()
    {
        await SeedAsync(CollectionNames.MenuItems,
            await _items.Find(FilterDefinition<MenuItemDoc>.Empty).SortByDescending(i => i.Id)
                .Limit(1).Project(i => i.Id).FirstOrDefaultAsync());
        await SeedAsync(CollectionNames.MenuCategories,
            await _categories.Find(FilterDefinition<MenuCategoryDoc>.Empty).SortByDescending(c => c.Id)
                .Limit(1).Project(c => c.Id).FirstOrDefaultAsync());
        await SeedAsync(CollectionNames.MenuItemPhotos,
            await _itemPhotos.Find(FilterDefinition<MenuItemPhotoDoc>.Empty).SortByDescending(p => p.Id)
                .Limit(1).Project(p => p.Id).FirstOrDefaultAsync());

        async Task SeedAsync(string collection, int highest)
        {
            if (highest <= 0) return;
            // $max in one atomic upsert: creates the counter at `highest` when missing,
            // raises it only if lower. The old `seq <` filter could not match an
            // existing higher counter, so the upsert tried to insert a duplicate _id
            // and threw on every boot after the first.
            await _counters.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", collection),
                Builders<BsonDocument>.Update.Max("seq", highest),
                new UpdateOptions { IsUpsert = true });
        }
    }
}
