using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

public class BrowseTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public BrowseTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Browse_ShowsOnlyApprovedRestaurants()
    {
        var client = _factory.CreateClient();
        var restaurants = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants");

        Assert.NotNull(restaurants);
        Assert.Equal(4, restaurants.Count);
        Assert.DoesNotContain(restaurants, r => r.Name == "Curry Corner"); // seeded unapproved
    }

    [Fact]
    public async Task Browse_SearchFindsRestaurantByDishName()
    {
        var client = _factory.CreateClient();
        var restaurants = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants?search=Carbonara");

        Assert.NotNull(restaurants);
        Assert.Single(restaurants);
        Assert.Equal("Bella Napoli", restaurants[0].Name);
    }

    [Fact]
    public async Task Detail_ReturnsMenuGroupedByCategory()
    {
        var client = _factory.CreateClient();
        var all = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants");
        var bella = all!.Single(r => r.Name == "Bella Napoli");

        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{bella.Id}");

        Assert.NotNull(detail);
        // Three shelves of its own, plus the house Drinks shelf every store gets.
        Assert.Equal(4, detail.Categories.Count);
        Assert.Contains(detail.Categories, c => c.Name == "Pizzas" && c.Items.Count > 0);
        Assert.Contains(detail.Categories, c => c.Name == "Drinks" && c.Items.Any(i => i.Name == "Water"));
    }
}
