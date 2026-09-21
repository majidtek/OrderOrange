using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// The customer home "Offers" strip. It is public, it must stay small, and it must not
/// be dominated by a single store — that is what makes it feel like a real offers row.
/// </summary>
public class DealsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DealsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Deals_ArePublicAndDiscounted()
    {
        var anonymous = _factory.CreateClient();      // no token — same as a fresh visitor
        var deals = await anonymous.GetFromJsonAsync<List<DealDto>>("api/restaurants/deals");

        Assert.NotNull(deals);
        Assert.All(deals!, d =>
        {
            Assert.True(d.DiscountPercent > 0, $"{d.Name} has no discount");
            Assert.True(d.FinalPrice < d.Price, $"{d.Name} is not cheaper than its list price");
            Assert.Equal(Math.Round(d.Price - d.FinalPrice, 3), d.Saving);
            Assert.False(string.IsNullOrWhiteSpace(d.RestaurantName));
        });
    }

    [Fact]
    public async Task Deals_RankByMoneySaved()
    {
        var deals = await _factory.CreateClient().GetFromJsonAsync<List<DealDto>>("api/restaurants/deals");

        // Biggest saving first — a cheap side at 35% must not outrank a costly dish at 20%.
        var savings = deals!.Select(d => d.Saving).ToList();
        Assert.Equal(savings.OrderByDescending(s => s).ToList(), savings);
    }

    [Fact]
    public async Task Deals_ShowAtMostTwoPerStore()
    {
        var deals = await _factory.CreateClient().GetFromJsonAsync<List<DealDto>>("api/restaurants/deals");

        foreach (var group in deals!.GroupBy(d => d.RestaurantId))
            Assert.True(group.Count() <= 2, $"store {group.Key} filled {group.Count()} of the strip");
    }

    [Theory]
    [InlineData(0, 1)]      // clamped up
    [InlineData(5, 5)]
    [InlineData(999, 30)]   // clamped down
    public async Task Deals_ClampTheRequestedCount(int asked, int max)
    {
        var deals = await _factory.CreateClient()
            .GetFromJsonAsync<List<DealDto>>($"api/restaurants/deals?take={asked}");

        Assert.NotNull(deals);
        Assert.True(deals!.Count <= max, $"asked {asked}, got {deals.Count}");
    }

    [Fact]
    public void FinalPrice_RoundsToThreeDecimals()
    {
        // 0.800 at 35% is 0.52 exactly; 2.600 at 35% is 1.69 — both must stay 3-dp clean.
        var deal = new DealDto(1, "x", "🍽️", null, 2.600m, 35m, 1, "s", "🍽️", "c", "a", true);
        Assert.Equal(1.690m, deal.FinalPrice);
        Assert.Equal(0.910m, deal.Saving);
    }
}
