using OrderOrange.Shared;
using MongoDB.Bson.Serialization.Attributes;

namespace OrderOrange.ApiServer.Data;

// The catalog as it lives in MongoDB. Ids stay the integers SQL assigned, so orders,
// favourites and every link the apps already hold keep resolving after the move.

public sealed class RestaurantDoc
{
    [BsonId] public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int CuisineId { get; set; }

    /// <summary>Denormalised so a store card never needs a second lookup.</summary>
    public string CuisineName { get; set; } = "";
    public string CuisineEmoji { get; set; } = "";

    public StoreType StoreType { get; set; }
    public string LogoEmoji { get; set; } = "🍽️";
    public string BannerColor { get; set; } = "#FFE8D9";
    public string Area { get; set; } = "";
    public string Street { get; set; } = "";
    public string Phone { get; set; } = "";
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal MinOrder { get; set; }
    public int AvgPrepMinutes { get; set; }
    public bool IsOpen { get; set; }
    public bool IsApproved { get; set; }
    public decimal CommissionPercent { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Rating rolled up from reviews — kept on the document so browse never joins.</summary>
    public double Rating { get; set; }
    public int RatingCount { get; set; }

    /// <summary>GeoJSON point for "near me"; null when the store has no coordinates.</summary>
    public double[]? Location { get; set; }
}

public sealed class CuisineDoc
{
    [BsonId] public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "";
}

public sealed class MenuCategoryDoc
{
    [BsonId] public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }

    /// <summary>The category's name in every tongue the partner filled; Name mirrors the canonical one.</summary>
    public Dictionary<string, string>? Names { get; set; }
}

public sealed class MenuItemDoc
{
    [BsonId] public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }

    /// <summary>
    /// Sold at the table only: the item stays on the POS, the till and the table-QR
    /// menu, but never appears on the public web menu, search, offers or the home strips
    /// (shisha, for one — a web listing breaks the WhatsApp commerce policy).
    /// </summary>
    public bool InStoreOnly { get; set; }
    public string ImageEmoji { get; set; } = "🍽️";
    public bool IsPopular { get; set; }
    public string? PhotoData { get; set; }
    public decimal DiscountPercent { get; set; }
    public string SearchKeywords { get; set; } = "";

    /// <summary>Where the product stands inside its group: lowest first, ties by name.</summary>
    public int SortOrder { get; set; }

    /// <summary>The dish's name in every tongue the partner filled; Name mirrors the canonical one.</summary>
    public Dictionary<string, string>? Names { get; set; }

    /// <summary>The dish's description in every tongue the partner filled; Description mirrors the canonical one.</summary>
    public Dictionary<string, string>? Descriptions { get; set; }

    /// <summary>What it is made of, "; "-joined — written freely by the partner.</summary>
    public string Ingredients { get; set; } = "";

    /// <summary>How it is sold: a unit key like "kg" or "piece"; empty = not shown.</summary>
    public string Unit { get; set; } = "";

    /// <summary>
    /// Moderation state. A partner's new product starts <see cref="ProductStatus.Pending"/>
    /// and no customer query returns it until an administrator approves it. Editing an
    /// approved product leaves the status alone — only creation is reviewed.
    /// </summary>
    public ProductStatus Status { get; set; } = ProductStatus.Approved;

    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewedByUserId { get; set; }
    public string? RejectionReason { get; set; }

    // When may this be ordered? Nulls/empty = always; To < From spans midnight;
    // Days is a CSV of DayOfWeek ints. LeadTimeDays > 0 = order now, made in N days.
    public int? AvailableFromMinutes { get; set; }
    public int? AvailableToMinutes { get; set; }
    public string AvailableDays { get; set; } = "";
    public int LeadTimeDays { get; set; }

    public MenuItemDto ToDto() => new(
        Id, CategoryId, Name, Description, Price, IsAvailable, ImageEmoji, IsPopular,
        PhotoData, DiscountPercent, Status, RejectionReason,
        AvailableFromMinutes, AvailableToMinutes, AvailableDays, LeadTimeDays,
        Ingredients, Unit, Names, Descriptions, SortOrder, InStoreOnly);
}

public sealed class RestaurantPhotoDoc
{
    [BsonId] public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string Data { get; set; } = "";
    public bool IsMain { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class MenuItemPhotoDoc
{
    [BsonId] public int Id { get; set; }
    public int MenuItemId { get; set; }
    public string Data { get; set; } = "";
    public bool IsMain { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class RestaurantHoursDoc
{
    [BsonId] public int Id { get; set; }
    public int RestaurantId { get; set; }
    /// <summary>0 = Sunday … 6 = Saturday (matches .NET DayOfWeek).</summary>
    public int Day { get; set; }
    public bool IsClosed { get; set; }
    /// <summary>Stored as ticks so the TimeSpan survives the round trip exactly.</summary>
    public long OpenTicks { get; set; }
    public long CloseTicks { get; set; }

    public TimeSpan Open => TimeSpan.FromTicks(OpenTicks);
    public TimeSpan Close => TimeSpan.FromTicks(CloseTicks);
}
