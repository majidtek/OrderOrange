using System.Net.Http.Json;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

public class CouponTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CouponTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("WELCOME10", 5.0, true, 0.5)]    // 10% of 5.000
    [InlineData("welcome10", 5.0, true, 0.5)]    // case-insensitive
    [InlineData("WELCOME10", 1.0, false, 0)]     // below the coupon's min order
    [InlineData("EID15", 10.0, false, 0)]        // seeded inactive/expired
    [InlineData("NOPE", 10.0, false, 0)]         // unknown code
    public async Task Validate_AppliesTheRules(string code, double subtotal, bool valid, double discount)
    {
        var client = _factory.CreateClient();
        await client.SignInAsync("majed.maniat.p2@gmail.com");

        var response = await client.PostAsJsonAsync("api/coupons/validate",
            new ValidateCouponRequest(code, (decimal)subtotal));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ValidateCouponResponse>();

        Assert.NotNull(result);
        Assert.Equal(valid, result.Valid);
        Assert.Equal((decimal)discount, result.Discount);
    }

    [Fact]
    public async Task PlaceOrder_WithCoupon_DiscountsTheTotal()
    {
        var client = _factory.CreateClient();
        await client.SignInAsync("majed.maniat.p2@gmail.com");

        var restaurants = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants");
        var bella = restaurants!.Single(r => r.Name == "Bella Napoli");
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{bella.Id}");
        var margherita = detail!.Categories.SelectMany(c => c.Items).Single(i => i.Name == "Margherita");
        var addresses = await client.GetFromJsonAsync<List<AddressDto>>("api/addresses");

        var response = await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            bella.Id, addresses![0].Id, PaymentMethod.CardOnDelivery, "WELCOME10", null,
            [new PlaceOrderItem(margherita.Id, 2, null)]));
        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(order);
        Assert.Equal(5.600m, order.Subtotal);
        Assert.Equal(0.560m, order.Discount); // 10%
        // VAT lands on the goods AFTER the coupon: (5.600 − 0.560) × 5% = 0.252.
        Assert.Equal(0.252m, order.TaxAmount);
        Assert.Equal(5.600m + 0.500m + Pricing.ServiceFee - 0.560m + 0.252m, order.Total);
    }
}
