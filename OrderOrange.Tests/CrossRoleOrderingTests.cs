using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// One email, one person, two hats: the account that runs a store can also order dinner.
/// The customer-side endpoints admit partners and riders; the staff sides stay locked to
/// their own roles.
/// </summary>
public class CrossRoleOrderingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CrossRoleOrderingTests(ApiFactory factory) => _factory = factory;

    private async Task<OrderDto> PlacePickupAsAsync(string email)
    {
        var client = _factory.CreateClient();
        await client.SignInAsync(email);

        var stores = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants?take=20");
        var open = stores!.First(s => s.IsOpen);
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{open.Id}");
        var dish = detail!.Categories.SelectMany(c => c.Items).First();

        // Pickup: no address needed, which keeps this test to the point being made.
        var response = await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            open.Id, 0, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(dish.Id, 4, null)], CardId: null, OrderType: OrderType.Pickup));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    [Fact]
    public async Task APartnerCanOrderFoodLikeAnyCustomer()
    {
        var order = await PlacePickupAsAsync("marco@majidfood.com");   // owns Bella Napoli
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task ARiderCanOrderFoodLikeAnyCustomer()
    {
        var order = await PlacePickupAsAsync("salim.driver@majidfood.com");
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task TheirPersonalOrdersShowUnderMine()
    {
        var partner = _factory.CreateClient();
        await partner.SignInAsync("marco@majidfood.com");

        var response = await partner.GetAsync("api/orders/mine");

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// A collection never gets a rider, so the store itself closes it once the customer has
    /// the bag, even when the store does not deliver with its own people (ticket T-00002).
    /// </summary>
    [Fact]
    public async Task TheStoreClosesACollectionWhenTheCustomerTakesIt()
    {
        var customer = _factory.CreateClient();
        await customer.SignInAsync("ahmed@majidfood.com");
        var stores = await customer.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants?take=50");
        var burgers = stores!.First(s => s.Name == "Burger Bros");
        var detail = await customer.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{burgers.Id}");
        var placed = await customer.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            burgers.Id, 0, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(detail!.Categories.SelectMany(c => c.Items).First().Id, 1, null)],
            CardId: null, OrderType: OrderType.Pickup));
        var order = (await placed.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<OrderDto>())!;

        var store = _factory.CreateClient();
        await store.SignInAsync("sara@majidfood.com"); // Burger Bros

        foreach (var step in new[] { "accept", "preparing", "ready" })
            (await store.PostAsync($"api/orders/{order.Id}/{step}", null)).EnsureSuccessStatusCode();
        (await store.PostAsync($"api/orders/{order.Id}/self-delivered", null)).EnsureSuccessStatusCode();

        var closed = await store.GetFromJsonAsync<OrderDto>($"api/orders/{order.Id}");
        Assert.Equal(OrderStatus.Delivered, closed!.Status);
    }

    /// <summary>The door swings one way: eating is open, running a store is not.</summary>
    [Fact]
    public async Task ACustomerStillCannotTouchPartnerEndpoints()
    {
        var customer = _factory.CreateClient();
        await customer.SignInAsync("ahmed@majidfood.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("api/menu")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("api/restaurants/my-stores")).StatusCode);
    }
}
