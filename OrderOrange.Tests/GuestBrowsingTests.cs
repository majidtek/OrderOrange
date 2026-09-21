using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// A visitor can browse the whole shop and build a basket without an account; signing in
/// is only required at the moment the order is placed. These pin the API half of that:
/// everything a guest needs is anonymous, and everything personal still is not.
/// </summary>
public class GuestBrowsingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public GuestBrowsingTests(ApiFactory factory) => _factory = factory;

    /// <summary>A client with no token at all — exactly what a first-time visitor is.</summary>
    private HttpClient Guest() => _factory.CreateClient();

    [Fact]
    public async Task Guest_CanBrowseStores()
    {
        var stores = await Guest().GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants?take=10");
        Assert.NotNull(stores);
        Assert.NotEmpty(stores!);
    }

    [Fact]
    public async Task Guest_CanOpenAStoreAndSeeItsMenu()
    {
        var guest = Guest();
        var stores = await guest.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants?take=5");
        var detail = await guest.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{stores![0].Id}");

        Assert.NotNull(detail);
        Assert.NotEmpty(detail!.Categories);
        Assert.NotEmpty(detail.Categories.SelectMany(c => c.Items));
    }

    [Theory]
    [InlineData("api/lookups/cuisines")]
    [InlineData("api/restaurants/deals")]
    [InlineData("api/coupons/promos")]
    [InlineData("api/restaurants/search?query=pizza")]
    public async Task Guest_CanReachEveryPublicListing(string url)
    {
        var res = await Guest().GetAsync(url);
        Assert.True(res.IsSuccessStatusCode, $"{url} refused a guest with {res.StatusCode}");
    }

    [Theory]
    [InlineData("api/addresses")]
    [InlineData("api/orders/mine")]
    [InlineData("api/cards")]
    public async Task Guest_IsStillRefusedAnythingPersonal(string url)
    {
        var res = await Guest().GetAsync(url);
        Assert.True(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"{url} should need an account, got {res.StatusCode}");
    }

    [Fact]
    public async Task Guest_CannotPlaceAnOrder()
    {
        // The basket is client-side, so the gate that actually matters is this one.
        var res = await Guest().PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            1, 1, PaymentMethod.CashOnDelivery, null, null, [new PlaceOrderItem(1, 1, null)]));

        Assert.True(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"a guest must not be able to place an order, got {res.StatusCode}");
    }

    [Fact]
    public async Task SigningInAfterBrowsing_LetsTheSameVisitorOrder()
    {
        // The exact journey: browse anonymously, then sign in and place the order.
        var visitor = Guest();
        var stores = await visitor.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants?take=20");
        var bella = stores!.Single(s => s.Name == "Bella Napoli");
        var detail = await visitor.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{bella.Id}");
        var dish = detail!.Categories.SelectMany(c => c.Items).First();

        // …still a guest here, and the order is refused.
        var tooEarly = await visitor.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            bella.Id, 1, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(dish.Id, 1, null)]));
        Assert.True(tooEarly.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        // Sign in on the same client — the basket the UI holds is unaffected.
        await visitor.SignInAsync("customer@majidfood.com");
        var addresses = await visitor.GetFromJsonAsync<List<AddressDto>>("api/addresses");

        var placed = await visitor.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            bella.Id, addresses![0].Id, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(dish.Id, 1, null)]));
        placed.EnsureSuccessStatusCode();

        var order = await placed.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(order);
        Assert.Equal(bella.Id, order!.RestaurantId);
    }
}
