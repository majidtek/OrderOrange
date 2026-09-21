using System.Net;
using System.Net.Http.Json;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

public class OrderLifecycleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public OrderLifecycleTests(ApiFactory factory) => _factory = factory;

    private async Task<OrderDto> PlaceOrderAsync(HttpClient client, string dishName = "Margherita", int quantity = 2)
    {
        await client.SignInAsync("customer@majidfood.com");

        var restaurants = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants");
        var bella = restaurants!.Single(r => r.Name == "Bella Napoli");
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{bella.Id}");
        var dish = detail!.Categories.SelectMany(c => c.Items).Single(i => i.Name == dishName);
        var addresses = await client.GetFromJsonAsync<List<AddressDto>>("api/addresses");

        var response = await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            bella.Id, addresses![0].Id, PaymentMethod.CashOnDelivery, null, "Test order",
            [new PlaceOrderItem(dish.Id, quantity, null)]));
        response.EnsureSuccessStatusCode();

        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(order);
        return order;
    }

    [Fact]
    public async Task PlaceOrder_ComputesTotalsServerSide()
    {
        var client = _factory.CreateClient();
        var order = await PlaceOrderAsync(client);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.StartsWith("MF-", order.Number);
        Assert.Equal(2 * 2.800m, order.Subtotal);              // 2 × Margherita
        Assert.Equal(0.500m, order.DeliveryFee);               // Bella Napoli's fee
        Assert.Equal(Pricing.ServiceFee, order.ServiceFee);    // platform flat fee, whatever it stands at
        Assert.Equal(0.280m, order.TaxAmount);                 // Oman VAT: 5.600 × 5%
        Assert.Equal(order.Subtotal + 0.500m + order.ServiceFee + 0.280m, order.Total);
    }

    [Fact]
    public async Task PlaceOrder_BelowMinimum_IsRejected()
    {
        var client = _factory.CreateClient();
        await client.SignInAsync("customer@majidfood.com");

        var restaurants = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants");
        var bella = restaurants!.Single(r => r.Name == "Bella Napoli");
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{bella.Id}");
        var lemonade = detail!.Categories.SelectMany(c => c.Items).Single(i => i.Name == "Italian Lemonade"); // 0.900
        var addresses = await client.GetFromJsonAsync<List<AddressDto>>("api/addresses");

        var response = await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            bella.Id, addresses![0].Id, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(lemonade.Id, 1, null)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CustomerRestaurantDriver_EndsDelivered()
    {
        var customer = _factory.CreateClient();
        var order = await PlaceOrderAsync(customer);

        // Restaurant: accept → preparing → ready.
        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");
        (await owner.PostAsync($"api/orders/{order.Id}/accept", null)).EnsureSuccessStatusCode();
        (await owner.PostAsync($"api/orders/{order.Id}/preparing", null)).EnsureSuccessStatusCode();
        (await owner.PostAsync($"api/orders/{order.Id}/ready", null)).EnsureSuccessStatusCode();

        // Driver: claim → pickup → on the way → delivered.
        var driver = _factory.CreateClient();
        await driver.SignInAsync("salim.driver@majidfood.com");
        (await driver.PostAsync($"api/orders/{order.Id}/claim", null)).EnsureSuccessStatusCode();
        (await driver.PostAsync($"api/orders/{order.Id}/pickup", null)).EnsureSuccessStatusCode();
        (await driver.PostAsync($"api/orders/{order.Id}/on-the-way", null)).EnsureSuccessStatusCode();
        (await driver.PostAsync($"api/orders/{order.Id}/delivered", null)).EnsureSuccessStatusCode();

        // Customer sees the finished order with a full timeline.
        var final = await customer.GetFromJsonAsync<OrderDto>($"api/orders/{order.Id}");
        Assert.NotNull(final);
        Assert.Equal(OrderStatus.Delivered, final.Status);
        Assert.Equal("Salim Al Amri", final.DriverName);
        Assert.Equal(7, final.History.Count); // Pending..Delivered, every step recorded

        // And can now review it.
        var review = await customer.PostAsJsonAsync("api/reviews",
            new CreateReviewRequest(order.Id, 5, 5, "Perfect test pizza"));
        review.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Pickup_BeforeKitchenIsReady_IsRejected()
    {
        var customer = _factory.CreateClient();
        var order = await PlaceOrderAsync(customer);

        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");
        (await owner.PostAsync($"api/orders/{order.Id}/accept", null)).EnsureSuccessStatusCode();

        // Ali (seeded offline, no active delivery) so this claim doesn't block
        // Salim's FullLifecycle test — the class shares one database.
        var driver = _factory.CreateClient();
        await driver.SignInAsync("ali.driver@majidfood.com");
        (await driver.PostAsync("api/drivers/toggle-online", null)).EnsureSuccessStatusCode();
        (await driver.PostAsync($"api/orders/{order.Id}/claim", null)).EnsureSuccessStatusCode();

        // Still only Accepted — picking up must fail.
        var pickup = await driver.PostAsync($"api/orders/{order.Id}/pickup", null);
        Assert.Equal(HttpStatusCode.BadRequest, pickup.StatusCode);
    }

    [Fact]
    public async Task Cancel_AfterRestaurantAccepted_IsRejected()
    {
        var customer = _factory.CreateClient();
        var order = await PlaceOrderAsync(customer);

        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");
        (await owner.PostAsync($"api/orders/{order.Id}/accept", null)).EnsureSuccessStatusCode();

        var cancel = await customer.PostAsJsonAsync($"api/orders/{order.Id}/cancel", new CancelOrderRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, cancel.StatusCode);
    }

    [Fact]
    public async Task ForeignOrder_IsHiddenFromOtherCustomers()
    {
        var customer = _factory.CreateClient();
        var order = await PlaceOrderAsync(customer);

        var other = _factory.CreateClient();
        await other.SignInAsync("ahmed@majidfood.com");
        var response = await other.GetAsync($"api/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
