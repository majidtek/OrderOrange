namespace OrderOrange.RestaurantWeb.Services;

/// <summary>The fixed set of store-expense categories, with their icon and label.</summary>
public static class BillCategories
{
    public static readonly (string Key, string Emoji, string Label)[] All =
    [
        ("electricity", "⚡", "Electricity"),
        ("water", "💧", "Water"),
        ("phone", "📞", "Phone"),
        ("internet", "🌐", "Internet"),
        ("rent", "🏠", "Rent"),
        ("salary", "👥", "Salaries"),
        ("gas", "🔥", "Gas"),
        ("supplies", "📦", "Supplies"),
        ("maintenance", "🔧", "Maintenance"),
        ("tax", "🏛️", "Tax & fees"),
        ("other", "🧾", "Other")
    ];

    public static string Emoji(string key) => All.FirstOrDefault(c => c.Key == key).Emoji ?? "🧾";

    public static string Label(string key) => All.FirstOrDefault(c => c.Key == key).Label ?? "Other";
}
