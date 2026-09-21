namespace OrderOrange.RestaurantWeb.Services;

/// <summary>Job roles a store hires for, with their icon and label.</summary>
public static class StaffRoles
{
    public static readonly (string Key, string Emoji, string Label)[] All =
    [
        ("barista", "☕", "Barista"),
        ("chef", "👨‍🍳", "Chef"),
        ("cook", "🍳", "Cook"),
        ("cashier", "🧾", "Cashier"),
        ("waiter", "🍽️", "Waiter"),
        ("manager", "📋", "Manager"),
        ("cleaner", "🧹", "Cleaner"),
        ("driver", "🛵", "Driver"),
        ("helper", "🤝", "Helper"),
        ("other", "👤", "Other")
    ];

    public static string Emoji(string key) => All.FirstOrDefault(r => r.Key == key).Emoji ?? "👤";

    public static string Label(string key) => All.FirstOrDefault(r => r.Key == key).Label ?? "Other";
}
