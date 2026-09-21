using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The shop's dining room. Tables belong to the store — one partner can never see or
/// seat another's floor — and a "customer at a table" is either an entry from the
/// store's own book (orders can then be taken for them) or a walk-in name.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Tables, Perm.TablesView)]
public class StoreTablesController(AppDbContext db) : ApiControllerBase
{
    private static readonly string[] AllowedTypes = ["indoor", "outdoor", "vip", "family", "counter", "other"];
    private static readonly string[] AllowedShapes = ["square", "round", "rect"];

    private static StoreTableDto ToDto(StoreTable t) => new(
        t.Id, t.Name, t.Type, t.Seats, t.StoreCustomerId, t.StoreCustomer?.Name, t.GuestName, t.OccupiedAt,
        t.Floor, t.X, t.Y, t.Shape, t.W, t.H, t.RoomId);

    [HttpGet]
    public async Task<ActionResult<List<StoreTableDto>>> List()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var tables = await db.StoreTables
            .Include(t => t.StoreCustomer)
            .Where(t => t.RestaurantId == CurrentRestaurantId)
            .OrderBy(t => t.Type).ThenBy(t => t.Name)
            .ToListAsync();
        return Ok(tables.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<StoreTableDto>> Create(SaveStoreTableRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var problem = Validate(req);
        if (problem is not null) return BadRequest(new { message = problem });

        var name = req.Name.Trim();
        if (await db.StoreTables.AnyAsync(t => t.RestaurantId == CurrentRestaurantId && t.Name == name))
            return BadRequest(new { message = "A table with this name already exists." });

        // Stagger newcomers across the plan so ten new tables don't land in one pile.
        var floor = CleanFloor(req.Floor);
        var count = await db.StoreTables.CountAsync(t => t.RestaurantId == CurrentRestaurantId && t.Floor == floor);
        var table = new StoreTable
        {
            RestaurantId = CurrentRestaurantId,
            Name = name,
            Type = req.Type,
            Seats = Math.Clamp(req.Seats, 1, 50),
            Shape = req.Shape,
            Floor = floor,
            RoomId = await OwnedRoomOrNullAsync(req.RoomId),
            X = 15 + count % 5 * 17.5,
            Y = 18 + count / 5 % 4 * 21,
            CreatedAt = DateTime.Now,
        };
        db.StoreTables.Add(table);
        await db.SaveChangesAsync();
        return Ok(ToDto(table));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StoreTableDto>> Update(int id, SaveStoreTableRequest req)
    {
        var table = await OwnedAsync(id);
        if (table is null) return NotFound();
        var problem = Validate(req);
        if (problem is not null) return BadRequest(new { message = problem });

        var name = req.Name.Trim();
        if (name != table.Name &&
            await db.StoreTables.AnyAsync(t => t.RestaurantId == CurrentRestaurantId && t.Name == name && t.Id != id))
            return BadRequest(new { message = "A table with this name already exists." });

        table.Name = name;
        table.Type = req.Type;
        table.Seats = Math.Clamp(req.Seats, 1, 50);
        table.Shape = req.Shape;
        table.Floor = CleanFloor(req.Floor);
        table.RoomId = await OwnedRoomOrNullAsync(req.RoomId);
        await db.SaveChangesAsync();
        return Ok(ToDto(table));
    }

    /// <summary>The owner dragged a table somewhere new on the plan (or onto another floor).</summary>
    [HttpPost("{id:int}/position")]
    public async Task<ActionResult<StoreTableDto>> Move(int id, MoveTableRequest req)
    {
        var table = await OwnedAsync(id);
        if (table is null) return NotFound();

        table.X = Math.Clamp(req.X, 0, 100);
        table.Y = Math.Clamp(req.Y, 0, 100);
        if (req.Floor is not null) table.Floor = CleanFloor(req.Floor);
        // A resize rides the same request: 0 returns the table to its default size.
        if (req.W is { } w) table.W = w == 0 ? 0 : Math.Clamp(w, 60, 480);
        if (req.H is { } h) table.H = h == 0 ? 0 : Math.Clamp(h, 52, 480);
        await db.SaveChangesAsync();
        return Ok(ToDto(table));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var table = await OwnedAsync(id);
        if (table is null) return NotFound();
        // Orders reference tables by NAME text, so history survives the row going away.
        // But a table with a BILLED open invoice cannot be deleted out from under it.
        var billedTab = await db.StoreTabs.Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.RestaurantId == CurrentRestaurantId && t.TableId == id);
        if (billedTab is { Lines.Count: > 0 })
            return BadRequest(new { message = "This table has an open invoice — close it first." });
        if (billedTab is not null) db.StoreTabs.Remove(billedTab);
        db.StoreTables.Remove(table);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Every table's printable QR: the signed code and the public URL it encodes.
    /// The owner prints these once and glues them to the tables.
    /// </summary>
    [HttpGet("qrcodes")]
    public async Task<ActionResult<List<TableQrDto>>> QrCodes([FromServices] IConfiguration config)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var clientUrl = config["ClientUrl"] ?? "https://www.orderorange.com";
        var tables = await db.StoreTables
            .Where(t => t.RestaurantId == CurrentRestaurantId)
            .OrderBy(t => t.Floor).ThenBy(t => t.Name)
            .ToListAsync();
        return Ok(tables.Select(t => new TableQrDto(
            t.Id, t.Name, t.Floor,
            TableCode.For(CurrentRestaurantId, t.Id),
            TableCode.LinkFor(CurrentRestaurantId, t.Id, clientUrl))).ToList());
    }

    /// <summary>
    /// Rename a floor by moving every table on it. The main floor ("") is the anchor
    /// of the plan and keeps its name; named floors are the owner's to rename.
    /// </summary>
    [HttpPost("floors/rename")]
    public async Task<IActionResult> RenameFloor(RenameFloorRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var from = (req.From ?? "").Trim();
        var to = (req.To ?? "").Trim();
        if (from.Length == 0) return BadRequest(new { message = "The main floor cannot be renamed." });
        if (to.Length is 0 or > 40) return BadRequest(new { message = "Give the floor a name up to 40 letters." });

        var tables = await db.StoreTables
            .Where(t => t.RestaurantId == CurrentRestaurantId && t.Floor == from)
            .ToListAsync();
        if (tables.Count == 0) return NotFound();

        foreach (var table in tables) table.Floor = to;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Seat a party: someone from the book, or a walk-in by name.</summary>
    [HttpPost("{id:int}/seat")]
    public async Task<ActionResult<StoreTableDto>> Seat(int id, SeatTableRequest req)
    {
        var table = await OwnedAsync(id);
        if (table is null) return NotFound();

        if (req.StoreCustomerId is { } customerId)
        {
            var customer = await db.StoreCustomers.FirstOrDefaultAsync(
                c => c.Id == customerId && c.RestaurantId == CurrentRestaurantId && c.IsActive);
            if (customer is null) return BadRequest(new { message = "That customer is not in your book." });
            table.StoreCustomerId = customer.Id;
            table.GuestName = null;
        }
        else if (!string.IsNullOrWhiteSpace(req.GuestName))
        {
            table.StoreCustomerId = null;
            table.GuestName = req.GuestName.Trim();
        }
        else
        {
            return BadRequest(new { message = "Pick a customer or enter a guest name." });
        }

        table.OccupiedAt = DateTime.Now;
        await db.SaveChangesAsync();
        await db.Entry(table).Reference(t => t.StoreCustomer).LoadAsync();
        return Ok(ToDto(table));
    }

    /// <summary>The party left — the table is free again.</summary>
    [HttpPost("{id:int}/clear")]
    public async Task<ActionResult<StoreTableDto>> Clear(int id)
    {
        var table = await OwnedAsync(id);
        if (table is null) return NotFound();

        // A bill with items on it must be SETTLED, not vacuumed up with the crumbs —
        // clearing past it would silently throw money away. Empty tabs go quietly.
        var openTab = await db.StoreTabs.Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.RestaurantId == CurrentRestaurantId && t.TableId == id);
        if (openTab is { Lines.Count: > 0 })
            return BadRequest(new { message = "This table has an open invoice — close it first." });

        table.StoreCustomerId = null;
        table.GuestName = null;
        table.OccupiedAt = null;
        if (openTab is not null) db.StoreTabs.Remove(openTab);
        await db.SaveChangesAsync();
        return Ok(ToDto(table));
    }

    /// <summary>A salon key is honoured only when the salon really is this store's.</summary>
    private async Task<int?> OwnedRoomOrNullAsync(int? roomId) =>
        roomId is { } id && await db.StoreRooms.AnyAsync(r => r.Id == id && r.RestaurantId == CurrentRestaurantId)
            ? id : null;

    private async Task<StoreTable?> OwnedAsync(int id) =>
        CurrentRestaurantId == 0
            ? null
            : await db.StoreTables.Include(t => t.StoreCustomer)
                .FirstOrDefaultAsync(t => t.Id == id && t.RestaurantId == CurrentRestaurantId);

    private static string? Validate(SaveStoreTableRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "The table needs a name.";
        if (!AllowedTypes.Contains(req.Type)) return "Unknown table type.";
        if (req.Seats is < 1 or > 50) return "Seats must be between 1 and 50.";
        if (!AllowedShapes.Contains(req.Shape)) return "Unknown table shape.";
        if (CleanFloor(req.Floor).Length > 40) return "The floor name is too long.";
        return null;
    }

    /// <summary>"" is the main floor; anything else is whatever the owner named it.</summary>
    private static string CleanFloor(string? floor) => (floor ?? "").Trim();
}
