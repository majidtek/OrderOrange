using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>
/// The indexes the catalog queries depend on. Created once at startup; MongoDB
/// ignores a create for an index that already exists, so this is safe to re-run.
/// </summary>
public static class CatalogIndexes
{
    public static async Task EnsureAsync(IMongoDatabase db, CancellationToken ct = default)
    {
        var restaurants = db.GetCollection<RestaurantDoc>(CollectionNames.Restaurants);
        var keys = Builders<RestaurantDoc>.IndexKeys;

        await restaurants.Indexes.CreateManyAsync(
        [
            // Browse always filters on approved + vertical; the trailing field is
            // whichever ordering the customer picked.
            new CreateIndexModel<RestaurantDoc>(
                keys.Ascending(r => r.IsApproved).Ascending(r => r.StoreType)
                    .Descending(r => r.IsOpen).Ascending(r => r.Name),
                new CreateIndexOptions { Name = "browse_name" }),
            new CreateIndexModel<RestaurantDoc>(
                keys.Ascending(r => r.IsApproved).Ascending(r => r.StoreType)
                    .Descending(r => r.IsOpen).Ascending(r => r.DeliveryFee),
                new CreateIndexOptions { Name = "browse_fee" }),
            new CreateIndexModel<RestaurantDoc>(
                keys.Ascending(r => r.IsApproved).Ascending(r => r.StoreType)
                    .Descending(r => r.IsOpen).Ascending(r => r.AvgPrepMinutes),
                new CreateIndexOptions { Name = "browse_fastest" }),
            new CreateIndexModel<RestaurantDoc>(
                keys.Ascending(r => r.IsApproved).Ascending(r => r.StoreType)
                    .Descending(r => r.Rating).Descending(r => r.RatingCount),
                new CreateIndexOptions { Name = "browse_rating" }),

            // Cuisine chips narrow before ordering.
            new CreateIndexModel<RestaurantDoc>(
                keys.Ascending(r => r.IsApproved).Ascending(r => r.CuisineId).Ascending(r => r.StoreType),
                new CreateIndexOptions { Name = "browse_cuisine" }),

            // "Near you" — a real geospatial index instead of the widening bounding
            // box the SQL version had to use.
            new CreateIndexModel<RestaurantDoc>(
                keys.Geo2DSphere(r => r.Location),
                new CreateIndexOptions { Name = "near_me", Sparse = true }),

            new CreateIndexModel<RestaurantDoc>(
                keys.Ascending(r => r.OwnerUserId),
                new CreateIndexOptions { Name = "by_owner" }),
        ], ct);

        var items = db.GetCollection<MenuItemDoc>(CollectionNames.MenuItems);
        await items.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<MenuItemDoc>(
                Builders<MenuItemDoc>.IndexKeys.Ascending(i => i.RestaurantId).Ascending(i => i.CategoryId),
                new CreateIndexOptions { Name = "by_store" }),
            new CreateIndexModel<MenuItemDoc>(
                Builders<MenuItemDoc>.IndexKeys.Descending(i => i.DiscountPercent),
                new CreateIndexOptions { Name = "by_discount" }),
            // Dish search matches the multilingual keyword blob as well as the title.
            new CreateIndexModel<MenuItemDoc>(
                Builders<MenuItemDoc>.IndexKeys.Text(i => i.Name).Text(i => i.SearchKeywords),
                new CreateIndexOptions { Name = "dish_text" }),
        ], ct);

        await db.GetCollection<MenuCategoryDoc>(CollectionNames.MenuCategories).Indexes.CreateOneAsync(
            new CreateIndexModel<MenuCategoryDoc>(
                Builders<MenuCategoryDoc>.IndexKeys.Ascending(c => c.RestaurantId).Ascending(c => c.SortOrder),
                new CreateIndexOptions { Name = "by_store" }), cancellationToken: ct);

        await db.GetCollection<RestaurantPhotoDoc>(CollectionNames.RestaurantPhotos).Indexes.CreateOneAsync(
            new CreateIndexModel<RestaurantPhotoDoc>(
                Builders<RestaurantPhotoDoc>.IndexKeys.Ascending(p => p.RestaurantId).Descending(p => p.IsMain),
                new CreateIndexOptions { Name = "by_store" }), cancellationToken: ct);

        await db.GetCollection<MenuItemPhotoDoc>(CollectionNames.MenuItemPhotos).Indexes.CreateOneAsync(
            new CreateIndexModel<MenuItemPhotoDoc>(
                Builders<MenuItemPhotoDoc>.IndexKeys.Ascending(p => p.MenuItemId).Descending(p => p.IsMain),
                new CreateIndexOptions { Name = "by_item" }), cancellationToken: ct);

        await db.GetCollection<RestaurantHoursDoc>(CollectionNames.RestaurantHours).Indexes.CreateOneAsync(
            new CreateIndexModel<RestaurantHoursDoc>(
                Builders<RestaurantHoursDoc>.IndexKeys.Ascending(h => h.RestaurantId).Ascending(h => h.Day),
                new CreateIndexOptions { Name = "by_store_day", Unique = true }), cancellationToken: ct);

        // Page views. Every board over them is "a window of time, usually one app", so
        // the date leads each index; the visitor board then groups by address and the
        // team board by person.
        var visits = db.GetCollection<VisitDoc>(CollectionNames.Visits);
        var vk = Builders<VisitDoc>.IndexKeys;
        await visits.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<VisitDoc>(vk.Descending(v => v.At),
                new CreateIndexOptions { Name = "by_time" }),
            new CreateIndexModel<VisitDoc>(vk.Ascending(v => v.App).Descending(v => v.At),
                new CreateIndexOptions { Name = "by_app_time" }),
            new CreateIndexModel<VisitDoc>(vk.Ascending(v => v.Ip).Descending(v => v.At),
                new CreateIndexOptions { Name = "by_ip_time" }),
            new CreateIndexModel<VisitDoc>(vk.Ascending(v => v.UserId).Descending(v => v.At),
                new CreateIndexOptions { Name = "by_user_time" }),
        ], ct);
    }
}
