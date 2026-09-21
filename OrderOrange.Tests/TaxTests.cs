using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// VAT on orders. What matters: Oman's 5% is the default without anyone configuring
/// anything, a store that sets 0% really charges none, the counter till is taxed the
/// same as the app, and the order snapshots the rate so old receipts survive a change.
/// </summary>
public class TaxTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TaxTests(ApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Client, RestaurantDetailDto Store, int AddressId)> CustomerAsync(string storeName)
    {
        var client = _factory.CreateClient();
        await client.SignInAsync("ahmed@majidfood.com");
        var stores = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants?take=30");
        var card = stores!.First(s => s.Name == storeName);
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{card.Id}");
        var addresses = await client.GetFromJsonAsync<List<AddressDto>>("api/addresses");
        return (client, detail!, addresses![0].Id);
    }

    private static PlaceOrderRequest OrderOf(RestaurantDetailDto store, int addressId, int quantity = 2) => new(
        store.Info.Id, addressId, PaymentMethod.CashOnDelivery, null, null,
        [new PlaceOrderItem(store.Categories.SelectMany(c => c.Items).First().Id, quantity, null)]);

    [Fact]
    public async Task OmansFivePercentIsChargedWithoutAnySetup()
    {
        var (client, store, addressId) = await CustomerAsync("Bella Napoli");

        var order = await (await client.PostAsJsonAsync("api/orders", OrderOf(store, addressId)))
            .Content.ReadFromJsonAsync<OrderDto>();

        Assert.Equal(5m, order!.TaxPercent);
        Assert.Equal(Math.Round(order.Subtotal * 0.05m, 3), order.TaxAmount);
        Assert.Equal(order.Subtotal + order.DeliveryFee + order.ServiceFee + order.TaxAmount, order.Total);
    }

    [Fact]
    public async Task AStoreThatSetsZeroChargesNoTax()
    {
        // Sara switches her own shop to 0% in Settings — HER shop, not Bella Napoli,
        // because other pricing tests run in parallel against Marco's store.
        var owner = _factory.CreateClient();
        await owner.SignInAsync("sara@majidfood.com");
        var mine = await owner.GetFromJsonAsync<MyRestaurantDto>("api/restaurants/mine");
        var update = new UpdateRestaurantRequest(
            mine!.Name, mine.Description, mine.CuisineId, mine.LogoEmoji, mine.BannerColor,
            mine.Area, mine.Street, mine.Phone, mine.DeliveryFee, mine.MinOrder, mine.AvgPrepMinutes,
            mine.StoreType, mine.AllowsPickup, TaxPercent: 0m);
        (await owner.PutAsJsonAsync("api/restaurants/mine", update)).EnsureSuccessStatusCode();

        try
        {
            // …and a customer's next order carries no tax at all.
            var (client, store, addressId) = await CustomerAsync(mine.Name);
            var order = await (await client.PostAsJsonAsync("api/orders", OrderOf(store, addressId)))
                .Content.ReadFromJsonAsync<OrderDto>();

            Assert.Equal(0m, order!.TaxPercent);
            Assert.Equal(0m, order.TaxAmount);
            Assert.Equal(order.Subtotal + order.DeliveryFee + order.ServiceFee, order.Total);
        }
        finally
        {
            // Put the shop back on 5% — other tests price against the default.
            await owner.PutAsJsonAsync("api/restaurants/mine", update with { TaxPercent = 5m });
        }
    }

    [Fact]
    public async Task TheCounterTillIsTaxedLikeTheApp()
    {
        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");
        var customer = await (await owner.PostAsJsonAsync("api/storecustomers",
            new SaveStoreCustomerRequest("Tax Walk-in", "+968 9333 0001", "—")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();
        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();

        var order = await (await owner.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            customer!.Id, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(dish.Id, 2, null)])))
            .Content.ReadFromJsonAsync<OrderDto>();

        Assert.Equal(5m, order!.TaxPercent);
        Assert.Equal(Math.Round(order.Subtotal * 0.05m, 3), order.TaxAmount);
        Assert.True(order.Total > order.Subtotal, "the till total must include the tax");
    }
}
