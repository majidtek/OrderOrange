using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// The dining room. What matters: one shop can never see or seat another's floor, a
/// dine-in order carries its table to the kitchen, and ordering at a table seats the
/// party there without a second step.
/// </summary>
public class StoreTableTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StoreTableTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> OwnerAsync(string email = "marco@majidfood.com")
    {
        var client = _factory.CreateClient();
        await client.SignInAsync(email);
        return client;
    }

    private static async Task<StoreTableDto> CreateTableAsync(HttpClient owner, string name, string type = "indoor", int seats = 4)
    {
        var response = await owner.PostAsJsonAsync("api/storetables", new SaveStoreTableRequest(name, type, seats));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoreTableDto>())!;
    }

    [Fact]
    public async Task ATableIsCreatedWithTypeAndSeats()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Terrace 1", "outdoor", 6);

        Assert.Equal("outdoor", table.Type);
        Assert.Equal(6, table.Seats);
        Assert.False(table.IsOccupied);
    }

    [Fact]
    public async Task DuplicateTableNamesOnOneFloorAreRefused()
    {
        var owner = await OwnerAsync();
        await CreateTableAsync(owner, "Dup T1");

        var again = await owner.PostAsJsonAsync("api/storetables", new SaveStoreTableRequest("Dup T1", "indoor", 2));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task AnUnknownTableTypeIsRefused()
    {
        var owner = await OwnerAsync();
        var response = await owner.PostAsJsonAsync("api/storetables", new SaveStoreTableRequest("T9", "rooftop", 2));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OneStoreCannotTouchAnothersFloor()
    {
        var marco = await OwnerAsync("marco@majidfood.com");
        var table = await CreateTableAsync(marco, "Marco Floor 1");

        var sara = await OwnerAsync("sara@majidfood.com");
        var theirList = await sara.GetFromJsonAsync<List<StoreTableDto>>("api/storetables");

        Assert.DoesNotContain(theirList!, t => t.Id == table.Id);
        Assert.Equal(HttpStatusCode.NotFound,
            (await sara.PostAsJsonAsync($"api/storetables/{table.Id}/seat", new SeatTableRequest(GuestName: "Intruder"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sara.DeleteAsync($"api/storetables/{table.Id}")).StatusCode);
    }

    [Fact]
    public async Task SeatingFromTheBookAndClearingWork()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Seat Test");
        var customer = await (await owner.PostAsJsonAsync("api/storecustomers",
            new SaveStoreCustomerRequest("Table Guest", "+968 9222 0001", "—")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var seated = await (await owner.PostAsJsonAsync($"api/storetables/{table.Id}/seat",
            new SeatTableRequest(customer!.Id))).Content.ReadFromJsonAsync<StoreTableDto>();
        Assert.True(seated!.IsOccupied);
        Assert.Equal("Table Guest", seated.CustomerName);

        var cleared = await (await owner.PostAsJsonAsync($"api/storetables/{table.Id}/clear", new { }))
            .Content.ReadFromJsonAsync<StoreTableDto>();
        Assert.False(cleared!.IsOccupied);
    }

    [Fact]
    public async Task AWalkInIsSeatedByNameAlone()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Walk-in T");

        var seated = await (await owner.PostAsJsonAsync($"api/storetables/{table.Id}/seat",
            new SeatTableRequest(GuestName: "Abu Khalid"))).Content.ReadFromJsonAsync<StoreTableDto>();

        Assert.True(seated!.IsOccupied);
        Assert.Equal("Abu Khalid", seated.GuestName);
        Assert.Null(seated.StoreCustomerId);
    }

    [Fact]
    public async Task ADineInOrderCarriesItsTableAndSeatsTheParty()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Order T5");
        var customer = await (await owner.PostAsJsonAsync("api/storecustomers",
            new SaveStoreCustomerRequest("Diner", "+968 9222 0002", "—")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();

        var order = await (await owner.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            customer!.Id, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(dish.Id, 1, null)],
            TableId: table.Id))).Content.ReadFromJsonAsync<OrderDto>();

        // The kitchen sees which table the food goes to…
        Assert.Equal("Order T5", order!.TableName);

        // …and the floor plan already shows the diner seated there.
        var floor = await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables");
        var seated = floor!.First(t => t.Id == table.Id);
        Assert.Equal(customer.Id, seated.StoreCustomerId);
    }

    // ---------- The 2D floor plan ----------

    [Fact]
    public async Task ATableRemembersItsFloorAndWhereItWasDropped()
    {
        var owner = await OwnerAsync();
        var created = await (await owner.PostAsJsonAsync("api/storetables",
            new SaveStoreTableRequest("Plan T1", "outdoor", 4, "Terrace", "round")))
            .Content.ReadFromJsonAsync<StoreTableDto>();
        Assert.Equal("Terrace", created!.Floor);
        Assert.Equal("round", created.Shape);

        // A shape the plan can't draw is refused, not silently stored.
        var badShape = await owner.PostAsJsonAsync("api/storetables",
            new SaveStoreTableRequest("Plan T2", "indoor", 4, "", "triangle"));
        Assert.Equal(HttpStatusCode.BadRequest, badShape.StatusCode);

        // Dragged to the window corner — the drop position comes back and sticks.
        var moved = await (await owner.PostAsJsonAsync($"api/storetables/{created.Id}/position",
            new MoveTableRequest(81.5, 22.25))).Content.ReadFromJsonAsync<StoreTableDto>();
        Assert.Equal(81.5, moved!.X);
        Assert.Equal(22.25, moved.Y);

        var listed = (await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables"))!
            .First(t => t.Id == created.Id);
        Assert.Equal(81.5, listed.X);
        Assert.Equal("Terrace", listed.Floor);
    }

    [Fact]
    public async Task ADropOutsideTheRoomIsPulledBackToTheEdge()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Clamp T");

        var moved = await (await owner.PostAsJsonAsync($"api/storetables/{table.Id}/position",
            new MoveTableRequest(-30, 400))).Content.ReadFromJsonAsync<StoreTableDto>();

        Assert.Equal(0, moved!.X);
        Assert.Equal(100, moved.Y);
    }

    [Fact]
    public async Task MovingToAnotherFloorKeepsTheTableSeatable()
    {
        var owner = await OwnerAsync();
        var table = await CreateTableAsync(owner, "Floor Mover");

        var moved = await (await owner.PostAsJsonAsync($"api/storetables/{table.Id}/position",
            new MoveTableRequest(50, 50, "Rooftop"))).Content.ReadFromJsonAsync<StoreTableDto>();
        Assert.Equal("Rooftop", moved!.Floor);

        var seated = await (await owner.PostAsJsonAsync($"api/storetables/{table.Id}/seat",
            new SeatTableRequest(GuestName: "Upstairs Guest"))).Content.ReadFromJsonAsync<StoreTableDto>();
        Assert.True(seated!.IsOccupied);
    }

    [Fact]
    public async Task RenamingAFloorCarriesEveryTableWithIt()
    {
        var owner = await OwnerAsync();
        await owner.PostAsJsonAsync("api/storetables", new SaveStoreTableRequest("Rn 1", "indoor", 2, "Old Wing"));
        await owner.PostAsJsonAsync("api/storetables", new SaveStoreTableRequest("Rn 2", "indoor", 2, "Old Wing"));

        var response = await owner.PostAsJsonAsync("api/storetables/floors/rename",
            new RenameFloorRequest("Old Wing", "Garden Wing"));
        response.EnsureSuccessStatusCode();

        var floors = (await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables"))!
            .Where(t => t.Name.StartsWith("Rn ")).Select(t => t.Floor).Distinct().ToList();
        Assert.Equal(["Garden Wing"], floors);

        // The main floor is the anchor of the plan — it cannot be renamed away.
        var main = await owner.PostAsJsonAsync("api/storetables/floors/rename", new RenameFloorRequest("", "Nope"));
        Assert.Equal(HttpStatusCode.BadRequest, main.StatusCode);
    }

    [Fact]
    public async Task OneStoreCannotDragAnothersTables()
    {
        var marco = await OwnerAsync("marco@majidfood.com");
        var table = await CreateTableAsync(marco, "Marco Plan T");

        var sara = await OwnerAsync("sara@majidfood.com");
        var response = await sara.PostAsJsonAsync($"api/storetables/{table.Id}/position",
            new MoveTableRequest(10, 10));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnotherStoresTableCannotBeUsedOnAnOrder()
    {
        var sara = await OwnerAsync("sara@majidfood.com");
        var saraTable = await CreateTableAsync(sara, "Sara T1");

        var marco = await OwnerAsync("marco@majidfood.com");
        var customer = await (await marco.PostAsJsonAsync("api/storecustomers",
            new SaveStoreCustomerRequest("Wrong Floor", "+968 9222 0003", "—")))
            .Content.ReadFromJsonAsync<StoreCustomerDto>();
        var menu = await marco.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var dish = menu!.SelectMany(c => c.Items).First();

        var response = await marco.PostAsJsonAsync("api/orders/counter", new PlaceCounterOrderRequest(
            customer!.Id, PaymentMethod.CashOnDelivery, [new PlaceOrderItem(dish.Id, 1, null)],
            TableId: saraTable.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
