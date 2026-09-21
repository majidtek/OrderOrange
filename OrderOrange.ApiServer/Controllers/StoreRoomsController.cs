using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Rooms sketched on the floor plan — "Salon", "Outside", "Family room". A room is a
/// rectangle on the canvas, owned by the store, living on one floor. Tables are "in"
/// a room simply by standing inside it; deleting a room never touches the tables.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Tables, Perm.TablesView)]
public class StoreRoomsController(AppDbContext db) : ApiControllerBase
{
    private static StoreRoomDto ToDto(StoreRoom r) => new(r.Id, r.Name, r.Floor, r.X, r.Y, r.W, r.H, r.SortOrder);

    [HttpGet]
    public async Task<ActionResult<List<StoreRoomDto>>> List()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var rooms = await db.StoreRooms
            .Where(r => r.RestaurantId == CurrentRestaurantId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();
        return Ok(rooms.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<StoreRoomDto>> Create(SaveStoreRoomRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var problem = Validate(req);
        if (problem is not null) return BadRequest(new { message = problem });

        // A plan buried under rectangles helps nobody — and no real venue has 40 rooms.
        var count = await db.StoreRooms.CountAsync(r => r.RestaurantId == CurrentRestaurantId);
        if (count >= 40) return BadRequest(new { message = "That is already a lot of rooms — remove one first." });

        var room = new StoreRoom
        {
            RestaurantId = CurrentRestaurantId,
            Name = req.Name.Trim(),
            Floor = (req.Floor ?? "").Trim(),
            SortOrder = 1 + await db.StoreRooms.Where(r => r.RestaurantId == CurrentRestaurantId)
                .Select(r => (int?)r.SortOrder).MaxAsync() ?? 1,
            CreatedAt = DateTime.Now,
        };
        Apply(room, new RoomRectRequest(req.X, req.Y, req.W, req.H));
        if (await OverlapsAsync(0, room.Floor, room.X, room.Y, room.W, room.H))
            return BadRequest(new { message = "Rooms cannot overlap each other." });
        db.StoreRooms.Add(room);
        await db.SaveChangesAsync();
        return Ok(ToDto(room));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StoreRoomDto>> Rename(int id, SaveStoreRoomRequest req)
    {
        var room = await OwnedAsync(id);
        if (room is null) return NotFound();
        var problem = Validate(req);
        if (problem is not null) return BadRequest(new { message = problem });

        room.Name = req.Name.Trim();
        await db.SaveChangesAsync();
        return Ok(ToDto(room));
    }

    /// <summary>
    /// The owner dragged or resized the room on the plan. Two hard rules guard the
    /// floor: rooms never overlap each other, and a room's tables belong to it —
    /// they travel with a move and get pulled back inside after a shrink.
    /// </summary>
    [HttpPost("{id:int}/position")]
    public async Task<ActionResult<StoreRoomDto>> Move(int id, RoomRectRequest req)
    {
        var room = await OwnedAsync(id);
        if (room is null) return NotFound();

        var old = (room.X, room.Y, room.W, room.H);
        var probe = new StoreRoom();
        Apply(probe, req);
        if (await OverlapsAsync(room.Id, room.Floor, probe.X, probe.Y, probe.W, probe.H))
            return BadRequest(new { message = "Rooms cannot overlap each other." });

        var members = (await db.StoreTables
                .Where(t => t.RestaurantId == CurrentRestaurantId && t.Floor == room.Floor)
                .ToListAsync())
            .Where(t => t.X >= old.X && t.X <= old.X + old.W &&
                        t.Y >= old.Y && t.Y <= old.Y + old.H)
            .ToList();

        Apply(room, req);
        var dx = room.X - old.X;
        var dy = room.Y - old.Y;
        const double inset = 1.5; // keeps the box and its chairs off the wall, in canvas %
        foreach (var t in members)
        {
            t.X = Math.Clamp(t.X + dx, room.X + inset, Math.Max(room.X + inset, room.X + room.W - inset));
            t.Y = Math.Clamp(t.Y + dy, room.Y + inset, Math.Max(room.Y + inset, room.Y + room.H - inset));
        }
        await db.SaveChangesAsync();
        return Ok(ToDto(room));
    }

    /// <summary>True when this rectangle would touch any OTHER room on the same floor.</summary>
    private async Task<bool> OverlapsAsync(int exceptId, string floor, double x, double y, double w, double h)
    {
        const double gap = 0.4;
        var rooms = await db.StoreRooms
            .Where(r => r.RestaurantId == CurrentRestaurantId && r.Id != exceptId && r.Floor == floor)
            .ToListAsync();
        return rooms.Any(o =>
            x < o.X + o.W + gap && x + w > o.X - gap &&
            y < o.Y + o.H + gap && y + h > o.Y - gap);
    }

    /// <summary>The owner rearranged the salons — the list IS the new order.</summary>
    [HttpPost("order")]
    public async Task<IActionResult> Reorder(List<int> ids)
    {
        var rooms = await db.StoreRooms.Where(r => r.RestaurantId == CurrentRestaurantId).ToListAsync();
        var position = 1;
        foreach (var id in ids)
        {
            var room = rooms.FirstOrDefault(r => r.Id == id);
            if (room is not null) room.SortOrder = position++;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var room = await OwnedAsync(id);
        if (room is null) return NotFound();

        // The salon is a container: its tables leave WITH it — a table may never
        // be left standing without a salon. A billed open invoice pins everything.
        var tables = await db.StoreTables
            .Where(t => t.RestaurantId == CurrentRestaurantId && t.RoomId == id)
            .ToListAsync();
        var tableIds = tables.Select(t => t.Id).ToList();
        var tabs = await db.StoreTabs.Include(t => t.Lines)
            .Where(t => t.RestaurantId == CurrentRestaurantId && tableIds.Contains(t.TableId))
            .ToListAsync();
        if (tabs.Any(t => t.Lines.Count > 0))
            return BadRequest(new { message = "A table in this salon has an open invoice — close it first." });

        db.StoreTabs.RemoveRange(tabs);
        db.StoreTables.RemoveRange(tables);
        db.StoreRooms.Remove(room);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<StoreRoom?> OwnedAsync(int id) =>
        CurrentRestaurantId == 0
            ? null
            : await db.StoreRooms.FirstOrDefaultAsync(r => r.Id == id && r.RestaurantId == CurrentRestaurantId);

    /// <summary>Clamp the rectangle into the canvas, never thinner than a table.</summary>
    private static void Apply(StoreRoom room, RoomRectRequest rect)
    {
        room.W = Math.Clamp(rect.W, 8, 100);
        room.H = Math.Clamp(rect.H, 8, 100);
        room.X = Math.Clamp(rect.X, 0, 100 - room.W);
        room.Y = Math.Clamp(rect.Y, 0, 100 - room.H);
    }

    private static string? Validate(SaveStoreRoomRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "The room needs a name.";
        if (req.Name.Trim().Length > 60) return "The room name is too long.";
        if (((req.Floor ?? "").Trim()).Length > 40) return "The floor name is too long.";
        return null;
    }
}
