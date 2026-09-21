namespace OrderOrange.Shared;

/// <summary>Everything the driver home screen needs in one call.</summary>
public record DriverStateDto(bool IsOnline, VehicleType VehicleType, OrderDto? ActiveOrder,
    DriverVerificationStatus Verification = DriverVerificationStatus.Approved, string? VerificationReason = null);

/// <summary>What the driver sees about their own document review.</summary>
public record DriverVerificationDto(
    DriverVerificationStatus Status,
    string? Reason,
    DateTime? SubmittedAt,
    bool HasIdFront,
    bool HasIdBack,
    bool HasLicenseFront,
    bool HasLicenseBack);

/// <summary>All four documents as data-URLs — ID card and driving license, both sides.</summary>
public record SubmitDriverDocsRequest(string IdCardFront, string IdCardBack, string LicenseFront, string LicenseBack);

public record RejectDriverDocsRequest(string Reason);


/// <summary>One driver's submission as the admin reviewer sees it.</summary>
public record AdminDriverDocsDto(
    int UserId,
    string FullName,
    string Email,
    string Phone,
    VehicleType Vehicle,
    DriverVerificationStatus Status,
    string? Reason,
    DateTime? SubmittedAt,
    string? IdCardFront,
    string? IdCardBack,
    string? LicenseFront,
    string? LicenseBack);

public record DayEarningsDto(DateTime Date, int Deliveries, decimal Earnings);

public record UpdateLocationRequest(double Lat, double Lng);
public record DriverLocationDto(double Lat, double Lng, DateTime At);

public record EarningsDto(
    int TotalDeliveries,
    decimal TotalEarnings,
    int TodayDeliveries,
    decimal TodayEarnings,
    decimal WeekEarnings,
    List<DayEarningsDto> PerDay);
