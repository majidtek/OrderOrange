using OrderOrange.Shared;

namespace OrderOrange.ClientWeb.Services;

/// <summary>
/// The tab's shopping cart. The model still allows several restaurants (each becomes its
/// own order at checkout), but the STORE PAGES keep the basket to ONE store at a time —
/// Majed 2026-09-05: "user can do order just from one partner until finish, or clear
/// basket". Before adding from a different store the page asks: finish that order, or
/// clear the basket. <see cref="OtherStore"/> is the question they ask.
/// </summary>
public class CartState
{
    /// <summary>
    /// The store already in the basket when it is NOT the given one — the caller must
    /// clear the basket (or send the customer to checkout) before adding. Null when the
    /// basket is empty or already belongs to this store.
    /// </summary>
    public RestaurantCardDto? OtherStore(int restaurantId) =>
        Lines.Select(l => l.Restaurant).FirstOrDefault(r => r.Id != restaurantId);

    public sealed class Line
    {
        /// <summary>Which kitchen this line will be ordered from.</summary>
        public required RestaurantCardDto Restaurant { get; init; }
        public required MenuItemDto Item { get; init; }
        public int Quantity { get; set; } = 1;
        public string? Notes { get; set; }
        public decimal LineTotal => Item.FinalPrice * Quantity;
    }

    /// <summary>One restaurant's share of the basket — this becomes one order.</summary>
    public sealed record Group(RestaurantCardDto Restaurant, List<Line> Lines)
    {
        public int Count => Lines.Sum(l => l.Quantity);
        public decimal Subtotal => Lines.Sum(l => l.LineTotal);
        public decimal DeliveryFee => Restaurant.DeliveryFee;
        public bool MeetsMinimum => Subtotal >= Restaurant.MinOrder;
        public decimal Missing => Math.Max(0, Restaurant.MinOrder - Subtotal);
    }

    public List<Line> Lines { get; } = [];

    /// <summary>The basket split per restaurant, in the order the customer started each one.</summary>
    public List<Group> Groups =>
        Lines.GroupBy(l => l.Restaurant.Id)
             .Select(g => new Group(g.First().Restaurant, g.ToList()))
             .ToList();

    /// <summary>
    /// The only restaurant in the basket, or null when it spans several. Screens that show
    /// a single header use this; anything that prices or submits must go through Groups.
    /// </summary>
    public RestaurantCardDto? Restaurant =>
        Lines.Select(l => l.Restaurant).DistinctBy(r => r.Id).ToList() is [var only] ? only : null;

    public bool IsMultiStore => Lines.DistinctBy(l => l.Restaurant.Id).Count() > 1;

    /// <summary>The address picked in the home hero — checkout preselects it.</summary>
    public int? PreferredAddressId { get; set; }

    public int Count => Lines.Sum(l => l.Quantity);
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);

    /// <summary>Every restaurant charges its own delivery — a basket of three pays three.</summary>
    public decimal DeliveryFees => Groups.Sum(g => g.DeliveryFee);

    /// <summary>What partner discounts saved versus full prices — shown on the bill.</summary>
    public decimal ProductSavings => Lines.Sum(l => (l.Item.Price - l.Item.FinalPrice) * l.Quantity);

    /// <summary>True only when EVERY restaurant in the basket has reached its minimum.</summary>
    public bool MeetsMinimum => Groups.All(g => g.MeetsMinimum);

    /// <summary>The restaurants still short of their minimum, so the UI can name them.</summary>
    public List<Group> BelowMinimum => Groups.Where(g => !g.MeetsMinimum).ToList();

    public event Action? Changed;

    public void Add(RestaurantCardDto restaurant, MenuItemDto item)
    {
        var line = Lines.FirstOrDefault(l => l.Item.Id == item.Id && l.Restaurant.Id == restaurant.Id);
        if (line is null) Lines.Add(new Line { Restaurant = restaurant, Item = item });
        else line.Quantity++;
        Changed?.Invoke();
    }

    /// <summary>
    /// How many of this product are in the basket. Menu-item ids are unique per restaurant
    /// for real rows but NOT for the shared virtual template menus, so the restaurant has
    /// to be part of the question.
    /// </summary>
    public int QuantityOf(int restaurantId, int menuItemId) =>
        Lines.FirstOrDefault(l => l.Restaurant.Id == restaurantId && l.Item.Id == menuItemId)?.Quantity ?? 0;

    public Line? FindLine(int restaurantId, int menuItemId) =>
        Lines.FirstOrDefault(l => l.Restaurant.Id == restaurantId && l.Item.Id == menuItemId);

    /// <summary>
    /// Sets a product's quantity outright rather than adding to it. The dish sheet opens
    /// showing what is already in the basket, so what comes back is the new total — adding
    /// it on top would double the line every time it was reopened. Zero removes the line.
    /// </summary>
    public void SetQuantity(RestaurantCardDto restaurant, MenuItemDto item, int quantity, string? notes)
    {
        var line = FindLine(restaurant.Id, item.Id);
        if (quantity <= 0)
        {
            if (line is not null) Lines.Remove(line);
        }
        else if (line is null)
        {
            Lines.Add(new Line { Restaurant = restaurant, Item = item, Quantity = quantity, Notes = notes });
        }
        else
        {
            line.Quantity = quantity;
            line.Notes = notes;
        }
        Changed?.Invoke();
    }

    public void Increment(Line line)
    {
        line.Quantity++;
        Changed?.Invoke();
    }

    public void Decrement(Line line)
    {
        line.Quantity--;
        if (line.Quantity <= 0) Lines.Remove(line);
        Changed?.Invoke();
    }

    public void Clear()
    {
        Lines.Clear();
        Changed?.Invoke();
    }

    /// <summary>Drops just one restaurant's share, leaving the rest of the basket alone.</summary>
    public void ClearGroup(int restaurantId)
    {
        Lines.RemoveAll(l => l.Restaurant.Id == restaurantId);
        Changed?.Invoke();
    }

    /// <summary>The items of one restaurant, ready to submit as that restaurant's order.</summary>
    public List<PlaceOrderItem> ToOrderItems(int restaurantId) =>
        Lines.Where(l => l.Restaurant.Id == restaurantId)
             .Select(l => new PlaceOrderItem(l.Item.Id, l.Quantity, l.Notes))
             .ToList();

    // ---------- Persistence (a page reload starts a new circuit — don't lose the basket) ----------

    public record LineSnapshot(MenuItemDto Item, int Quantity, string? Notes, RestaurantCardDto? Restaurant = null);
    public record Snapshot(RestaurantCardDto? Restaurant, List<LineSnapshot> Lines);

    public Snapshot ToSnapshot() =>
        new(Restaurant, Lines.Select(l => new LineSnapshot(l.Item, l.Quantity, l.Notes, l.Restaurant)).ToList());

    /// <summary>
    /// Restores a stored basket. Only meant for an empty, freshly-created cart. Baskets
    /// saved before the multi-restaurant change have the restaurant at the top level
    /// instead of on each line, so both shapes are accepted.
    /// </summary>
    public void RestoreFrom(Snapshot snapshot)
    {
        if (Lines.Count > 0) return;
        foreach (var line in snapshot.Lines)
        {
            var restaurant = line.Restaurant ?? snapshot.Restaurant;
            if (restaurant is null) continue;                  // unusable without its kitchen
            Lines.Add(new Line
            {
                Restaurant = restaurant,
                Item = line.Item,
                Quantity = line.Quantity,
                Notes = line.Notes,
            });
        }
        if (Lines.Count > 0) Changed?.Invoke();
    }
}
