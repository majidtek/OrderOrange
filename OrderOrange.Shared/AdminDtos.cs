namespace OrderOrange.Shared;

public record StatusCountDto(OrderStatus Status, int Count);

/// <summary>One day of the dashboard trend chart.</summary>
public record DayStatDto(DateTime Date, int Orders, decimal Revenue);

public record ResetPasswordRequest(string NewPassword);

public record TopRestaurantDto(int Id, string Name, string LogoEmoji, int Orders, decimal Revenue, double Rating);

public record AdminDashboardDto(
    int TodayOrders,
    decimal TodayRevenue,
    decimal TodayCommission,
    int ActiveOrders,
    int TotalCustomers,
    int TotalRestaurants,
    int PendingRestaurantApprovals,
    int OnlineDrivers,
    int TotalDrivers,
    int TotalOrders,
    decimal TotalRevenue,
    decimal TotalCommission,
    List<StatusCountDto> OrdersByStatus,
    List<TopRestaurantDto> TopRestaurants,
    List<DayStatDto> Trend);

public record UserDto(
    int Id,
    string FullName,
    string Email,
    string Phone,
    UserRole Role,
    bool IsActive,
    DateTime CreatedAt,
    string? RestaurantName,
    bool? DriverOnline,
    DateTime? LastSeenAt);

/// <summary>
/// Admin creates staff accounts. For a RestaurantOwner, RestaurantName + CuisineId also
/// create the (unapproved) restaurant; for a Driver, VehicleType creates the driver profile.
/// </summary>
public record CreateUserRequest(
    string FullName,
    string Email,
    string Phone,
    string Password,
    UserRole Role,
    VehicleType? VehicleType,
    string? RestaurantName,
    int? CuisineId,
    // Which vertical the new partner's store belongs to (restaurant, grocery, pharmacy…).
    StoreType StoreType = StoreType.Restaurant);

public record AdminRestaurantDto(
    int Id,
    string Name,
    string LogoEmoji,
    string Cuisine,
    string Area,
    string OwnerName,
    string OwnerEmail,
    bool IsApproved,
    bool IsOpen,
    decimal CommissionPercent,
    double Rating,
    int RatingCount,
    int TotalOrders,
    decimal TotalRevenue,
    // Needed by "open as partner" — impersonation is minted against the USER, not the store.
    int OwnerUserId = 0,
    // Position in the customer home's "Suggested" strip; null = not featured.
    int? SuggestedOrder = null);

/// <summary>One row of the admin panel's "Suggested strip" panel, in running order.</summary>
public record AdminSuggestedDto(int Id, string Name, string LogoEmoji, bool IsApproved);

/// <summary>Everything a hard delete would erase — shown in full before the red button.</summary>
public record AdminStoreFootprintDto(
    int Orders,
    int Reviews,
    int Tables,
    int Salons,
    int OpenTabs,
    int Reservations,
    int Staff,
    int Bills,
    int MenuCategories,
    int MenuItems,
    int TableChats,
    int StoreCustomers);

public record SetCommissionRequest(decimal CommissionPercent);

/// <summary>An admin rewrites who a user IS — name, sign-in email, phone.</summary>
public record AdminUpdateUserRequest(string FullName, string Email, string Phone);
