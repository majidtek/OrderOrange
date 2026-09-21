using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// What the shop threw away, and what it cost.
///
/// Writing off stock could have been a quiet adjustment on the materials page. It is a
/// record instead, because the useful question is never "how much is left" — the shelf
/// already answers that — but "where is it going, and can we stop it". A reason is
/// therefore required, and it comes from a fixed list so the answer can be counted.
/// </summary>
[ApiController]
[Route("api/waste")]
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Inventory)]
public class WasteController(AppDbContext db, WasteStore store) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WasteBoardDto>> Board([FromQuery] int days = 30)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        days = Math.Clamp(days, 1, 365);

        var docs = await store.SinceAsync(storeId, DateTime.Now.AddDays(-days));

        var entries = docs.Select(d => new WasteEntryDto(
            d.Id, d.Kind, d.RefId, d.Name, d.Quantity, d.Unit, d.UnitCost, d.Cost,
            d.Reason, d.Notes, d.By, d.At)).ToList();

        var byReason = docs.GroupBy(d => d.Reason)
            .Select(g => new WasteReasonTotalDto(g.Key, g.Sum(d => d.Cost), g.Count()))
            .OrderByDescending(r => r.Cost).ToList();

        var topItems = docs.GroupBy(d => (d.Kind, d.RefId))
            .Select(g => new WasteItemTotalDto(g.Key.Kind, g.Key.RefId, g.First().Name,
                g.First().Unit, g.Sum(d => d.Quantity), g.Sum(d => d.Cost)))
            .OrderByDescending(i => i.Cost).Take(12).ToList();

        // Against what the shelf is worth right now: a number on its own means nothing,
        // but "this month we binned a twentieth of the store" lands.
        var shelf = await db.StoreMaterials.Where(m => m.RestaurantId == storeId)
            .SumAsync(m => (decimal?)(m.Quantity * m.UnitCost)) ?? 0m;
        var total = docs.Sum(d => d.Cost);
        var share = shelf > 0 ? Math.Round(total / shelf * 100m, 1) : 0m;

        return Ok(new WasteBoardDto(entries, byReason, topItems, total, docs.Count, share, days));
    }

    [HttpPost]
    public async Task<ActionResult<WasteEntryDto>> Record(RecordWasteRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (req.Quantity <= 0) return BadRequest(new { message = "How much was thrown away?" });
        if (!WasteReasons.IsKnown(req.Reason)) return BadRequest(new { message = "Pick a reason." });

        var doc = new WasteDoc
        {
            StoreId = storeId,
            Kind = req.Kind == "product" ? "product" : "material",
            RefId = req.RefId,
            Quantity = req.Quantity,
            Reason = req.Reason,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim()[..Math.Min(req.Notes.Trim().Length, 300)],
            UserId = CurrentUserId,
            By = CurrentUserName,
            At = DateTime.Now,
        };

        if (doc.Kind == "material")
        {
            var material = await db.StoreMaterials
                .FirstOrDefaultAsync(m => m.Id == req.RefId && m.RestaurantId == storeId);
            if (material is null) return BadRequest(new { message = "Unknown material." });

            doc.Name = material.Name;
            doc.Unit = material.Unit;
            doc.UnitCost = material.UnitCost;
            doc.Cost = Math.Round(material.UnitCost * req.Quantity, 3);

            // The shelf really loses it. Never below zero: a miscount must not invent stock.
            material.Quantity = Math.Max(0, material.Quantity - req.Quantity);
            material.UpdatedAt = DateTime.Now;
            await db.SaveChangesAsync();
        }
        else
        {
            var item = await db.MenuItems
                .FirstOrDefaultAsync(i => i.Id == req.RefId && i.RestaurantId == storeId);
            if (item is null) return BadRequest(new { message = "Unknown product." });

            doc.Name = item.Name;
            doc.Unit = "pcs";
            // A finished dish is valued at what its materials cost, not at its menu price:
            // throwing away a plate loses the ingredients, not the profit.
            doc.UnitCost = await PortionCostAsync(storeId, item.Id);
            doc.Cost = Math.Round(doc.UnitCost * req.Quantity, 3);

            await DeductRecipeAsync(storeId, item.Id, req.Quantity);
        }

        var saved = await store.AddAsync(doc);
        return Ok(new WasteEntryDto(saved.Id, saved.Kind, saved.RefId, saved.Name, saved.Quantity,
            saved.Unit, saved.UnitCost, saved.Cost, saved.Reason, saved.Notes, saved.By, saved.At));
    }

    /// <summary>Wrong entry: the record goes and the stock comes back.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Undo(int id)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();

        var doc = await store.GetAsync(storeId, id);
        if (doc is null) return NotFound();

        if (doc.Kind == "material")
        {
            var material = await db.StoreMaterials
                .FirstOrDefaultAsync(m => m.Id == doc.RefId && m.RestaurantId == storeId);
            if (material is not null)
            {
                material.Quantity += doc.Quantity;
                material.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync();
            }
        }
        else
        {
            await DeductRecipeAsync(storeId, doc.RefId, -doc.Quantity);
        }

        await store.DeleteAsync(storeId, id);
        return NoContent();
    }

    /// <summary>What one portion costs in materials, from its recipe. Zero when it has none.</summary>
    private async Task<decimal> PortionCostAsync(int storeId, int menuItemId)
    {
        var lines = await db.ProductMaterials
            .Where(r => r.MenuItemId == menuItemId && r.RestaurantId == storeId)
            .Join(db.StoreMaterials.Where(m => m.RestaurantId == storeId),
                  r => r.MaterialId, m => m.Id, (r, m) => r.Amount * m.UnitCost)
            .ToListAsync();
        return lines.Count == 0 ? 0m : Math.Round(lines.Sum(), 3);
    }

    /// <summary>
    /// Takes a dish's ingredients off the shelf, the same way selling it would. A negative
    /// count puts them back, which is what an undo needs.
    /// </summary>
    private async Task DeductRecipeAsync(int storeId, int menuItemId, decimal portions)
    {
        var recipe = await db.ProductMaterials
            .Where(r => r.MenuItemId == menuItemId && r.RestaurantId == storeId).ToListAsync();
        if (recipe.Count == 0) return;

        var ids = recipe.Select(r => r.MaterialId).ToList();
        var materials = await db.StoreMaterials
            .Where(m => m.RestaurantId == storeId && ids.Contains(m.Id)).ToListAsync();

        foreach (var line in recipe)
        {
            var material = materials.FirstOrDefault(m => m.Id == line.MaterialId);
            if (material is null) continue;
            material.Quantity = Math.Max(0, material.Quantity - line.Amount * portions);
            material.UpdatedAt = DateTime.Now;
        }
        await db.SaveChangesAsync();
    }
}
