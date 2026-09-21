using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The kitchen screen. It answers one question — what has to be cooked right now — from
/// the two places food is actually ordered: online orders, and rounds rung up at a table.
///
/// The board is assembled here rather than in the browser on purpose. A kitchen screen
/// polls every few seconds all day; sending one ready-made list beats sending the whole
/// order book plus the tab book plus the printer map and joining them fourteen times a
/// minute on a cheap tablet.
/// </summary>
[ApiController]
[Route("api/kds")]
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Orders)]
public class KdsController(AppDbContext db, KdsStore kds) : ApiControllerBase
{
    /// <summary>How long a finished ticket stays on the board so it can be recalled.</summary>
    private static readonly TimeSpan RecallWindow = TimeSpan.FromMinutes(30);

    [HttpGet("board")]
    public async Task<ActionResult<KdsBoardDto>> Board()
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();

        var since = DateTime.Now.AddHours(-12);
        var state = await kds.ForStoreAsync(storeId, DateTime.UtcNow.AddHours(-24));
        var routes = await db.PrintRoutes.Where(r => r.RestaurantId == storeId)
            .ToDictionaryAsync(r => r.MenuItemId, r => r.Station);

        var tickets = new List<KdsTicketDto>();

        // ---- online orders ----------------------------------------------------------
        // Anything still live, plus the ones marked ready in the last half hour so a cook
        // who bumped the wrong ticket can pull it back.
        var orders = await db.Orders
            .Include(o => o.Items)
            .Include(o => o.Customer)
            .Where(o => o.RestaurantId == storeId
                        && o.PlacedAt >= since
                        && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected
                        && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Accepted
                            || o.Status == OrderStatus.Preparing
                            || ((o.Status == OrderStatus.Ready || o.Status == OrderStatus.PickedUp
                                 || o.Status == OrderStatus.OnTheWay || o.Status == OrderStatus.Delivered)
                                && o.PlacedAt >= DateTime.Now.AddHours(-2))))
            .OrderBy(o => o.PlacedAt)
            .ToListAsync();

        foreach (var order in orders)
        {
            var key = $"o:{order.Id}";
            state.TryGetValue(key, out var saved);
            var done = saved?.DoneLines ?? [];

            // An order carries its own status; the kitchen's own row only remembers which
            // lines have been ticked. Ready and beyond means the pass is finished with it.
            var ticketState = order.Status switch
            {
                OrderStatus.Pending or OrderStatus.Accepted => "new",
                OrderStatus.Preparing => "cooking",
                _ => "done",
            };
            if (ticketState == "done" && order.PlacedAt < DateTime.Now - RecallWindow) continue;

            tickets.Add(new KdsTicketDto(
                key, "order", order.Number,
                order.TableName is { Length: > 0 } ? "dinein" : order.OrderType == OrderType.Pickup ? "pickup" : "delivery",
                order.TableName,
                order.Customer.FullName,
                order.PlacedAt,
                ticketState,
                order.Items.Select(i => new KdsLineDto(
                    i.Id, i.Name, i.Quantity, i.Notes,
                    routes.TryGetValue(i.MenuItemId, out var st) ? st : null,
                    done.Contains(i.Id))).ToList(),
                order.Notes,
                order.ScheduledFor,
                Wall(saved?.StartedAt),
                Wall(saved?.DoneAt)));
        }

        // ---- rounds rung up at a table ----------------------------------------------
        // A tab is one bill that grows all evening; the kitchen does not want the bill, it
        // wants each ROUND — the lines a waiter sent together, which share one stamp.
        var tabs = await db.StoreTabs
            .Include(t => t.Lines)
            .Where(t => t.RestaurantId == storeId)
            .ToListAsync();

        var tableNames = await db.StoreTables
            .Where(t => t.RestaurantId == storeId)
            .ToDictionaryAsync(t => t.Id, t => t.Name);

        foreach (var tab in tabs)
        {
            var table = tableNames.TryGetValue(tab.TableId, out var name) ? name : $"#{tab.TableId}";
            foreach (var round in tab.Lines.GroupBy(l => RoundStamp(l.AddedAt == default ? tab.OpenedAt : l.AddedAt))
                                           .OrderBy(g => g.Key))
            {
                if (round.Key < since) continue;
                var key = $"t:{tab.Id}:{round.Key.Ticks}";
                state.TryGetValue(key, out var saved);
                var ticketState = saved?.State ?? "new";
                if (ticketState == "done" && (saved?.DoneAt ?? DateTime.UtcNow) < DateTime.UtcNow - RecallWindow) continue;

                var doneLines = saved?.DoneLines ?? [];
                // A round has no order number; the minute it was sent IS its name at the pass,
                // and the table is already the chip beside it — printing "T1 T1" said nothing.
                tickets.Add(new KdsTicketDto(
                    key, "tab", round.Key.ToString("HH:mm"), "dinein", table,
                    tab.GuestName,
                    round.Key,
                    ticketState,
                    round.Select(l => new KdsLineDto(
                        l.Id, l.Name, l.Quantity, l.Notes,
                        routes.TryGetValue(l.MenuItemId, out var st) ? st : null,
                        doneLines.Contains(l.Id))).ToList(),
                    null, null,
                    Wall(saved?.StartedAt),
                    Wall(saved?.DoneAt)));
            }
        }

        var stations = routes.Values.Distinct().OrderBy(s => s).ToList();
        var target = await db.Restaurants.Where(r => r.Id == storeId)
            .Select(r => r.AvgPrepMinutes).FirstOrDefaultAsync();

        // Every time on this board is the SHOP's wall clock, sent without an offset. The
        // partner app is built without the timezone database (BlazorEnableTimeZoneSupport
        // is false), so its DateTime.Now is UTC; anything carrying an offset would be
        // converted on arrival and a ticket would read four hours in the future.
        return Ok(new KdsBoardDto(
            tickets.OrderBy(t => t.At).ToList(),
            stations,
            target > 0 ? target : 15,
            DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)));
    }

    /// <summary>Mongo keeps UTC; the board speaks the shop's wall clock, offset-free.</summary>
    private static DateTime? Wall(DateTime? utc) =>
        utc is null ? null : DateTime.SpecifyKind(utc.Value.ToLocalTime(), DateTimeKind.Unspecified);

    /// <summary>
    /// Lines rung up within the same minute are one trip to the pass. Without this every
    /// tap of "add" on the till would become its own ticket.
    /// </summary>
    private static DateTime RoundStamp(DateTime at) =>
        new(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, at.Kind);

    /// <summary>Start cooking a ticket.</summary>
    [HttpPost("ticket/start")]
    public Task<IActionResult> Start([FromBody] KdsActionRequest req) => MoveAsync(req.Key, "cooking");

    /// <summary>The ticket is plated and gone.</summary>
    [HttpPost("ticket/done")]
    public Task<IActionResult> Done([FromBody] KdsActionRequest req) => MoveAsync(req.Key, "done");

    /// <summary>Pull a bumped ticket back — the wrong one gets tapped at every service.</summary>
    [HttpPost("ticket/recall")]
    public Task<IActionResult> Recall([FromBody] KdsActionRequest req) => MoveAsync(req.Key, "cooking");

    private async Task<IActionResult> MoveAsync(string key, string state)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0 || string.IsNullOrWhiteSpace(key)) return BadRequest(new { message = "Unknown ticket." });

        if (key.StartsWith("o:") && int.TryParse(key[2..], out var orderId))
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == storeId);
            if (order is null) return NotFound();

            // The kitchen drives the real order status, because the customer's tracking
            // screen and the driver both read it. A ticket that was never accepted is
            // accepted on the way past — a cook starting the food IS the acceptance.
            var moved = state switch
            {
                "cooking" when order.Status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Ready
                    => OrderStatus.Preparing,
                "done" when order.Status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Preparing
                    => OrderStatus.Ready,
                _ => order.Status,
            };
            if (moved != order.Status)
            {
                order.Status = moved;
                db.OrderEvents.Add(new OrderEvent { OrderId = order.Id, Status = moved, At = DateTime.Now, By = "kitchen" });
                await db.SaveChangesAsync();
            }
        }

        await kds.SetStateAsync(storeId, key, state);
        return Ok();
    }

    /// <summary>Tick one line off, or put it back.</summary>
    [HttpPost("ticket/line")]
    public async Task<ActionResult<List<int>>> Line([FromBody] KdsLineActionRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0 || string.IsNullOrWhiteSpace(req.Key)) return BadRequest();
        return Ok(await kds.ToggleLineAsync(storeId, req.Key, req.LineId));
    }
}
