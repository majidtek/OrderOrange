using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

public class MenuAndAdminTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public MenuAndAdminTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Owner_CanAddCategoryAndDish()
    {
        var owner = _factory.CreateClient();
        await owner.SignInAsync("sara@majidfood.com"); // Burger Bros

        (await owner.PostAsJsonAsync("api/menu/categories", new SaveCategoryRequest("Test Specials", 9)))
            .EnsureSuccessStatusCode();

        var menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var specials = menu!.Single(c => c.Name == "Test Specials");

        (await owner.PostAsJsonAsync("api/menu/items",
            new SaveMenuItemRequest(specials.Id, "Test Burger", "For tests only", 1.500m, "🧪", false, true)))
            .EnsureSuccessStatusCode();

        menu = await owner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        Assert.Contains(menu!.Single(c => c.Name == "Test Specials").Items, i => i.Name == "Test Burger");
    }

    [Fact]
    public async Task Owner_CannotTouchAnotherRestaurantsMenu()
    {
        var marco = _factory.CreateClient();
        await marco.SignInAsync("marco@majidfood.com"); // Bella Napoli
        var bellaMenu = await marco.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var bellaCategory = bellaMenu![0];

        var sara = _factory.CreateClient();
        await sara.SignInAsync("sara@majidfood.com"); // Burger Bros

        // Sara tries to put a dish into Marco's category — the category lookup is
        // scoped to her own restaurant, so it must fail.
        var response = await sara.PostAsJsonAsync("api/menu/items",
            new SaveMenuItemRequest(bellaCategory.Id, "Sneaky Burger", "", 1.000m, "🚫", false, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Customer_CannotOpenOwnerOrAdminEndpoints()
    {
        var client = _factory.CreateClient();
        await client.SignInAsync("customer@majidfood.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/menu")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/admin/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("api/orders/driver/available")).StatusCode);
    }

    [Fact]
    public async Task Admin_Dashboard_ReflectsTheSeededWorld()
    {
        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");

        var dashboard = await admin.GetFromJsonAsync<AdminDashboardDto>("api/admin/dashboard");

        Assert.NotNull(dashboard);
        // Another test in this class may approve Curry Corner first, so assert on
        // the sum rather than the split.
        Assert.Equal(5, dashboard.TotalRestaurants + dashboard.PendingRestaurantApprovals);
        Assert.Equal(3, dashboard.TotalDrivers);
        Assert.True(dashboard.TotalOrders > 20);
        Assert.True(dashboard.TotalCommission > 0);
    }

    [Fact]
    public async Task Admin_CanApproveAPendingRestaurant()
    {
        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");

        var restaurants = await admin.GetFromJsonAsync<List<AdminRestaurantDto>>("api/admin/restaurants");
        var curry = restaurants!.Single(r => r.Name == "Curry Corner");
        Assert.False(curry.IsApproved);

        (await admin.PostAsync($"api/admin/restaurants/{curry.Id}/approve", null)).EnsureSuccessStatusCode();

        restaurants = await admin.GetFromJsonAsync<List<AdminRestaurantDto>>("api/admin/restaurants");
        Assert.True(restaurants!.Single(r => r.Name == "Curry Corner").IsApproved);
    }
}
