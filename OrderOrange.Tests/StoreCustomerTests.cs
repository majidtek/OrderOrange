using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// The store's own customer book, and the orders it takes on their behalf. Two things
/// matter most here: a shop must never see or touch another shop's customers, and a
/// counter order must be priced by exactly the same rules as one placed in the app.
/// </summary>
public class StoreCustomerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StoreCustomerTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> OwnerAsync(string email = "marco@majidfood.com")
    {
        var client = _factory.CreateClient();
        await client.SignInAsync(email);
        return client;
    }

    private static SaveStoreCustomerRequest New(string name, string phone, string address = "Villa 12, Al Khuwair") =>
        new(name, phone, address);

    [Fact]
    public async Task AddingACustomerNeedsNoAccountForThem()
    {
        var owner = await OwnerAsync();

        var response = await owner.PostAsJsonAsync("api/storecustomers", New("Salim Al Habsi", "+968 9111 0001"));
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<StoreCustomerDto>();
        Assert.NotNull(created);
        Assert.Equal("Salim Al Habsi", created!.Name);
        Assert.Equal(0, created.OrderCount);
        Assert.Null(created.LastOrderAt);
    }

    [Fact]
    public async Task TheSamePhoneCannotBeAddedTwiceToOneStore()
    {
        var owner = await OwnerAsync();
        await owner.PostAsJsonAsync("api/storecustomers", New("First Entry", "+968 9111 0002"));

        var again = await owner.PostAsJsonAsync("api/storecustomers", New("Same Number", "+968 9111 0002"));

        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Theory]
    [InlineData("", "+968 9111 9999", "Somewhere")]      // no name
    [InlineData("No Phone", "12", "Somewhere")]           // too few digits
    [InlineData("No Address", "+968 9111 9998", "")]      // nowhere to deliver
    public async Task IncompleteCustomersAreRejected(string name, string phone, string address)
    {
        var owner = await OwnerAsync();

        var response = await owner.PostAsJsonAsync("api/storecustomers", new SaveStoreCustomerRequest(name, phone, address));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>The whole point of scoping: one shop's book is invisible to another.</summary>
    [Fact]
    public async Task OneStoreCannotSeeOrEditAnothersCustomers()
    {
        var marco = await OwnerAsync("marco@majidfood.com");
        var created = await (await marco.PostAsJsonAsync("api/storecustomers", New("Marco's Regular", "+968 9111 0003")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var sara = await OwnerAsync("sara@majidfood.com");

        var theirList = await sara.GetFromJsonAsync<List<StoreCustomerDto>>("api/storecustomers");
        Assert.DoesNotContain(theirList!, c => c.Id == created!.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await sara.GetAsync($"api/storecustomers/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await sara.PutAsJsonAsync($"api/storecustomers/{created.Id}", New("Stolen", "+968 9111 0003"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sara.DeleteAsync($"api/storecustomers/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task CustomersAreOnlyForPartners()
    {
        var customer = _factory.CreateClient();
        await customer.SignInAsync("ahmed@majidfood.com");

        var response = await customer.GetAsync("api/storecustomers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemovingACustomerHidesThemButKeepsTheRecord()
    {
        var owner = await OwnerAsync();
        var created = await (await owner.PostAsJsonAsync("api/storecustomers", New("Passing Trade", "+968 9111 0004")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        (await owner.DeleteAsync($"api/storecustomers/{created!.Id}")).EnsureSuccessStatusCode();

        var list = await owner.GetFromJsonAsync<List<StoreCustomerDto>>("api/storecustomers");
        Assert.DoesNotContain(list!, c => c.Id == created.Id);
    }

    // ---------- Orders taken at the counter ----------

    [Fact]
    public async Task AStoreCanPlaceAnOrderForSomeoneWithNoAccount()
    {
        var owner = await OwnerAsync();
        var customer = await (await owner.PostAsJsonAsync("api/storecustomers", New("Phone Caller", "+968 9111 0010")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();

        var response = await owner.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            customer!.Id, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(dish.Id, 2, null)]));
        response.EnsureSuccessStatusCode();

        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Pending, order!.Status);
        Assert.StartsWith("MF-", order.Number);

        // Priced by the same rules as the app, and delivered where the book says.
        Assert.Equal(dish.FinalPrice * 2, order.Subtotal);
        Assert.Equal(customer.Address, order.DeliveryAddress);
    }

    [Fact]
    public async Task PlacingACounterOrderCountsAgainstTheCustomer()
    {
        var owner = await OwnerAsync();
        var customer = await (await owner.PostAsJsonAsync("api/storecustomers", New("Regular", "+968 9111 0011")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();

        await owner.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            customer!.Id, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(dish.Id, 1, null)]));

        var refreshed = (await owner.GetFromJsonAsync<List<StoreCustomerDto>>("api/storecustomers"))!
            .First(c => c.Id == customer.Id);

        Assert.Equal(1, refreshed.OrderCount);
        Assert.NotNull(refreshed.LastOrderAt);
    }

    [Fact]
    public async Task AStoreCannotPlaceAnOrderForAnotherStoresCustomer()
    {
        var marco = await OwnerAsync("marco@majidfood.com");
        var theirs = await (await marco.PostAsJsonAsync("api/storecustomers", New("Not Yours", "+968 9111 0012")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var sara = await OwnerAsync("sara@majidfood.com");
        var menu = await sara.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();

        var response = await sara.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            theirs!.Id, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(dish.Id, 1, null)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AStoreCannotSellAnotherStoresProduct()
    {
        var marco = await OwnerAsync("marco@majidfood.com");
        var customer = await (await marco.PostAsJsonAsync("api/storecustomers", New("Menu Check", "+968 9111 0013")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var sara = await OwnerAsync("sara@majidfood.com");
        var otherMenu = await sara.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var otherDish = otherMenu!.SelectMany(c => c.Items).First();

        var response = await marco.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            customer!.Id, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(otherDish.Id, 1, null)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnEmptyCounterOrderIsRejected()
    {
        var owner = await OwnerAsync();
        var customer = await (await owner.PostAsJsonAsync("api/storecustomers", New("Nothing Ordered", "+968 9111 0014")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var response = await owner.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            customer!.Id, PaymentMethod.CashOnDelivery, []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MarkingItPaidRecordsTheMoneyAsTaken()
    {
        var owner = await OwnerAsync();
        var customer = await (await owner.PostAsJsonAsync("api/storecustomers", New("Paid Upfront", "+968 9111 0015")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();

        var order = await (await owner.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            customer!.Id, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(dish.Id, 1, null)], MarkPaid: true)))
            .Content.ReadFromJsonAsync<OrderDto>();

        Assert.True(order!.IsPaid);
    }
}
