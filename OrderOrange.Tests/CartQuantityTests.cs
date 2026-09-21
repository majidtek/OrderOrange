extern alias clientweb;
using clientweb::OrderOrange.ClientWeb.Services;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// The dish sheet opens showing what is already in the basket, so what it returns is the
/// new total — not an amount to add. Getting that backwards silently doubled a line every
/// time the customer reopened a dish.
/// </summary>
public class CartQuantityTests
{
    private static RestaurantCardDto Store(int id = 1) =>
        new(id, "Bella Napoli", "", "Italian", "🍕", "#fff", "Muscat", 4.5, 10, 0.5m, 2m, 25, true);

    private static MenuItemDto Dish(int id = 100, decimal price = 2m) =>
        new(id, 1, "Fries", "", price, true, "🍟", false);

    [Fact]
    public void SetQuantity_ReplacesRatherThanAccumulates()
    {
        var cart = new CartState();
        var store = Store();
        var fries = Dish();

        cart.SetQuantity(store, fries, 3, null);
        Assert.Equal(3, cart.QuantityOf(store.Id, fries.Id));

        // Reopening the sheet and confirming 3 again must leave 3, not 6.
        cart.SetQuantity(store, fries, 3, null);
        Assert.Equal(3, cart.QuantityOf(store.Id, fries.Id));

        cart.SetQuantity(store, fries, 5, null);
        Assert.Equal(5, cart.QuantityOf(store.Id, fries.Id));
        Assert.Single(cart.Lines);
    }

    [Fact]
    public void SetQuantity_ZeroRemovesTheLine()
    {
        var cart = new CartState();
        var store = Store();
        var fries = Dish();

        cart.SetQuantity(store, fries, 2, null);
        cart.SetQuantity(store, fries, 0, null);

        Assert.Empty(cart.Lines);
        Assert.Equal(0, cart.QuantityOf(store.Id, fries.Id));
    }

    [Fact]
    public void SetQuantity_KeepsNotes()
    {
        var cart = new CartState();
        var store = Store();
        var fries = Dish();

        cart.SetQuantity(store, fries, 1, "no salt");
        Assert.Equal("no salt", cart.FindLine(store.Id, fries.Id)!.Notes);

        cart.SetQuantity(store, fries, 2, "extra crispy");
        Assert.Equal("extra crispy", cart.FindLine(store.Id, fries.Id)!.Notes);
        Assert.Equal(2, cart.QuantityOf(store.Id, fries.Id));
    }

    [Fact]
    public void SameDishIdInTwoStores_StaysSeparate()
    {
        // Virtual template menus share item ids across stores, so the store has to be
        // part of the identity or one basket line would overwrite the other.
        var cart = new CartState();
        var bella = Store(1);
        var other = Store(2);
        var fries = Dish();

        cart.SetQuantity(bella, fries, 2, null);
        cart.SetQuantity(other, fries, 5, null);

        Assert.Equal(2, cart.QuantityOf(bella.Id, fries.Id));
        Assert.Equal(5, cart.QuantityOf(other.Id, fries.Id));
        Assert.Equal(2, cart.Lines.Count);
    }

    [Fact]
    public void Add_StillIncrementsForTheQuickPlusButton()
    {
        // The "+" on a menu row is a different gesture and must keep adding one.
        var cart = new CartState();
        var store = Store();
        var fries = Dish();

        cart.Add(store, fries);
        cart.Add(store, fries);

        Assert.Equal(2, cart.QuantityOf(store.Id, fries.Id));
    }
}
