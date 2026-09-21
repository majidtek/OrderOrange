using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>Entity → DTO mapping. Callers are responsible for loading the navigations used here.</summary>
public static class Mapping
{
    public static OrderDto ToDto(this Order o) => new(
        o.Id,
        o.Number,
        o.Status,
        o.PlacedAt,
        o.CustomerId,
        o.Customer.FullName,
        o.Customer.Phone,
        o.RestaurantId,
        o.Restaurant.Name,
        o.Restaurant.LogoEmoji,
        o.Restaurant.Area,
        o.Restaurant.Phone,
        o.DriverUserId,
        o.Driver?.FullName,
        o.Driver?.Phone,
        o.DeliveryAddress,
        o.DeliveryLat,
        o.DeliveryLng,
        o.PaymentMethod,
        o.Subtotal,
        o.DeliveryFee,
        o.ServiceFee,
        o.Discount,
        o.Total,
        o.Notes,
        o.CancelReason,
        o.EstimatedMinutes,
        o.IsPaid,
        o.PaymentRef,
        o.Review is not null,
        o.Items.Select(i => new OrderItemDto(i.Id, i.Name, i.UnitPrice, i.Quantity, i.Notes)).ToList(),
        o.Events.OrderBy(e => e.At).Select(e => new OrderEventDto(e.Status, e.At, e.By)).ToList(),
        OrderType: o.OrderType,
        ScheduledFor: o.ScheduledFor,
        TableName: o.TableName,
        TaxPercent: o.TaxPercent,
        TaxAmount: o.TaxAmount,
        DriverAvatar: o.Driver?.AvatarIcon,
        DriverPlate: o.Driver?.DriverProfile?.PlateNumber,
        DriverVehicle: o.Driver?.DriverProfile?.VehicleType,
        SelfDelivery: o.Restaurant?.SelfDelivery ?? false,
        CourierName: o.CourierName,
        CourierPhone: o.CourierPhone,
        CourierLat: o.CourierLat,
        CourierLng: o.CourierLng,
        CourierAt: o.CourierAt);

    public static MenuItemDto ToDto(this MenuItem i) =>
        new(i.Id, i.CategoryId, i.Name, i.Description, i.Price, i.IsAvailable, i.ImageEmoji, i.IsPopular, i.PhotoData, i.DiscountPercent,
            AvailableFromMinutes: i.AvailableFromMinutes, AvailableToMinutes: i.AvailableToMinutes,
            AvailableDays: i.AvailableDays, LeadTimeDays: i.LeadTimeDays,
            Ingredients: i.Ingredients, Unit: i.Unit);

    public static AddressDto ToDto(this Address a) =>
        new(a.Id, a.Label, a.Area, a.Street, a.Building, a.Notes, a.Lat, a.Lng);

    public static string ToOneLine(this Address a)
    {
        var line = $"{a.Label} — {a.Building}, {a.Street}, {a.Area}";
        return string.IsNullOrWhiteSpace(a.Notes) ? line : $"{line} ({a.Notes})";
    }

    public static CouponDto ToDto(this Coupon c) =>
        new(c.Id, c.Code, c.Percent, c.MinOrder, c.ExpiresAt, c.IsActive, c.MaxUses, c.Uses);

    public static CuisineDto ToDto(this Cuisine c) => new(c.Id, c.Name, c.Emoji);

    public static CardDto ToDto(this SavedCard c) =>
        new(c.Id, c.Brand, c.HolderName, c.Last4, c.ExpMonth, c.ExpYear);
}
