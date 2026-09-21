using OrderOrange.ApiServer.Services;

namespace OrderOrange.Tests;

public class UnitTests
{
    [Fact]
    public void PasswordHasher_RoundTrips()
    {
        var hash = PasswordHasher.Hash("Pas_123");

        Assert.True(PasswordHasher.Verify("Pas_123", hash));
        Assert.False(PasswordHasher.Verify("pas_123", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void PasswordHasher_SaltsEveryHash()
    {
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    // The platform's cut is the percentage plus whatever the flat service fee is at
    // the time — read from Pricing itself, so changing the fee doesn't make this lie.
    [Theory]
    [InlineData(10.0, 15.0, 1.5)]   // 1.500 commission
    [InlineData(5.0, 12.0, 0.6)]    // 0.600
    [InlineData(0.0, 15.0, 0.0)]    // nothing sold, nothing owed but the fee
    public void Pricing_Commission_IsPercentPlusServiceFee(double subtotal, double percent, double onPercent)
    {
        Assert.Equal((decimal)onPercent + Pricing.ServiceFee,
            Pricing.Commission((decimal)subtotal, (decimal)percent));
    }

    [Fact]
    public void Pricing_Estimate_AddsDeliveryTime()
    {
        Assert.Equal(40, Pricing.EstimateMinutes(25));
    }
}
