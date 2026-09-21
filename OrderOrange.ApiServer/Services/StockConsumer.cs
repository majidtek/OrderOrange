using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Burns an order's recipes off the shelf. Every completed order calls this exactly
/// once — the StockApplied flag on the order is the receipt — and whenever the
/// subtraction pushes a material under its minimum (or to zero), a stock alert is
/// born for the partner's bell. Callers save; this only stages changes.
/// </summary>
public static class StockConsumer
{
    public static async Task ApplyAsync(AppDbContext db, Order order)
    {
        if (order.StockApplied) return;
        order.StockApplied = true;   // even a recipe-less order never gets re-counted

        if (order.Items.Count == 0) return;

        var itemIds = order.Items.Select(i => i.MenuItemId).Distinct().ToList();
        var recipeLines = await db.ProductMaterials
            .Where(p => p.RestaurantId == order.RestaurantId && itemIds.Contains(p.MenuItemId))
            .ToListAsync();
        if (recipeLines.Count == 0) return;

        var materialIds = recipeLines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = await db.StoreMaterials
            .Where(m => m.RestaurantId == order.RestaurantId && materialIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id);

        // Alerts already waiting unseen — a material nags once, not every order.
        var pending = await db.StoreAlerts
            .Where(a => a.RestaurantId == order.RestaurantId && a.SeenAt == null)
            .Select(a => new { a.MaterialId, a.Type })
            .ToListAsync();

        var recipes = recipeLines.GroupBy(l => l.MenuItemId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var item in order.Items)
        {
            if (!recipes.TryGetValue(item.MenuItemId, out var lines)) continue;
            foreach (var line in lines)
            {
                if (!materials.TryGetValue(line.MaterialId, out var material)) continue;

                var before = material.Quantity;
                material.Quantity = Math.Max(0, material.Quantity - line.Amount * item.Quantity);
                material.UpdatedAt = DateTime.Now;

                // Only the CROSSING alerts — a material already below minimum since
                // yesterday should not fire again on every plate.
                var wentOut = before > 0 && material.Quantity <= 0;
                var wentLow = !wentOut && material.MinQuantity > 0
                              && before > material.MinQuantity && material.Quantity <= material.MinQuantity;

                var type = wentOut ? "out" : wentLow ? "low" : null;
                if (type is null) continue;
                if (pending.Any(p => p.MaterialId == material.Id && p.Type == type)) continue;

                db.StoreAlerts.Add(new StoreAlert
                {
                    RestaurantId = order.RestaurantId,
                    MaterialId = material.Id,
                    MaterialName = material.Name,
                    Unit = material.Unit,
                    Quantity = material.Quantity,
                    Type = type,
                    CreatedAt = DateTime.Now,
                });
                pending.Add(new { MaterialId = material.Id, Type = type });
            }
        }
    }
}
