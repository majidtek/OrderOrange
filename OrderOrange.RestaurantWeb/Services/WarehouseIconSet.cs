using MudBlazor;
using OrderOrange.Shared;

namespace OrderOrange.RestaurantWeb.Services;

/// <summary>
/// The pictures a place can wear on the warehouse map, drawn from the icon set the rest of
/// the app already uses so a truck here looks like a truck everywhere else.
/// </summary>
public static class WarehouseIconSet
{
    private static readonly Dictionary<string, string> Glyphs = new()
    {
        ["factory"] = Icons.Material.Filled.Factory,
        ["truck"] = Icons.Material.Filled.LocalShipping,
        ["warehouse"] = Icons.Material.Filled.Warehouse,
        ["store"] = Icons.Material.Filled.Store,
        ["restaurant"] = Icons.Material.Filled.Restaurant,
        ["kitchen"] = Icons.Material.Filled.SoupKitchen,
        ["cold"] = Icons.Material.Filled.AcUnit,
        ["freezer"] = Icons.Material.Filled.Kitchen,
        ["bar"] = Icons.Material.Filled.LocalBar,
        ["customers"] = Icons.Material.Filled.Groups,
    };

    public static string Glyph(string? icon) =>
        Glyphs.GetValueOrDefault(icon ?? "", Glyphs["warehouse"]);

    /// <summary>The set in display order — the same order as <see cref="WarehouseIcons.All"/>.</summary>
    public static IEnumerable<(string Key, string Glyph)> All =>
        WarehouseIcons.All.Select(k => (k, Glyph(k)));
}
