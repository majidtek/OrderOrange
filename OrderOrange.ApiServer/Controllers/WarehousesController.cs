using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Where the stock actually is, and moving it between places.
///
/// The materials page stays the single truth about HOW MUCH of something the shop owns.
/// A warehouse balance is a breakdown of that number, never a competitor to it, which is
/// why a transfer only ever moves quantity between two places and leaves the total alone.
/// Anything the shop owns that has not been put in a place yet shows as unassigned rather
/// than being quietly hidden, so the two views can always be reconciled by eye.
/// </summary>
[ApiController]
[Route("api/warehouses")]
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Inventory)]
public class WarehousesController(AppDbContext db, WarehouseStore store) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<WarehouseBoardDto>> Board()
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();

        var materials = await db.StoreMaterials.Where(m => m.RestaurantId == storeId).ToListAsync();
        var places = await store.PlacesAsync(storeId);
        var stock = await store.StockAsync(storeId);

        var byPlace = stock.GroupBy(s => s.WarehouseId).ToDictionary(g => g.Key, g => g.ToList());
        var list = places.Select(p =>
        {
            var rows = byPlace.GetValueOrDefault(p.Id, []);
            var value = rows.Sum(r => r.Quantity * (materials.FirstOrDefault(m => m.Id == r.MaterialId)?.UnitCost ?? 0m));
            return new WarehouseDto(p.Id, p.Name, p.Kind, p.Notes, p.IsDefault, rows.Count, Math.Round(value, 3),
                p.Icon, p.X, p.Y, p.Links, p.IsCentral);
        }).ToList();

        // The owner's other stores are the branches; the central place may live in any of them.
        var family = await FamilyAsync(storeId);
        var central = await store.CentralAsync(family.Select(f => f.Id).ToList());
        CentralDto? centralDto = null;
        if (central is not null)
        {
            var cRows = await store.StockAsync(central.StoreId, central.Id);
            var cCost = await db.StoreMaterials.Where(m => m.RestaurantId == central.StoreId)
                .ToDictionaryAsync(m => m.Id, m => m.UnitCost);
            centralDto = new CentralDto(central.Id, central.StoreId,
                family.FirstOrDefault(f => f.Id == central.StoreId).Name ?? "",
                central.Name, central.StoreId == storeId, cRows.Count,
                Math.Round(cRows.Sum(r => r.Quantity * cCost.GetValueOrDefault(r.MaterialId)), 3));
        }
        var branches = family.Where(f => f.Id != storeId).Select(f => new BranchDto(f.Id, f.Name)).ToList();

        // What the shop owns minus what the places hold. Buying, selling and writing off
        // all change the total without naming a place, so this is where that shows up.
        var held = stock.GroupBy(s => s.MaterialId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
        var unassigned = materials
            .Select(m => new { m, Left = m.Quantity - held.GetValueOrDefault(m.Id, 0m) })
            .Where(x => x.Left > 0.0001m)
            .Select(x => new WarehouseStockDto(x.m.Id, x.m.Name, x.m.Unit, Math.Round(x.Left, 3),
                x.m.UnitCost, Math.Round(x.Left * x.m.UnitCost, 3)))
            .OrderByDescending(x => x.Value).ToList();

        return Ok(new WarehouseBoardDto(
            list, unassigned,
            Math.Round(unassigned.Sum(u => u.Value), 3),
            Math.Round(materials.Sum(m => m.Quantity * m.UnitCost), 3),
            storeId, centralDto, branches));
    }

    /// <summary>Every store the same owner has, this one included.</summary>
    private async Task<List<(int Id, string Name)>> FamilyAsync(int storeId)
    {
        var owner = await db.Restaurants.Where(r => r.Id == storeId).Select(r => r.OwnerUserId).FirstOrDefaultAsync();
        var rows = await db.Restaurants.Where(r => r.OwnerUserId == owner).Select(r => new { r.Id, r.Name }).ToListAsync();
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    /// <summary>What the central warehouse holds, readable from any branch of the owner.</summary>
    [HttpGet("central/stock")]
    public async Task<ActionResult<List<WarehouseStockDto>>> CentralStock()
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        var family = await FamilyAsync(storeId);
        var central = await store.CentralAsync(family.Select(f => f.Id).ToList());
        if (central is null) return NotFound();

        var materials = await db.StoreMaterials.Where(m => m.RestaurantId == central.StoreId).ToDictionaryAsync(m => m.Id);
        var rows = await store.StockAsync(central.StoreId, central.Id);
        return Ok(rows.Where(r => materials.ContainsKey(r.MaterialId)).Select(r =>
        {
            var m = materials[r.MaterialId];
            return new WarehouseStockDto(m.Id, m.Name, m.Unit, Math.Round(r.Quantity, 3), m.UnitCost, Math.Round(r.Quantity * m.UnitCost, 3));
        }).OrderByDescending(r => r.Value).ToList());
    }

    /// <summary>
    /// Stock leaves the central warehouse for a branch. Unlike a move inside one shop this
    /// changes ownership: the central store's material total goes down, the branch's goes
    /// up (matched by name and unit, created when the branch has never stocked it), and the
    /// quantity lands in the branch's default place. Both shops get a line in their log.
    /// The caller must be in the central store or in the branch, and both must be the owner's.
    /// </summary>
    [HttpPost("central/transfer")]
    public async Task<IActionResult> CentralTransfer(CentralTransferRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (req.Lines is not { Count: > 0 }) return BadRequest(new { message = "Nothing to move." });
        if (req.Lines.Count > 60) return BadRequest(new { message = "Too many lines for one move." });

        var family = await FamilyAsync(storeId);
        var central = await store.CentralAsync(family.Select(f => f.Id).ToList());
        if (central is null) return BadRequest(new { message = "There is no central warehouse yet." });
        var branch = family.FirstOrDefault(f => f.Id == req.ToStoreId);
        if (branch.Id == 0) return BadRequest(new { message = "That branch is not one of yours." });
        if (branch.Id == central.StoreId) return BadRequest(new { message = "Pick a branch other than the central store." });
        if (storeId != central.StoreId && storeId != branch.Id) return Forbid();
        var centralStoreName = family.FirstOrDefault(f => f.Id == central.StoreId).Name ?? "";

        var ids = req.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = await db.StoreMaterials
            .Where(m => m.RestaurantId == central.StoreId && ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        var held = await store.StockAsync(central.StoreId, central.Id);

        var lines = new List<(StoreMaterial Material, decimal Quantity)>();
        foreach (var line in req.Lines)
        {
            if (line.Quantity <= 0) continue;
            if (!materials.TryGetValue(line.MaterialId, out var material))
                return BadRequest(new { message = "Unknown material on one of the lines." });
            var available = held.FirstOrDefault(h => h.MaterialId == material.Id)?.Quantity ?? 0m;
            if (line.Quantity > available + 0.0001m)
                return BadRequest(new { message = $"{material.Name}: only {available:0.###} {material.Unit} in {central.Name}." });
            lines.Add((material, line.Quantity));
        }
        if (lines.Count == 0) return BadRequest(new { message = "Nothing to move." });

        var toPlace = (await store.PlacesAsync(branch.Id)).FirstOrDefault(p => p.IsDefault);
        var now = DateTime.Now;
        var outLines = new List<TransferLineDoc>();
        var inLines = new List<TransferLineDoc>();

        // SQL first, all lines together; the Mongo balances follow only once that holds.
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            foreach (var (material, quantity) in lines)
            {
                material.Quantity = Math.Max(0m, material.Quantity - quantity);
                material.UpdatedAt = now;

                var mine = await db.StoreMaterials.FirstOrDefaultAsync(m =>
                    m.RestaurantId == branch.Id && m.Name == material.Name && m.Unit == material.Unit);
                if (mine is null)
                {
                    mine = new StoreMaterial
                    {
                        RestaurantId = branch.Id,
                        Name = material.Name,
                        Category = material.Category,
                        Unit = material.Unit,
                        Quantity = 0m,
                        MinQuantity = 0m,
                        UnitCost = material.UnitCost,
                        Supplier = material.Supplier,
                        CreatedAt = now,
                    };
                    db.StoreMaterials.Add(mine);
                }
                mine.Quantity += quantity;
                mine.RestockedAt = now;
                mine.UpdatedAt = now;
                await db.SaveChangesAsync();

                outLines.Add(new TransferLineDoc { MaterialId = material.Id, Name = material.Name, Unit = material.Unit, Quantity = quantity, UnitCost = material.UnitCost });
                inLines.Add(new TransferLineDoc { MaterialId = mine.Id, Name = mine.Name, Unit = mine.Unit, Quantity = quantity, UnitCost = material.UnitCost });
            }
            await tx.CommitAsync();
        }

        foreach (var l in outLines) await store.MoveAsync(central.StoreId, central.Id, l.MaterialId, -l.Quantity);
        if (toPlace is not null)
            foreach (var l in inLines) await store.MoveAsync(branch.Id, toPlace.Id, l.MaterialId, l.Quantity);

        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim()[..Math.Min(req.Note.Trim().Length, 300)];
        var total = Math.Round(outLines.Sum(l => l.Quantity * l.UnitCost), 3);
        await store.AddTransferAsync(new TransferDoc
        {
            StoreId = central.StoreId, FromId = central.Id, FromName = central.Name,
            ToId = 0, ToName = branch.Name, Lines = outLines, TotalValue = total, Note = note, By = CurrentUserName,
        });
        await store.AddTransferAsync(new TransferDoc
        {
            StoreId = branch.Id, FromId = null, FromName = $"{central.Name} · {centralStoreName}",
            ToId = toPlace?.Id ?? 0, ToName = toPlace?.Name ?? "", Lines = inLines, TotalValue = total, Note = note, By = CurrentUserName,
        });
        return NoContent();
    }

    /// <summary>Everything one place holds.</summary>
    [HttpGet("{id:int}/stock")]
    public async Task<ActionResult<List<WarehouseStockDto>>> Stock(int id)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (await store.PlaceAsync(storeId, id) is null) return NotFound();

        var materials = await db.StoreMaterials.Where(m => m.RestaurantId == storeId)
            .ToDictionaryAsync(m => m.Id);
        var rows = await store.StockAsync(storeId, id);

        return Ok(rows
            .Where(r => materials.ContainsKey(r.MaterialId))
            .Select(r =>
            {
                var m = materials[r.MaterialId];
                return new WarehouseStockDto(m.Id, m.Name, m.Unit, Math.Round(r.Quantity, 3), m.UnitCost,
                    Math.Round(r.Quantity * m.UnitCost, 3));
            })
            .OrderByDescending(r => r.Value).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<WarehouseDto>> Create(SaveWarehouseRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        var name = (req.Name ?? "").Trim();
        if (name.Length is < 2 or > 60) return BadRequest(new { message = "Give the place a name." });
        if (!WarehouseKinds.IsKnown(req.Kind)) return BadRequest(new { message = "Unknown kind." });
        if ((await store.PlacesAsync(storeId)).Count >= 40)
            return BadRequest(new { message = "That is enough places for one shop." });

        // New nodes land in a loose grid so they never pile up on one spot.
        var count = (await store.PlacesAsync(storeId)).Count;
        var doc = await store.AddPlaceAsync(new WarehouseDoc
        {
            StoreId = storeId,
            Name = name,
            Kind = req.Kind,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            IsDefault = req.MakeDefault,
            Icon = WarehouseIcons.IsKnown(req.Icon) ? req.Icon : "warehouse",
            X = 14 + (count % 4) * 24,
            Y = 18 + (count / 4) * 30,
        });
        if (req.MakeCentral)
            await store.MakeCentralAsync((await FamilyAsync(storeId)).Select(f => f.Id).ToList(), storeId, doc.Id);
        return Ok(new WarehouseDto(doc.Id, doc.Name, doc.Kind, doc.Notes, doc.IsDefault, 0, 0m,
            doc.Icon, doc.X, doc.Y, doc.Links, req.MakeCentral));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveWarehouseRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (await store.PlaceAsync(storeId, id) is null) return NotFound();
        var name = (req.Name ?? "").Trim();
        if (name.Length is < 2 or > 60) return BadRequest(new { message = "Give the place a name." });
        if (!WarehouseKinds.IsKnown(req.Kind)) return BadRequest(new { message = "Unknown kind." });

        await store.UpdatePlaceAsync(storeId, id, name, req.Kind,
            string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(), req.MakeDefault,
            WarehouseIcons.IsKnown(req.Icon) ? req.Icon : "warehouse");
        if (req.MakeCentral)
            await store.MakeCentralAsync((await FamilyAsync(storeId)).Select(f => f.Id).ToList(), storeId, id);
        else
            await store.ClearCentralAsync(storeId, id);
        return NoContent();
    }

    /// <summary>A node was dragged somewhere on the map. Percentages, so every screen agrees.</summary>
    [HttpPut("{id:int}/position")]
    public async Task<IActionResult> Position(int id, MoveNodeRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (await store.PlaceAsync(storeId, id) is null) return NotFound();
        await store.PlaceNodeAsync(storeId, id, Math.Clamp(req.X, 2, 96), Math.Clamp(req.Y, 4, 94));
        return NoContent();
    }

    /// <summary>Draw a line: this place feeds that one.</summary>
    [HttpPost("{id:int}/link/{toId:int}")]
    public async Task<IActionResult> Link(int id, int toId)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (id == toId) return BadRequest(new { message = "A place cannot feed itself." });
        if (await store.PlaceAsync(storeId, id) is null || await store.PlaceAsync(storeId, toId) is null) return NotFound();
        await store.LinkAsync(storeId, id, toId);
        return NoContent();
    }

    [HttpDelete("{id:int}/link/{toId:int}")]
    public async Task<IActionResult> Unlink(int id, int toId)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        await store.UnlinkAsync(storeId, id, toId);
        return NoContent();
    }

    /// <summary>
    /// Removing a place does not destroy stock: whatever it held goes back to unassigned,
    /// because the shop still owns it. Emptying it first is therefore optional, not a trap.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (await store.PlaceAsync(storeId, id) is null) return NotFound();
        await store.RemovePlaceAsync(storeId, id);
        return NoContent();
    }

    /// <summary>
    /// Move stock. From one place to another, or from the unassigned pool into a place
    /// when <c>FromId</c> is null. The shop's totals do not change either way.
    /// </summary>
    [HttpPost("transfer")]
    public async Task<ActionResult<TransferDto>> Transfer(TransferRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (req.Lines is not { Count: > 0 }) return BadRequest(new { message = "Nothing to move." });
        if (req.Lines.Count > 60) return BadRequest(new { message = "Too many lines for one move." });
        if (req.FromId == req.ToId) return BadRequest(new { message = "Pick two different places." });

        var to = await store.PlaceAsync(storeId, req.ToId);
        if (to is null) return BadRequest(new { message = "Unknown destination." });

        WarehouseDoc? from = null;
        if (req.FromId is { } fromId)
        {
            from = await store.PlaceAsync(storeId, fromId);
            if (from is null) return BadRequest(new { message = "Unknown source." });
        }

        var ids = req.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = await db.StoreMaterials
            .Where(m => m.RestaurantId == storeId && ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

        // Check every line before moving anything: a half-applied transfer is worse than
        // a refused one, and the person can fix the number and press the button again.
        var held = await store.StockAsync(storeId);
        var lines = new List<TransferLineDoc>();
        foreach (var line in req.Lines)
        {
            if (line.Quantity <= 0) continue;
            if (!materials.TryGetValue(line.MaterialId, out var material))
                return BadRequest(new { message = "Unknown material on one of the lines." });

            decimal available;
            if (from is null)
            {
                var inPlaces = held.Where(h => h.MaterialId == material.Id).Sum(h => h.Quantity);
                available = material.Quantity - inPlaces;
            }
            else
            {
                available = held.FirstOrDefault(h => h.WarehouseId == from.Id && h.MaterialId == material.Id)?.Quantity ?? 0m;
            }

            if (line.Quantity > available + 0.0001m)
                return BadRequest(new
                {
                    message = $"{material.Name}: only {available:0.###} {material.Unit} available" +
                              (from is null ? " outside the warehouses." : $" in {from.Name}.")
                });

            lines.Add(new TransferLineDoc
            {
                MaterialId = material.Id,
                Name = material.Name,
                Unit = material.Unit,
                Quantity = line.Quantity,
                UnitCost = material.UnitCost,
            });
        }
        if (lines.Count == 0) return BadRequest(new { message = "Nothing to move." });

        foreach (var line in lines)
        {
            if (from is not null) await store.MoveAsync(storeId, from.Id, line.MaterialId, -line.Quantity);
            await store.MoveAsync(storeId, to.Id, line.MaterialId, line.Quantity);
        }

        var doc = await store.AddTransferAsync(new TransferDoc
        {
            StoreId = storeId,
            FromId = from?.Id,
            FromName = from?.Name ?? "",
            ToId = to.Id,
            ToName = to.Name,
            Lines = lines,
            TotalValue = Math.Round(lines.Sum(l => l.Quantity * l.UnitCost), 3),
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim()[..Math.Min(req.Note.Trim().Length, 300)],
            By = CurrentUserName,
        });

        return Ok(ToDto(doc));
    }

    [HttpGet("transfers")]
    public async Task<ActionResult<List<TransferDto>>> Transfers([FromQuery] int take = 60)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        var docs = await store.TransfersAsync(storeId, Math.Clamp(take, 1, 200));
        return Ok(docs.Select(ToDto).ToList());
    }

    private static TransferDto ToDto(TransferDoc d) => new(
        d.Id, d.FromId, d.FromName, d.ToId, d.ToName,
        d.Lines.Select(l => new TransferLineDto(l.MaterialId, l.Name, l.Unit, l.Quantity, l.UnitCost,
            Math.Round(l.Quantity * l.UnitCost, 3))).ToList(),
        d.TotalValue, d.Note, d.By, d.At);
}
