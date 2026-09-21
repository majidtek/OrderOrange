using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OrderOrange.ApiServer.Data;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Serving windows and advance-notice items. The window logic itself is pinned as pure
/// unit tests (every clock case), and the enforcement is proven through the API: what
/// the menu refuses to sell, the order endpoint refuses to accept.
/// </summary>
public class AvailabilityWindowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AvailabilityWindowTests(ApiFactory factory) => _factory = factory;

    // ---------- The clock rules, exhaustively ----------

    private static DateTime At(DayOfWeek day, int hour, int minute = 0)
    {
        // 2026-08-09 was a Sunday; walk forward to the wanted weekday.
        var date = new DateTime(2026, 8, 9).AddDays(((int)day - (int)DayOfWeek.Sunday + 7) % 7);
        return date.AddHours(hour).AddMinutes(minute);
    }

    [Theory]
    [InlineData(6 * 60, 11 * 60, 8, true)]     // breakfast at 08:00 — inside
    [InlineData(6 * 60, 11 * 60, 12, false)]   // breakfast at noon — outside
    [InlineData(6 * 60, 11 * 60, 6, true)]     // opening minute counts
    [InlineData(6 * 60, 11 * 60, 11, false)]   // closing minute does not
    [InlineData(18 * 60, 2 * 60, 23, true)]    // overnight window, before midnight
    [InlineData(18 * 60, 2 * 60, 1, true)]     // overnight window, after midnight
    [InlineData(18 * 60, 2 * 60, 12, false)]   // overnight window, midday
    public void TimeWindows(int from, int to, int hourNow, bool expected) =>
        Assert.Equal(expected, ItemAvailability.IsWithinWindow(from, to, "", At(DayOfWeek.Monday, hourNow)));

    [Fact]
    public void DayRestrictions()
    {
        // Friday-and-Saturday-only, no time restriction.
        Assert.True(ItemAvailability.IsWithinWindow(null, null, "5,6", At(DayOfWeek.Friday, 14)));
        Assert.True(ItemAvailability.IsWithinWindow(null, null, "5,6", At(DayOfWeek.Saturday, 9)));
        Assert.False(ItemAvailability.IsWithinWindow(null, null, "5,6", At(DayOfWeek.Tuesday, 14)));

        // Day AND time must both hold.
        Assert.True(ItemAvailability.IsWithinWindow(6 * 60, 11 * 60, "5", At(DayOfWeek.Friday, 8)));
        Assert.False(ItemAvailability.IsWithinWindow(6 * 60, 11 * 60, "5", At(DayOfWeek.Friday, 13)));
        Assert.False(ItemAvailability.IsWithinWindow(6 * 60, 11 * 60, "5", At(DayOfWeek.Monday, 8)));
    }

    [Fact]
    public void NoWindowMeansAlways() =>
        Assert.True(ItemAvailability.IsWithinWindow(null, null, "", At(DayOfWeek.Wednesday, 3)));

    [Fact]
    public void LeadTimeItemsAreAlwaysOrderable()
    {
        var cake = new MenuItemDto(1, 1, "Cake", "", 10m, true, "🎂", false,
            AvailableFromMinutes: 6 * 60, AvailableToMinutes: 7 * 60, AvailableDays: "1", LeadTimeDays: 2);

        // Its window is a one-hour slot on Mondays — but with two days' notice it may be
        // ORDERED at any time; the window says when it is made.
        Assert.True(cake.OrderableAt(At(DayOfWeek.Friday, 23)));
    }

    // ---------- Enforcement through the API ----------

    private async Task<(HttpClient Owner, HttpClient Customer, int StoreId, int CategoryId)> WorldAsync()
    {
        var owner = _factory.CreateClient();
        var login = await owner.SignInAsync("marco@majidfood.com");
        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");

        var customer = _factory.CreateClient();
        await customer.SignInAsync("ahmed@majidfood.com");
        return (owner, customer, login.RestaurantId!.Value, menu!.First().Id);
    }

    private static async Task<MenuItemDto> CreateAsync(HttpClient owner, SaveMenuItemRequest req)
    {
        var response = await owner.PostAsJsonAsync("api/menu/items", req);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MenuItemDto>())!;
    }

    private async Task ApproveAsync(int itemId)
    {
        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");
        (await admin.PostAsync($"api/admin/products/{itemId}/approve", null)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AnItemOutsideItsWindowCannotBeOrdered()
    {
        var (owner, customer, storeId, categoryId) = await WorldAsync();

        // A window that is CLOSED right now: a one-minute slot at the opposite hour.
        var nowMinute = (int)DateTime.Now.TimeOfDay.TotalMinutes;
        var far = (nowMinute + 12 * 60) % (24 * 60);
        var item = await CreateAsync(owner, new SaveMenuItemRequest(categoryId, "Window Test Dish", "", 2m, "🍳", false, true,
            AvailableFromMinutes: far, AvailableToMinutes: (far + 1) % (24 * 60)));
        await ApproveAsync(item.Id);

        var response = await customer.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            storeId, 0, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(item.Id, 2, null)], CardId: null, OrderType: OrderType.Pickup));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("can be ordered", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ALeadTimeItemSchedulesTheOrderForThePromisedDay()
    {
        var (owner, customer, storeId, categoryId) = await WorldAsync();

        var cake = await CreateAsync(owner, new SaveMenuItemRequest(categoryId, "Two Day Cake", "", 15m, "🎂", false, true,
            LeadTimeDays: 2));
        await ApproveAsync(cake.Id);

        var response = await customer.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            storeId, 0, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(cake.Id, 1, null)], CardId: null, OrderType: OrderType.Pickup));
        response.EnsureSuccessStatusCode();

        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal(DateTime.Today.AddDays(2), order!.ScheduledFor);
    }

    /// <summary>
    /// The bug this work uncovered: ordering priced from SQL while menus render from the
    /// catalog, so a product added after launch was visible but unorderable. Now both
    /// read the same store — this pins that a freshly added+approved item can be bought.
    /// </summary>
    [Fact]
    public async Task AFreshlyAddedProductCanActuallyBeOrdered()
    {
        var (owner, customer, storeId, categoryId) = await WorldAsync();

        var item = await CreateAsync(owner, new SaveMenuItemRequest(categoryId, "Fresh Catalog Dish", "", 3m, "🥙", false, true));
        await ApproveAsync(item.Id);

        var response = await customer.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            storeId, 0, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(item.Id, 2, null)], CardId: null, OrderType: OrderType.Pickup));
        response.EnsureSuccessStatusCode();

        var order = await response.Content.ReadFromJsonAsync<OrderDto>();
        Assert.Equal(6m, order!.Subtotal);
        Assert.Null(order.ScheduledFor);
    }

    [Fact]
    public async Task APendingProductStillCannotBeOrderedEvenById()
    {
        var (owner, customer, storeId, categoryId) = await WorldAsync();

        var pending = await CreateAsync(owner, new SaveMenuItemRequest(categoryId, "Still Pending Dish", "", 3m, "🥘", false, true));
        // No approval on purpose.

        var response = await customer.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            storeId, 0, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(pending.Id, 1, null)], CardId: null, OrderType: OrderType.Pickup));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
