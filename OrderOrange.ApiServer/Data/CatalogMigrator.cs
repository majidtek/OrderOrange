using Microsoft.Data.SqlClient;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>
/// One-way copy of the catalog out of SQL Server and into MongoDB.
///
/// Deliberately ADO.NET rather than EF: five million restaurants would spend most of
/// their time in change tracking. Rows stream from a single reader straight into
/// batched bulk writes, so memory stays flat regardless of table size.
///
/// Safe to re-run — every write is an upsert keyed on the original integer id, and
/// the SQL tables are only ever read.
/// </summary>
public sealed class CatalogMigrator(string sqlConnectionString, IMongoDatabase mongo, ILogger logger)
{
    private const int BatchSize = 5_000;

    public async Task<Dictionary<string, long>> RunAsync(CancellationToken ct = default)
    {
        var report = new Dictionary<string, long>();

        report["cuisines"] = await CopyAsync<CuisineDoc>(
            "SELECT Id, Name, Emoji FROM Cuisines",
            r => new CuisineDoc
            {
                Id = r.GetInt32(0),
                Name = r.GetString(1),
                Emoji = r.GetString(2),
            }, ct);

        // Ratings live in Reviews; roll them up once here so a store card never joins.
        var ratings = await LoadRatingsAsync(ct);
        var cuisines = await mongo.GetCollection<CuisineDoc>("cuisines")
            .Find(FilterDefinition<CuisineDoc>.Empty).ToListAsync(ct);
        var cuisineById = cuisines.ToDictionary(c => c.Id);

        report["restaurants"] = await CopyAsync<RestaurantDoc>(
            @"SELECT Id, OwnerUserId, Name, Description, CuisineId, StoreType, LogoEmoji, BannerColor,
                     Area, Street, Phone, Lat, Lng, DeliveryFee, MinOrder, AvgPrepMinutes,
                     IsOpen, IsApproved, CommissionPercent, CreatedAt
              FROM Restaurants",
            r =>
            {
                var id = r.GetInt32(0);
                var cuisineId = r.GetInt32(4);
                cuisineById.TryGetValue(cuisineId, out var cuisine);
                var lat = r.IsDBNull(11) ? (double?)null : r.GetDouble(11);
                var lng = r.IsDBNull(12) ? (double?)null : r.GetDouble(12);
                ratings.TryGetValue(id, out var rating);
                return new RestaurantDoc
                {
                    Id = id,
                    OwnerUserId = r.GetInt32(1),
                    Name = r.GetString(2),
                    Description = r.GetString(3),
                    CuisineId = cuisineId,
                    CuisineName = cuisine?.Name ?? "",
                    CuisineEmoji = cuisine?.Emoji ?? "",
                    StoreType = (Shared.StoreType)r.GetInt32(5),
                    LogoEmoji = r.GetString(6),
                    BannerColor = r.GetString(7),
                    Area = r.GetString(8),
                    Street = r.GetString(9),
                    Phone = r.GetString(10),
                    Lat = lat,
                    Lng = lng,
                    DeliveryFee = r.GetDecimal(13),
                    MinOrder = r.GetDecimal(14),
                    AvgPrepMinutes = r.GetInt32(15),
                    IsOpen = r.GetBoolean(16),
                    IsApproved = r.GetBoolean(17),
                    CommissionPercent = r.GetDecimal(18),
                    CreatedAt = r.GetDateTime(19),
                    Rating = rating.Average,
                    RatingCount = rating.Count,
                    // GeoJSON is [longitude, latitude] — the opposite order to how we store them.
                    Location = lat is not null && lng is not null ? [lng.Value, lat.Value] : null,
                };
            }, ct);

        report["menuCategories"] = await CopyAsync<MenuCategoryDoc>(
            "SELECT Id, RestaurantId, Name, SortOrder FROM MenuCategories",
            r => new MenuCategoryDoc
            {
                Id = r.GetInt32(0),
                RestaurantId = r.GetInt32(1),
                Name = r.GetString(2),
                SortOrder = r.GetInt32(3),
            }, ct);

        report["menuItems"] = await CopyAsync<MenuItemDoc>(
            @"SELECT Id, RestaurantId, CategoryId, Name, Description, Price, IsAvailable,
                     ImageEmoji, IsPopular, PhotoData, DiscountPercent, SearchKeywords
              FROM MenuItems",
            r => new MenuItemDoc
            {
                Id = r.GetInt32(0),
                RestaurantId = r.GetInt32(1),
                CategoryId = r.GetInt32(2),
                Name = r.GetString(3),
                Description = r.GetString(4),
                Price = r.GetDecimal(5),
                IsAvailable = r.GetBoolean(6),
                ImageEmoji = r.GetString(7),
                IsPopular = r.GetBoolean(8),
                PhotoData = r.IsDBNull(9) ? null : r.GetString(9),
                DiscountPercent = r.GetDecimal(10),
                SearchKeywords = r.IsDBNull(11) ? "" : r.GetString(11),
            }, ct);

        report["restaurantPhotos"] = await CopyAsync<RestaurantPhotoDoc>(
            "SELECT Id, RestaurantId, Data, IsMain, CreatedAt FROM RestaurantPhotos",
            r => new RestaurantPhotoDoc
            {
                Id = r.GetInt32(0),
                RestaurantId = r.GetInt32(1),
                Data = r.GetString(2),
                IsMain = r.GetBoolean(3),
                CreatedAt = r.GetDateTime(4),
            }, ct);

        report["menuItemPhotos"] = await CopyAsync<MenuItemPhotoDoc>(
            "SELECT Id, MenuItemId, Data, IsMain, CreatedAt FROM MenuItemPhotos",
            r => new MenuItemPhotoDoc
            {
                Id = r.GetInt32(0),
                MenuItemId = r.GetInt32(1),
                Data = r.GetString(2),
                IsMain = r.GetBoolean(3),
                CreatedAt = r.GetDateTime(4),
            }, ct);

        report["restaurantHours"] = await CopyAsync<RestaurantHoursDoc>(
            "SELECT Id, RestaurantId, Day, IsClosed, [Open], [Close] FROM RestaurantHours",
            r => new RestaurantHoursDoc
            {
                Id = r.GetInt32(0),
                RestaurantId = r.GetInt32(1),
                Day = r.GetInt32(2),
                IsClosed = r.GetBoolean(3),
                OpenTicks = r.GetTimeSpan(4).Ticks,
                CloseTicks = r.GetTimeSpan(5).Ticks,
            }, ct);

        return report;
    }

    /// <summary>Average rating and review count per restaurant, computed in SQL.</summary>
    private async Task<Dictionary<int, (double Average, int Count)>> LoadRatingsAsync(CancellationToken ct)
    {
        var map = new Dictionary<int, (double, int)>();
        await using var connection = new SqlConnection(sqlConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT RestaurantId, AVG(CAST(RestaurantRating AS float)), COUNT(*) FROM Reviews GROUP BY RestaurantId",
            connection) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            map[reader.GetInt32(0)] = (Math.Round(reader.GetDouble(1), 2), reader.GetInt32(2));
        return map;
    }

    private async Task<long> CopyAsync<TDoc>(string sql, Func<SqlDataReader, TDoc> map, CancellationToken ct)
        where TDoc : class
    {
        var name = CollectionNames.For<TDoc>();
        var collection = mongo.GetCollection<TDoc>(name);

        await using var connection = new SqlConnection(sqlConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        await using var reader = await command.ExecuteReaderAsync(ct);

        var batch = new List<WriteModel<TDoc>>(BatchSize);
        long written = 0;
        var options = new BulkWriteOptions { IsOrdered = false };

        while (await reader.ReadAsync(ct))
        {
            var doc = map(reader);
            var id = IdOf(doc);
            batch.Add(new ReplaceOneModel<TDoc>(
                Builders<TDoc>.Filter.Eq("_id", id), doc) { IsUpsert = true });

            if (batch.Count < BatchSize) continue;
            await collection.BulkWriteAsync(batch, options, ct);
            written += batch.Count;
            batch.Clear();
            if (written % 250_000 == 0) logger.LogInformation("{Name}: {Written:N0} copied…", name, written);
        }

        if (batch.Count > 0)
        {
            await collection.BulkWriteAsync(batch, options, ct);
            written += batch.Count;
        }

        logger.LogInformation("{Name}: {Written:N0} documents in MongoDB.", name, written);
        return written;
    }

    private static int IdOf<TDoc>(TDoc doc) =>
        (int)typeof(TDoc).GetProperty("Id")!.GetValue(doc)!;
}

/// <summary>One place that decides which collection a document type lives in.</summary>
public static class CollectionNames
{
    public const string Restaurants = "restaurants";
    public const string Cuisines = "cuisines";
    public const string MenuCategories = "menuCategories";
    public const string MenuItems = "menuItems";
    public const string RestaurantPhotos = "restaurantPhotos";
    public const string MenuItemPhotos = "menuItemPhotos";
    public const string RestaurantHours = "restaurantHours";

    /// <summary>Page views. Append-only and unbounded — the reason they are not in SQL.</summary>
    public const string Visits = "visits";

    /// <summary>A store's questionnaires, and every guest's ticked answers to them.</summary>
    public const string Surveys = "surveys";
    public const string SurveyResponses = "surveyResponses";

    /// <summary>Partner support tickets (conversation) and the files attached to them.</summary>
    public const string SupportTickets = "supportTickets";
    public const string SupportFiles = "supportFiles";

    /// <summary>A store's own discount codes and their rules.</summary>
    public const string StorePromotions = "storePromotions";

    public static string For<TDoc>() => typeof(TDoc).Name switch
    {
        nameof(RestaurantDoc) => Restaurants,
        nameof(CuisineDoc) => Cuisines,
        nameof(MenuCategoryDoc) => MenuCategories,
        nameof(MenuItemDoc) => MenuItems,
        nameof(RestaurantPhotoDoc) => RestaurantPhotos,
        nameof(MenuItemPhotoDoc) => MenuItemPhotos,
        nameof(RestaurantHoursDoc) => RestaurantHours,
        var other => throw new InvalidOperationException($"No collection mapped for {other}."),
    };
}
