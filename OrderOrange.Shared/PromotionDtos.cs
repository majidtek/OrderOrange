namespace OrderOrange.Shared;

/// <summary>
/// Store discount codes: a partner's own promotions, valid only at that store. Each one
/// is a code, a discount and a RULE about who may use it — the first order, the loyal
/// regular, the happy-hour crowd — plus the usual limits.
/// </summary>
public static class PromoCatalog
{
    /// <summary>percent = x% off the goods (optionally capped) · amount = a fixed sum off.</summary>
    public static readonly string[] Types = ["percent", "amount"];

    /// <summary>
    /// any · first_order (nothing ordered here before) · orders_month (≥ N orders here this
    /// calendar month) · orders_total (≥ N orders here ever) · spent_total (≥ X OMR here ever)
    /// · min_items (≥ N items in this order).
    /// </summary>
    public static readonly string[] Rules = ["any", "first_order", "orders_month", "orders_total", "spent_total", "min_items"];

    public static readonly string[] OrderTypes = ["all", "delivery", "pickup"];
}

public record PromotionDto(
    string Id, int RestaurantId, string Code, string Title, string? Description,
    string Type, decimal Value, decimal MaxDiscount, decimal MinOrder,
    string Rule, int RuleValue, decimal RuleAmount,
    string OrderTypes, List<int> Days, int? StartHour, int? EndHour,
    DateTime? StartsAt, DateTime? EndsAt, int MaxUses, int MaxUsesPerCustomer,
    bool IsActive, bool IsPublic, DateTime CreatedAt,
    // Live figures from the order book: how often it was used and what it gave away.
    int Uses, decimal Saved, int UsesThisMonth);

public record SavePromotionRequest(
    string Code, string Title, string? Description,
    string Type, decimal Value, decimal MaxDiscount, decimal MinOrder,
    string Rule, int RuleValue, decimal RuleAmount,
    string OrderTypes, List<int>? Days, int? StartHour, int? EndHour,
    DateTime? StartsAt, DateTime? EndsAt, int MaxUses, int MaxUsesPerCustomer,
    bool IsActive, bool IsPublic);

/// <summary>What a guest sees on the store page: the offer, never the limits.</summary>
public record PublicPromotionDto(
    string Code, string Title, string? Description, string Type, decimal Value, decimal MaxDiscount,
    decimal MinOrder, string Rule, int RuleValue, decimal RuleAmount, DateTime? EndsAt);

public record CheckPromotionRequest(int RestaurantId, string Code, decimal Subtotal, int ItemCount, OrderType OrderType);

/// <summary>
/// Known = the code belongs to this store (else the caller may try the platform coupons).
/// ErrorCode is a localization key suffix (dc.e.*), Args its placeholders.
/// </summary>
public record PromotionCheckDto(bool Valid, bool Known, string? ErrorCode, string[] Args, decimal Discount, string? Title, string Message);

public record PromotionSummaryDto(int Active, int UsesThisMonth, decimal SavedThisMonth);

public record PromotionPageDto(List<PromotionDto> Items, PromotionSummaryDto Summary);
