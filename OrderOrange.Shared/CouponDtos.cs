namespace OrderOrange.Shared;

public record CouponDto(
    int Id,
    string Code,
    decimal Percent,
    decimal MinOrder,
    DateTime? ExpiresAt,
    bool IsActive,
    int MaxUses,
    int Uses);

public record SaveCouponRequest(
    string Code,
    decimal Percent,
    decimal MinOrder,
    DateTime? ExpiresAt,
    bool IsActive,
    int MaxUses);

public record ValidateCouponRequest(string Code, decimal Subtotal);
public record ValidateCouponResponse(bool Valid, string Message, decimal Discount);

/// <summary>A live offer shown on the customer home carousel.</summary>
public record PromoDto(string Code, decimal Percent, decimal MinOrder, DateTime? ExpiresAt);
