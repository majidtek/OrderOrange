using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>
/// Fills an empty Mongo catalog from whatever EF is pointed at.
///
/// Production is populated once by <see cref="CatalogMigrator"/> (ADO.NET, built for
/// five million rows) and this becomes a no-op. Tests boot against a throwaway Mongo
/// database and the EF InMemory demo world, which the ADO.NET path cannot read — this
/// is how their catalog gets there.
/// </summary>
public static class CatalogBootstrap
{
    public static async Task SyncIfEmptyAsync(AppDbContext db, IMongoDatabase mongo, ILogger logger)
    {
        // Each collection carries its own emptiness check. Gating everything on ONE
        // collection made a half-filled Mongo (cuisines present, restaurants absent)
        // retry the full sync every boot and die on the first duplicate key —
        // taking everything after it in the startup block down too.
        var restaurants = mongo.GetCollection<RestaurantDoc>(CollectionNames.Restaurants);
        var cuisineCollection = mongo.GetCollection<CuisineDoc>(CollectionNames.Cuisines);

        var cuisines = await db.Cuisines.AsNoTracking().ToListAsync();
        if (cuisines.Count > 0 && await cuisineCollection.EstimatedDocumentCountAsync() == 0)
            await cuisineCollection.InsertManyAsync(
                cuisines.Select(c => new CuisineDoc { Id = c.Id, Name = c.Name, Emoji = c.Emoji }));
        var cuisineById = cuisines.ToDictionary(c => c.Id);

        if (await restaurants.EstimatedDocumentCountAsync() > 0) return;

        var ratings = await db.Reviews.AsNoTracking()
            .GroupBy(r => r.RestaurantId)
            .Select(g => new { Id = g.Key, Avg = g.Average(x => (double)x.RestaurantRating), Count = g.Count() })
            .ToListAsync();
        var ratingById = ratings.ToDictionary(r => r.Id, r => (Math.Round(r.Avg, 2), r.Count));

        var stores = await db.Restaurants.AsNoTracking().ToListAsync();
        if (stores.Count > 0)
            await restaurants.InsertManyAsync(stores.Select(r =>
            {
                cuisineById.TryGetValue(r.CuisineId, out var cuisine);
                ratingById.TryGetValue(r.Id, out var rating);
                return new RestaurantDoc
                {
                    Id = r.Id,
                    OwnerUserId = r.OwnerUserId,
                    Name = r.Name,
                    Description = r.Description,
                    CuisineId = r.CuisineId,
                    CuisineName = cuisine?.Name ?? "",
                    CuisineEmoji = cuisine?.Emoji ?? "",
                    StoreType = r.StoreType,
                    LogoEmoji = r.LogoEmoji,
                    BannerColor = r.BannerColor,
                    Area = r.Area,
                    Street = r.Street,
                    Phone = r.Phone,
                    Lat = r.Lat,
                    Lng = r.Lng,
                    DeliveryFee = r.DeliveryFee,
                    MinOrder = r.MinOrder,
                    AvgPrepMinutes = r.AvgPrepMinutes,
                    IsOpen = r.IsOpen,
                    IsApproved = r.IsApproved,
                    CommissionPercent = r.CommissionPercent,
                    CreatedAt = r.CreatedAt,
                    Rating = rating.Item1,
                    RatingCount = rating.Item2,
                    Location = r.Lat is not null && r.Lng is not null ? [r.Lng.Value, r.Lat.Value] : null,
                };
            }));

        var categoryCollection = mongo.GetCollection<MenuCategoryDoc>(CollectionNames.MenuCategories);
        var categories = await db.MenuCategories.AsNoTracking().ToListAsync();
        if (categories.Count > 0 && await categoryCollection.EstimatedDocumentCountAsync() == 0)
            await categoryCollection.InsertManyAsync(
                categories.Select(c => new MenuCategoryDoc
                {
                    Id = c.Id, RestaurantId = c.RestaurantId, Name = c.Name, SortOrder = c.SortOrder,
                }));

        var itemCollection = mongo.GetCollection<MenuItemDoc>(CollectionNames.MenuItems);
        var items = await db.MenuItems.AsNoTracking().ToListAsync();
        if (items.Count > 0 && await itemCollection.EstimatedDocumentCountAsync() == 0)
            await itemCollection.InsertManyAsync(
                items.Select(i => new MenuItemDoc
                {
                    Id = i.Id,
                    RestaurantId = i.RestaurantId,
                    CategoryId = i.CategoryId,
                    Name = i.Name,
                    Description = i.Description,
                    Price = i.Price,
                    IsAvailable = i.IsAvailable,
                    ImageEmoji = i.ImageEmoji,
                    IsPopular = i.IsPopular,
                    PhotoData = i.PhotoData,
                    DiscountPercent = i.DiscountPercent,
                    SearchKeywords = i.SearchKeywords,
                    // Already live before the move, so already approved.
                    Status = Shared.ProductStatus.Approved,
                }));

        logger.LogInformation(
            "Seeded the Mongo catalog from EF: {Stores} stores, {Categories} categories, {Items} items.",
            stores.Count, categories.Count, items.Count);
    }
}
