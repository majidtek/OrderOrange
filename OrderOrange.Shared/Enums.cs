namespace OrderOrange.Shared;

public enum UserRole
{
    Customer = 0,
    RestaurantOwner = 1,
    Driver = 2,
    Administrator = 3
}

/// <summary>
/// The order lifecycle. Restaurant moves Pending → Accepted → Preparing → Ready;
/// the driver moves it PickedUp → OnTheWay → Delivered. Rejected is the restaurant
/// saying no (with a reason); Cancelled is the customer backing out while still Pending.
/// </summary>
/// <summary>Driver document review lifecycle: submit → pending → approved / rejected.</summary>
public enum DriverVerificationStatus
{
    NotSubmitted = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public enum OrderStatus
{
    Pending = 0,
    Accepted = 1,
    Preparing = 2,
    Ready = 3,
    PickedUp = 4,
    OnTheWay = 5,
    Delivered = 6,
    Cancelled = 7,
    Rejected = 8
}

/// <summary>
/// How the order reaches the customer. Pickup skips the rider entirely: no delivery fee,
/// no address, and the order finishes at <see cref="OrderStatus.Ready"/> → collected,
/// rather than going out for delivery.
/// </summary>
public enum OrderType
{
    Delivery = 0,
    Pickup = 1
}

public enum PaymentMethod
{
    CashOnDelivery = 0,
    CardOnDelivery = 1,

    /// <summary>Paid in the app with a saved card — TEST mode, no real charge.</summary>
    CardOnline = 2
}

/// <summary>Talabat-style verticals — a store is a restaurant, grocery, pharmacy, florist or shop.</summary>
public enum StoreType
{
    Restaurant = 0,
    Grocery = 1,
    Pharmacy = 2,
    Flowers = 3,
    Shop = 4
}

public enum VehicleType
{
    Motorbike = 0,
    Car = 1,
    Bicycle = 2
}

/// <summary>
/// Moderation state of a partner-created product. New products start Pending and are
/// invisible to customers until an administrator approves them; editing an already
/// approved product does not send it back for review.
/// </summary>
public enum ProductStatus
{
    Pending,
    Approved,
    Rejected
}
