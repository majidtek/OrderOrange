using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Open invoices (tabs). What matters: seating opens the invoice, rounds accumulate at
/// menu prices, the settled bill is a REAL order with VAT and a freed table behind it,
/// an empty tab closes into nothing, and no store can touch another's tabs.
/// </summary>
public class StoreTabTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StoreTabTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> OwnerAsync(string email = "marco@majidfood.com")
    {
        var client = _factory.CreateClient();
        await client.SignInAsync(email);
        return client;
    }

    private static async Task<StoreTableDto> CreateTableAsync(HttpClient owner, string name)
    {
        var response = await owner.PostAsJsonAsync("api/storetables", new SaveStoreTableRequest(name, "indoor", 4));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoreTableDto>())!;
    }

    [Fact]
    public async Task SeatingOpensTheInvoiceAndSettlingItBillsTheMeal()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Tab T1");

        // Seating a walk-in opens their tab…
        var tab = await (await owner.PostAsJsonAsync("api/storetabs/open",
            new OpenTabRequest(table.Id, GuestName: "Abu Faisal")))
            .Content.ReadFromJsonAsync<StoreTabDto>();
        Assert.Equal(0, tab!.Lines.Count);
        Assert.Equal("Abu Faisal", tab.GuestName);

        // …and the floor plan already shows the table busy.
        var floor = await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables");
        Assert.True(floor!.First(t => t.Id == table.Id).IsOccupied);

        // First round, priced by the menu.
        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();
        tab = await (await owner.PostAsJsonAsync($"api/storetabs/{tab.Id}/lines",
            new AddTabLinesRequest([new PlaceOrderItem(dish.Id, 2, null)])))
            .Content.ReadFromJsonAsync<StoreTabDto>();
        Assert.Single(tab!.Lines);
        Assert.Equal(tab.Subtotal + tab.ServiceFee + tab.TaxAmount, tab.Total);

        // The bill is called: a real order appears, taxed, marked dine-in…
        var order = await (await owner.PostAsJsonAsync($"api/storetabs/{tab.Id}/close",
            new CloseTabRequest(PaymentMethod.CashOnDelivery, MarkPaid: true)))
            .Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal("Tab T1", order!.TableName);
        Assert.Equal(0m, order.DeliveryFee);
        Assert.True(order.IsPaid);
        Assert.Equal(Math.Round(order.Subtotal * order.TaxPercent / 100m, 3), order.TaxAmount);

        // …the table is free again and the tab is gone.
        floor = await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables");
        Assert.False(floor!.First(t => t.Id == table.Id).IsOccupied);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"api/storetabs/table/{table.Id}")).StatusCode);
    }

    [Fact]
    public async Task AnEmptyTabClosesIntoNothingAndFreesTheTable()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Tab Empty");
        var tab = await (await owner.PostAsJsonAsync("api/storetabs/open",
            new OpenTabRequest(table.Id, GuestName: "Left Early")))
            .Content.ReadFromJsonAsync<StoreTabDto>();

        var response = await owner.PostAsJsonAsync($"api/storetabs/{tab!.Id}/close",
            new CloseTabRequest(PaymentMethod.CashOnDelivery));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var floor = await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables");
        Assert.False(floor!.First(t => t.Id == table.Id).IsOccupied);
    }

    [Fact]
    public async Task TheTicketSteppersChangeQuantityAndZeroStrikesTheLine()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Tab Step");
        var tab = await (await owner.PostAsJsonAsync("api/storetabs/open",
            new OpenTabRequest(table.Id, GuestName: "Hungry")))
            .Content.ReadFromJsonAsync<StoreTabDto>();

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();
        tab = await (await owner.PostAsJsonAsync($"api/storetabs/{tab!.Id}/lines",
            new AddTabLinesRequest([new PlaceOrderItem(dish.Id, 1, null)])))
            .Content.ReadFromJsonAsync<StoreTabDto>();

        // + up to three…
        tab = await (await owner.PutAsJsonAsync($"api/storetabs/{tab!.Id}/lines/{tab.Lines[0].Id}",
            new SetTabLineQtyRequest(3))).Content.ReadFromJsonAsync<StoreTabDto>();
        Assert.Equal(3, tab!.Lines[0].Quantity);
        Assert.Equal(3 * dish.FinalPrice, tab.Subtotal);

        // …and − down to zero strikes the line off the bill.
        tab = await (await owner.PutAsJsonAsync($"api/storetabs/{tab.Id}/lines/{tab.Lines[0].Id}",
            new SetTabLineQtyRequest(0))).Content.ReadFromJsonAsync<StoreTabDto>();
        Assert.Empty(tab!.Lines);
    }

    [Fact]
    public async Task ALineCanBeStruckFromTheBill()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Tab Strike");
        var tab = await (await owner.PostAsJsonAsync("api/storetabs/open",
            new OpenTabRequest(table.Id, GuestName: "Changed Mind")))
            .Content.ReadFromJsonAsync<StoreTabDto>();

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();
        tab = await (await owner.PostAsJsonAsync($"api/storetabs/{tab!.Id}/lines",
            new AddTabLinesRequest([new PlaceOrderItem(dish.Id, 1, null)])))
            .Content.ReadFromJsonAsync<StoreTabDto>();

        var struck = await owner.DeleteAsync($"api/storetabs/{tab!.Id}/lines/{tab.Lines[0].Id}");
        var after = await struck.Content.ReadFromJsonAsync<StoreTabDto>();
        Assert.Empty(after!.Lines);
        Assert.Equal(0m, after.Subtotal);
    }

    [Fact]
    public async Task ATableWithABilledInvoiceRefusesToBeClearedOrDeleted()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Tab Guard");
        var tab = await (await owner.PostAsJsonAsync("api/storetabs/open",
            new OpenTabRequest(table.Id, GuestName: "Still Eating")))
            .Content.ReadFromJsonAsync<StoreTabDto>();

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();
        await owner.PostAsJsonAsync($"api/storetabs/{tab!.Id}/lines",
            new AddTabLinesRequest([new PlaceOrderItem(dish.Id, 1, null)]));

        // Money is on the table — the broom and the axe both have to wait.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"api/storetables/{table.Id}/clear", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.DeleteAsync($"api/storetables/{table.Id}")).StatusCode);

        // Settle the bill — now the table clears like any other.
        (await owner.PostAsJsonAsync($"api/storetabs/{tab.Id}/close",
            new CloseTabRequest(PaymentMethod.CashOnDelivery, MarkPaid: true))).EnsureSuccessStatusCode();
        var floor = await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables");
        Assert.False(floor!.First(t => t.Id == table.Id).IsOccupied);
    }

    [Fact]
    public async Task ABillCanBeParkedOpenWithNoTableAtAll()
    {
        var owner = await OwnerAsync();

        // TableId 0: a walk-up who will pay later — no table anywhere in sight.
        var tab = await (await owner.PostAsJsonAsync("api/storetabs/open",
            new OpenTabRequest(0, GuestName: "Pays Later")))
            .Content.ReadFromJsonAsync<StoreTabDto>();
        Assert.Equal(0, tab!.TableId);

        // A second parked bill can run at the same time — they never merge.
        var second = await (await owner.PostAsJsonAsync("api/storetabs/open",
            new OpenTabRequest(0, GuestName: "Also Waiting")))
            .Content.ReadFromJsonAsync<StoreTabDto>();
        Assert.NotEqual(tab.Id, second!.Id);

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();
        await owner.PostAsJsonAsync($"api/storetabs/{tab.Id}/lines",
            new AddTabLinesRequest([new PlaceOrderItem(dish.Id, 1, null)]));

        // Settling it bills a counter handover: no table tag, no delivery fee.
        var order = await (await owner.PostAsJsonAsync($"api/storetabs/{tab.Id}/close",
            new CloseTabRequest(PaymentMethod.CashOnDelivery, MarkPaid: true)))
            .Content.ReadFromJsonAsync<OrderDto>();
        Assert.Null(order!.TableName);
        Assert.Equal(0m, order.DeliveryFee);
        Assert.Contains("Counter", order.DeliveryAddress);

        // Tidy up the second parked bill.
        await owner.PostAsJsonAsync($"api/storetabs/{second.Id}/close",
            new CloseTabRequest(PaymentMethod.CashOnDelivery));
    }

    [Fact]
    public async Task AnAnonymousCounterSaleNeedsNoCustomerNoTableAndNoScooter()
    {
        var owner = await OwnerAsync();
        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();

        // Customer id 0 = whoever is standing at the till right now.
        var order = await (await owner.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            0, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(dish.Id, 2, null)],
            MarkPaid: true, CounterSale: true))).Content.ReadFromJsonAsync<OrderDto>();

        Assert.Equal(OrderType.Pickup, order!.OrderType);
        Assert.Equal(0m, order.DeliveryFee);
        Assert.True(order.IsPaid);
        Assert.Equal(order.Subtotal + order.ServiceFee + order.TaxAmount, order.Total);
        Assert.Contains("Counter", order.DeliveryAddress);
    }

    [Fact]
    public async Task OneStoreCannotTouchAnothersTabs()
    {
        var marco = await OwnerAsync("marco@majidfood.com");
        var table = await CreateTableAsync(marco, "Tab Marco");
        var tab = await (await marco.PostAsJsonAsync("api/storetabs/open",
            new OpenTabRequest(table.Id, GuestName: "Marco Guest")))
            .Content.ReadFromJsonAsync<StoreTabDto>();

        var sara = await OwnerAsync("sara@majidfood.com");
        Assert.Equal(HttpStatusCode.NotFound,
            (await sara.GetAsync($"api/storetabs/table/{table.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await sara.PostAsJsonAsync($"api/storetabs/{tab!.Id}/close",
                new CloseTabRequest(PaymentMethod.CashOnDelivery))).StatusCode);
    }
}
