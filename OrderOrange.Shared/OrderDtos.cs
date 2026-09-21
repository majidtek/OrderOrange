namespace OrderOrange.Shared;

public record PlaceOrderItem(int MenuItemId, int Quantity, string? Notes);

public record PlaceOrderRequest(
    int RestaurantId,
    // Ignored for a Pickup order — there is nowhere to deliver to. Pass 0 there.
    int AddressId,
    PaymentMethod PaymentMethod,
    string? CouponCode,
    string? Notes,
    List<PlaceOrderItem> Items,
    int? CardId = null,
    OrderType OrderType = OrderType.Delivery);

public record OrderItemDto(int Id, string Name, decimal UnitPrice, int Quantity, string? Notes)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>One step of the order's status timeline — who moved it and when.</summary>
public record OrderEventDto(OrderStatus Status, DateTime At, string By);

/// <summary>One day's takings, for the dashboard's seven-day revenue trend.</summary>
public record DayRevenueDto(DateTime Day, decimal Total);

public record OrderDto(
    int Id,
    string Number,
    OrderStatus Status,
    DateTime PlacedAt,
    int CustomerId,
    string CustomerName,
    string CustomerPhone,
    int RestaurantId,
    string RestaurantName,
    string RestaurantLogoEmoji,
    string RestaurantArea,
    string RestaurantPhone,
    int? DriverId,
    string? DriverName,
    string? DriverPhone,
    string DeliveryAddress,
    double? DeliveryLat,
    double? DeliveryLng,
    PaymentMethod PaymentMethod,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal ServiceFee,
    decimal Discount,
    decimal Total,
    string? Notes,
    string? CancelReason,
    int EstimatedMinutes,
    bool IsPaid,
    string? PaymentRef,
    bool HasReview,
    List<OrderItemDto> Items,
    List<OrderEventDto> History,
    double? PickupKm = null,
    OrderType OrderType = OrderType.Delivery,
    // The promised day for advance-notice items; null = the normal today flow.
    DateTime? ScheduledFor = null,
    // Dine-in: which table the food goes to. Null for delivery and collection.
    string? TableName = null,
    // VAT as charged when the order was placed.
    decimal TaxPercent = 0m,
    decimal TaxAmount = 0m,
    string? DriverAvatar = null,
    string? DriverPlate = null,
    VehicleType? DriverVehicle = null,
    // ---- The shop's own courier, when the store delivers for itself ----
    bool SelfDelivery = false,
    string? CourierName = null,
    string? CourierPhone = null,
    double? CourierLat = null,
    double? CourierLng = null,
    DateTime? CourierAt = null);

public record RejectOrderRequest(string Reason);
public record CancelOrderRequest(string? Reason);

/// <summary>Who is taking this order out — the shop's own person, not a platform rider.</summary>
public record AssignCourierRequest(string Name, string? Phone = null);

/// <summary>A live position pushed from the courier's phone while they drive.</summary>
public record CourierLocationRequest(double Lat, double Lng);

public record PublicBillItemDto(string Name, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// What a scanned receipt shows anyone holding the paper. Deliberately narrower than
/// <see cref="OrderDto"/>: no phone, no delivery address, no customer or courier identity —
/// just enough to prove the bill is real and the amounts match.
/// </summary>
public record PublicBillDto(
    string Code,
    string Number,
    DateTime PlacedAt,
    OrderStatus Status,
    string RestaurantName,
    string RestaurantLogoEmoji,
    string RestaurantArea,
    string? RestaurantLogo,
    string CustomerInitials,
    List<PublicBillItemDto> Items,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal ServiceFee,
    decimal Discount,
    decimal Total,
    PaymentMethod PaymentMethod,
    bool IsPaid,
    string CrNumber,
    string VatNumber,
    // Everything the scanned page needs to draw the REAL slip, not a summary card.
    decimal TaxPercent = 0m,
    decimal TaxAmount = 0m,
    OrderType OrderType = OrderType.Delivery,
    string? TableName = null,
    string? DeliveryAddress = null,
    string StorePhone = "",
    string StoreStreet = "",
    // The door back in: "order again" on the scanned bill leads straight to the store.
    int RestaurantId = 0);

/// <summary>A dine-in order from a scanned table QR — placed without an account.</summary>
public record PlaceTableOrderRequest(int RestaurantId, int Table, List<PlaceOrderItem> Items, string? GuestName = null);

/// <summary>What the table-order endpoint hands back: the number the guest quotes.</summary>
public record TableOrderResultDto(int Id, string Number, decimal Total);
