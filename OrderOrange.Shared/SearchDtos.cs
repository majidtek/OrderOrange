namespace OrderOrange.Shared;

/// <summary>A dish matched by smart search, with enough context to jump to its restaurant.</summary>
public record DishHitDto(
    int MenuItemId,
    string Name,
    string Description,
    string ImageEmoji,
    decimal Price,
    int RestaurantId,
    string RestaurantName,
    string RestaurantLogoEmoji,
    bool RestaurantIsOpen,
    // What KIND of shop this came from. The catalog spans restaurants, groceries,
    // pharmacies, florists and general shops, and a caller that cannot tell them apart
    // will happily offer a pizza and a flash drive in the same breath.
    StoreType StoreType = StoreType.Restaurant);

public record SearchResultsDto(List<RestaurantCardDto> Restaurants, List<DishHitDto> Dishes, string? DidYouMean = null);

/// <summary>
/// The shops the platform is putting forward right now, with a taste of each menu.
/// The dishes reuse <see cref="DealDto"/> so the home strip renders them with the same
/// card as an offer; a suggested dish simply carries a zero discount.
/// </summary>
public record SuggestionsDto(List<RestaurantCardDto> Stores, List<DealDto> Dishes);

/// <summary>One store the sitemap should list, with what its page title is built from.</summary>
public record SitemapStoreDto(int Id, string Name, string Cuisine, string Area, string Slug = "");

/// <summary>One dish currently on offer — powers the customer home "Offers" strip.</summary>
public record DealDto(
    int MenuItemId,
    string Name,
    string ImageEmoji,
    string? Photo,
    decimal Price,
    decimal DiscountPercent,
    int RestaurantId,
    string RestaurantName,
    string RestaurantLogoEmoji,
    string RestaurantCuisine,
    string RestaurantArea,
    bool RestaurantIsOpen)
{
    public decimal FinalPrice => Math.Round(Price * (1 - DiscountPercent / 100m), 3);
    public decimal Saving => Math.Round(Price - FinalPrice, 3);
}
