extern alias clientweb;
using clientweb::OrderOrange.ClientWeb.Services;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// "I have 10 rials, what can I buy?" — the answer has to be a suggestion, not a search
/// result. The catalog spans restaurants, groceries, pharmacies, florists and general
/// shops, and offering a pizza next to a flash drive is what makes a bot look stupid.
/// </summary>
public class BudgetCoherenceTests
{
    private static DishHitDto Hit(string name, decimal price, StoreType type) =>
        new(1, name, "", "🍽️", price, 1, "Shop", "🏪", true, type);

    [Fact]
    public void Food_and_electronics_are_never_offered_together()
    {
        var mixed = new List<DishHitDto>
        {
            Hit("Pizza", 3.5m, StoreType.Restaurant),
            Hit("Flash memory 64GB", 4.0m, StoreType.Shop),
            Hit("Shawarma", 1.2m, StoreType.Restaurant),
            Hit("USB cable", 2.0m, StoreType.Shop),
        };

        var picks = OrderBot.OneKindOfShop(mixed);

        Assert.All(picks, p => Assert.Equal(StoreType.Restaurant, p.StoreType));
        Assert.Equal(2, picks.Count);
    }

    /// <summary>
    /// Food wins even when it is outnumbered. Someone asking what they can buy on a food
    /// app means food, and "there were simply more phone chargers" is not a reason to
    /// answer with phone chargers.
    /// </summary>
    [Fact]
    public void Food_wins_even_when_outnumbered()
    {
        var mixed = new List<DishHitDto>
        {
            Hit("Charger", 2m, StoreType.Shop),
            Hit("Cable", 2m, StoreType.Shop),
            Hit("Case", 2m, StoreType.Shop),
            Hit("Powerbank", 2m, StoreType.Shop),
            Hit("Falafel wrap", 1m, StoreType.Restaurant),
        };

        var picks = OrderBot.OneKindOfShop(mixed);

        Assert.Single(picks);
        Assert.Equal("Falafel wrap", picks[0].Name);
    }

    /// <summary>With no food in budget, the answer still commits to one kind of shop
    /// rather than shrugging and listing everything.</summary>
    [Fact]
    public void Without_food_it_picks_the_richest_other_shop()
    {
        var mixed = new List<DishHitDto>
        {
            Hit("Paracetamol", 1m, StoreType.Pharmacy),
            Hit("Charger", 2m, StoreType.Shop),
            Hit("Cable", 2m, StoreType.Shop),
            Hit("Roses", 5m, StoreType.Flowers),
        };

        var picks = OrderBot.OneKindOfShop(mixed);

        Assert.All(picks, p => Assert.Equal(StoreType.Shop, p.StoreType));
        Assert.Equal(2, picks.Count);
    }

    [Fact]
    public void Nothing_in_gives_nothing_out() =>
        Assert.Empty(OrderBot.OneKindOfShop([]));

    /// <summary>A list that is already coherent passes through untouched.</summary>
    [Fact]
    public void An_all_food_list_is_left_alone()
    {
        var food = new List<DishHitDto>
        {
            Hit("Pizza", 3.5m, StoreType.Restaurant),
            Hit("Burger", 2.5m, StoreType.Restaurant),
        };

        Assert.Equal(2, OrderBot.OneKindOfShop(food).Count);
    }
}
