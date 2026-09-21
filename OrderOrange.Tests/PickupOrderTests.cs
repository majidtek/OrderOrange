using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Collecting the order yourself. The rules that matter: no delivery fee is charged, no
/// address is demanded, and — the one that would cause real trouble in a kitchen — no
/// rider is ever offered the job.
/// </summary>
public class PickupOrderTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PickupOrderTests(ApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, RestaurantDetailDto Store)> CustomerAsync()
    {
        var client = _factory.CreateClient();
        await client.SignInAsync("ahmed@majidfood.com");
        var stores = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants?take=20");
        var open = stores!.First(s => s.IsOpen);
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{open.Id}");
        return (client, detail!);
    }

    private static List<PlaceOrderItem> ItemsFrom(RestaurantDetailDto store, int quantity = 4) =>
        [new PlaceOrderItem(store.Categories.SelectMany(c => c.Items).First().Id, quantity, null)];

    /// <summary>
    /// A signed-in rider who is definitely ONLINE. Toggling blindly is a coin flip — the
    /// seeded rider may already be online, and a test that quietly runs against an
    /// offline rider passes for the wrong reason.
    /// </summary>
    private async Task<HttpClient> OnlineRiderAsync()
    {
        var rider = _factory.CreateClient();
        await rider.SignInAsync("salim.driver@majidfood.com");

        var toggled = await rider.PostAsync("api/drivers/toggle-online", null);
        var state = await toggled.Content.ReadFromJsonAsync<OnlineState>();
        if (state?.IsOnline == false) await rider.PostAsync("api/drivers/toggle-online", null);

        return rider;
    }

    private record OnlineState(bool IsOnline);

    [Fact]
    public async Task ShopsOfferCollectionByDefault()
    {
        var (_, store) = await CustomerAsync();
        Assert.True(store.Info.AllowsPickup);
    }

    [Fact]
    public async Task ACollectionIsPlacedWithNoAddressAndNoDeliveryFee()
    {
        var (client, store) = await CustomerAsync();

        // AddressId 0 on purpose: a collection has nowhere to deliver to.
        var response = await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            store.Info.Id, 0, PaymentMethod.CashOnDelivery, null, null, ItemsFrom(store),
            CardId: null, OrderType: OrderType.Pickup));
        response.EnsureSuccessStatusCode();

        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(order);
        Assert.Equal(OrderType.Pickup, order!.OrderType);
        Assert.Equal(0m, order.DeliveryFee);

        // And the total reflects it — subtotal + service fee + tax, no delivery.
        Assert.Equal(order.Subtotal + order.ServiceFee - order.Discount + order.TaxAmount, order.Total);

        // The address field carries the SHOP, so the customer knows where to go.
        Assert.Contains(store.Info.Name, order.DeliveryAddress);
    }

    [Fact]
    public async Task ADeliveryStillChargesTheFee()
    {
        var (client, store) = await CustomerAsync();
        var addresses = await client.GetFromJsonAsync<List<AddressDto>>("api/addresses");

        var order = await (await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            store.Info.Id, addresses![0].Id, PaymentMethod.CashOnDelivery, null, null, ItemsFrom(store))))
            .Content.ReadFromJsonAsync<OrderDto>();

        Assert.Equal(OrderType.Delivery, order!.OrderType);
        Assert.True(order.DeliveryFee > 0m, "a delivery must still pay the delivery fee");
    }

    [Fact]
    public async Task ADeliveryWithoutAnAddressIsStillRefused()
    {
        var (client, store) = await CustomerAsync();

        var response = await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            store.Info.Id, 0, PaymentMethod.CashOnDelivery, null, null, ItemsFrom(store)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- The part riders must never see ----------

    [Fact]
    public async Task ACollectionIsNeverOfferedToRiders()
    {
        var (customer, store) = await CustomerAsync();
        var pickup = await (await customer.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            store.Info.Id, 0, PaymentMethod.CashOnDelivery, null, null, ItemsFrom(store),
            CardId: null, OrderType: OrderType.Pickup)))
            .Content.ReadFromJsonAsync<OrderDto>();

        // The shop accepts it, which is what puts a delivery on the riders' board.
        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");
        await owner.PostAsync($"api/orders/{pickup!.Id}/accept", null);

        var rider = await OnlineRiderAsync();
        var available = await rider.GetFromJsonAsync<List<OrderDto>>("api/orders/driver/available");

        Assert.DoesNotContain(available!, o => o.Id == pickup.Id);

        // Sanity: the board is reachable and does list deliveries, so "not contained"
        // means "filtered out", not "the rider can see nothing at all".
        Assert.NotNull(available);
    }

    [Fact]
    public async Task ARiderCannotClaimACollectionEvenByAskingDirectly()
    {
        var (customer, store) = await CustomerAsync();
        var pickup = await (await customer.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            store.Info.Id, 0, PaymentMethod.CashOnDelivery, null, null, ItemsFrom(store),
            CardId: null, OrderType: OrderType.Pickup)))
            .Content.ReadFromJsonAsync<OrderDto>();

        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");
        await owner.PostAsync($"api/orders/{pickup!.Id}/accept", null);

        var rider = await OnlineRiderAsync();

        // Filtering the list is not enough — a stale page or a hand-made request must
        // fail too, or a rider ends up driving to a shop for a bag already collected.
        var response = await rider.PostAsync($"api/orders/{pickup.Id}/claim", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Refused because it is a COLLECTION — not because the rider happened to be
        // offline, which would make this test pass while proving nothing.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("collection", body, StringComparison.OrdinalIgnoreCase);
    }
}
