namespace OrderOrange.Shared;

public record CouponDto(
    int Id,
    string Code,
    decimal Percent,
    decimal MinOrder,
    DateTime? ExpiresAt,
    bool IsActive,
    int MaxUses,
    int Uses,
    decimal MaxDiscount = 0,
    decimal Amount = 0,
    bool FirstOrderOnly = false);

public record SaveCouponRequest(
    string Code,
    decimal Percent,
    decimal MinOrder,
    DateTime? ExpiresAt,
    bool IsActive,
    int MaxUses,
    decimal MaxDiscount = 0,
    decimal Amount = 0,
    bool FirstOrderOnly = false);

public record ValidateCouponRequest(string Code, decimal Subtotal);
public record ValidateCouponResponse(bool Valid, string Message, decimal Discount);

/// <summary>A live offer shown on the customer home carousel.</summary>
public record PromoDto(string Code, decimal Percent, decimal MinOrder, DateTime? ExpiresAt, decimal MaxDiscount = 0,
    decimal Amount = 0, bool FirstOrderOnly = false);
