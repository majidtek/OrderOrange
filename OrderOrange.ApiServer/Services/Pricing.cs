namespace OrderOrange.ApiServer.Services;

/// <summary>Platform-wide commercial rules, in one place.</summary>
public static class Pricing
{
    /// <summary>Flat platform service fee added to every order (OMR).</summary>
    // Zero by the owner's decision (2026-08-28): the platform charges no service fee.
    // The constant stays so a future fee is one number away; every screen hides a 0.
    public const decimal ServiceFee = 0m;

    /// <summary>Minutes a delivery adds on top of the kitchen's prep time.</summary>
    public const int DeliveryMinutes = 15;

    public static int EstimateMinutes(int avgPrepMinutes) => avgPrepMinutes + DeliveryMinutes;

    /// <summary>The platform's cut of one order: commission on the subtotal plus the service fee.</summary>
    public static decimal Commission(decimal subtotal, decimal commissionPercent) =>
        Math.Round(subtotal * commissionPercent / 100m, 3) + ServiceFee;
}
