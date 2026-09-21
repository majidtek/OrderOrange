using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Open invoices ("tabs"). Seating a party opens one on their table; the waiter adds
/// lines while they eat; calling the bill settles the tab into a real Order — same
/// receipt, same VAT, same history as every other order — and frees the table.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
public class StoreTabsController(AppDbContext db, CatalogStore catalog, TableChatStore chat, LoyaltyStore loyalty) : ApiControllerBase
{
    // ---------- Reading ----------

    [HttpGet]
    public async Task<ActionResult<List<StoreTabDto>>> List()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var tabs = await OwnedTabs().ToListAsync();
        var lookups = await LookupsFor(tabs);
        return Ok(tabs.Select(t => ToDto(t, lookups)).ToList());
    }

    [HttpGet("table/{tableId:int}")]
    public async Task<ActionResult<StoreTabDto>> ForTable(int tableId)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var tab = await OwnedTabs().FirstOrDefaultAsync(t => t.TableId == tableId);
        if (tab is null) return NotFound();
        return Ok(ToDto(tab, await LookupsFor([tab])));
    }

    // ---------- Opening: seat + invoice in one move ----------

    [HttpPost("open")]
    public async Task<ActionResult<StoreTabDto>> Open(OpenTabRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();

        // TableId 0 = a parked bill with no table — a walk-up who will pay later.
        StoreTable? table = null;
        if (req.TableId > 0)
        {
            table = await db.StoreTables.FirstOrDefaultAsync(
                t => t.Id == req.TableId && t.RestaurantId == CurrentRestaurantId);
            if (table is null) return NotFound();
        }

        StoreCustomer? customer = null;
        if (req.StoreCustomerId is { } customerId)
        {
            customer = await db.StoreCustomers.FirstOrDefaultAsync(
                c => c.Id == customerId && c.RestaurantId == CurrentRestaurantId && c.IsActive);
            if (customer is null) return BadRequest(new { message = "That customer is not in your book." });
        }
        else if (string.IsNullOrWhiteSpace(req.GuestName))
        {
            return BadRequest(new { message = "Pick a customer or enter a guest name." });
        }

        var guestName = customer is null ? req.GuestName!.Trim() : null;

        StoreTab? tab = null;
        if (table is not null)
        {
            // Seat the party on the floor plan…
            table.StoreCustomerId = customer?.Id;
            table.GuestName = guestName;
            table.OccupiedAt = DateTime.Now;

            // …and hand over the running tab if one is already open on the table.
            tab = await OwnedTabs().FirstOrDefaultAsync(t => t.TableId == table.Id);
            if (tab is not null)
            {
                tab.StoreCustomerId = customer?.Id;
                tab.GuestName = guestName;
            }
        }

        // No table (or no tab yet): parked bills always start fresh — a shop can
        // hold several at once, one per waiting customer.
        if (tab is null)
        {
            tab = new StoreTab
            {
                RestaurantId = CurrentRestaurantId,
                TableId = table?.Id ?? 0,
                StoreCustomerId = customer?.Id,
                GuestName = guestName,
                OpenedAt = DateTime.Now,
            };
            db.StoreTabs.Add(tab);
        }
        await db.SaveChangesAsync();

        return Ok(ToDto(tab, await LookupsFor([tab])));
    }

    // ---------- Lines ----------

    [HttpPost("{id:int}/lines")]
    public async Task<ActionResult<StoreTabDto>> AddLines(int id, AddTabLinesRequest req)
    {
        var tab = await OwnedTabs().FirstOrDefaultAsync(t => t.Id == id);
        if (tab is null) return NotFound();
        var items = req.Items ?? [];
        var edits = req.Edits ?? [];
        if (items.Count == 0 && edits.Count == 0) return BadRequest(new { message = "Add at least one item." });

        // The cashier's edits to lines already on the invoice come first: a new quantity,
        // a new note, or 0 to strike the line. All in the same save as the new items.
        foreach (var e in edits)
        {
            var line = tab.Lines.FirstOrDefault(l => l.Id == e.LineId);
            if (line is null) continue;   // struck meanwhile — nothing to edit
            var quantity = Math.Clamp(e.Quantity, 0, 99);
            if (quantity == 0) { tab.Lines.Remove(line); db.StoreTabLines.Remove(line); continue; }
            line.Quantity = quantity;
            if (e.Notes is not null) line.Notes = string.IsNullOrWhiteSpace(e.Notes) ? null : e.Notes.Trim();
        }

        var restaurant = await db.Restaurants.Include(r => r.Cuisine)
            .FirstAsync(r => r.Id == CurrentRestaurantId);

        // Priced from the same catalog the menus render from, discounts included —
        // the tab must never quote a price the final bill would disagree with.
        var menuIds = items.Where(i => i.MenuItemId > 0).Select(i => i.MenuItemId).ToList();
        var menu = (await catalog.ItemsForOrderAsync(restaurant.Id, menuIds)).ToDictionary(m => m.Id);

        foreach (var line in items)
        {
            if (line.Quantity <= 0) return BadRequest(new { message = "Quantities must be at least 1." });

            if (line.MenuItemId < 0)
            {
                var (rid, slot) = MenuTemplates.Decode(line.MenuItemId);
                var template = rid == restaurant.Id
                    ? MenuTemplates.ItemAt(restaurant.StoreType, restaurant.Cuisine.Name, slot)
                    : null;
                if (template is null) return BadRequest(new { message = "An item no longer exists." });
                tab.Lines.Add(new StoreTabLine
                {
                    MenuItemId = line.MenuItemId, Name = template.Name,
                    UnitPrice = MenuTemplates.PriceFor(restaurant.Id, slot, template.Price),
                    Quantity = line.Quantity, Notes = line.Notes?.Trim(),
                    AddedAt = DateTime.Now,
                });
                continue;
            }

            if (!menu.TryGetValue(line.MenuItemId, out var dish))
                return BadRequest(new { message = "An item no longer exists." });
            if (!dish.IsAvailable)
                return BadRequest(new { message = $"'{dish.Name}' is currently unavailable." });

            var unit = dish.DiscountPercent > 0
                ? Math.Round(dish.Price * (1 - dish.DiscountPercent / 100m), 3)
                : dish.Price;
            tab.Lines.Add(new StoreTabLine
            {
                MenuItemId = dish.Id, Name = dish.Name, UnitPrice = unit,
                Quantity = line.Quantity, Notes = line.Notes?.Trim(),
                AddedAt = DateTime.Now,
            });
        }

        await db.SaveChangesAsync();
        return Ok(ToDto(tab, await LookupsFor([tab])));
    }

    /// <summary>The +/− on a ticket line: new quantity, 0 meaning "strike it".</summary>
    [HttpPut("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<StoreTabDto>> SetLineQuantity(int id, int lineId, SetTabLineQtyRequest req)
    {
        var tab = await OwnedTabs().FirstOrDefaultAsync(t => t.Id == id);
        if (tab is null) return NotFound();
        var line = tab.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return NotFound();

        var quantity = Math.Clamp(req.Quantity, 0, 99);
        if (quantity == 0)
        {
            tab.Lines.Remove(line);
            db.StoreTabLines.Remove(line);
        }
        else
        {
            line.Quantity = quantity;
            // The cashier's note for the kitchen on a line that is already on the invoice.
            if (req.Notes is not null) line.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        }
        await db.SaveChangesAsync();
        return Ok(ToDto(tab, await LookupsFor([tab])));
    }

    [HttpDelete("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<StoreTabDto>> RemoveLine(int id, int lineId)
    {
        var tab = await OwnedTabs().FirstOrDefaultAsync(t => t.Id == id);
        if (tab is null) return NotFound();

        var line = tab.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null) return NotFound();
        tab.Lines.Remove(line);
        db.StoreTabLines.Remove(line);
        await db.SaveChangesAsync();
        return Ok(ToDto(tab, await LookupsFor([tab])));
    }

    // ---------- Closing: the bill is called ----------

    [HttpPost("{id:int}/close")]
    public async Task<ActionResult<OrderDto>> Close(int id, CloseTabRequest req)
    {
        var tab = await OwnedTabs().FirstOrDefaultAsync(t => t.Id == id);
        if (tab is null) return NotFound();

        var table = await db.StoreTables.FirstOrDefaultAsync(
            t => t.Id == tab.TableId && t.RestaurantId == CurrentRestaurantId);

        // Nothing was ordered — the party left. Just free the table, no ghost invoice.
        if (tab.Lines.Count == 0)
        {
            RemoveTabAndFreeTable(tab, table);
            await db.SaveChangesAsync();
            if (table is not null) await chat.ArchiveTableAsync(CurrentRestaurantId, table.Id, null);
            return NoContent();
        }

        var restaurant = await db.Restaurants.FirstAsync(r => r.Id == CurrentRestaurantId);
        var customer = tab.StoreCustomerId is { } cid
            ? await db.StoreCustomers.FirstOrDefaultAsync(c => c.Id == cid && c.RestaurantId == CurrentRestaurantId)
            : null;
        customer ??= await WalkInBook.GetOrCreateAsync(db, CurrentRestaurantId, tab.GuestName);

        var subtotal = tab.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var tax = Math.Round(subtotal * restaurant.TaxPercent / 100m, 3);

        var order = new Order
        {
            CustomerId = customer.UserId,
            RestaurantId = restaurant.Id,
            Status = OrderStatus.Pending,
            PaymentMethod = req.PaymentMethod,
            // Nothing is driven anywhere either way — dine-in eats at the table,
            // a parked walk-up bill is handed over the counter.
            DeliveryAddress = table is not null ? $"Dine-in — {table.Name}" : "Counter",
            Subtotal = subtotal,
            DeliveryFee = 0m,
            ServiceFee = Pricing.ServiceFee,
            Discount = 0m,
            TaxPercent = restaurant.TaxPercent,
            TaxAmount = tax,
            Total = subtotal + Pricing.ServiceFee + tax,
            EstimatedMinutes = Pricing.EstimateMinutes(restaurant.AvgPrepMinutes),
            TableName = table?.Name,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            IsPaid = req.MarkPaid,
            PaymentRef = req.MarkPaid ? $"TAB-{DateTime.Now:yyyyMMddHHmmss}" : null,
            PlacedAt = DateTime.Now,
            Items = tab.Lines.Select(l => new OrderItem
            {
                MenuItemId = l.MenuItemId, Name = l.Name, UnitPrice = l.UnitPrice,
                Quantity = l.Quantity, Notes = l.Notes,
            }).ToList(),
            Events = { new OrderEvent { Status = OrderStatus.Pending, At = DateTime.Now, By = CurrentUserName } }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        order.Number = $"MF-{1000 + order.Id}";
        await loyalty.TryAwardAsync(db, order, CurrentUserName);
        customer.OrderCount++;
        customer.LastOrderAt = DateTime.Now;

        // The table already ate this — its ingredients leave the shelf with the bill.
        await StockConsumer.ApplyAsync(db, order);
        RemoveTabAndFreeTable(tab, table);
        await db.SaveChangesAsync();

        // The party is done: their chat leaves the live inbox and files itself
        // under this invoice's number in the history page.
        if (table is not null) await chat.ArchiveTableAsync(CurrentRestaurantId, table.Id, order.Number);

        var full = await db.Orders
            .Include(o => o.Customer)
            .Include(o => o.Restaurant)
            .Include(o => o.Items)
            .Include(o => o.Events)
            .FirstAsync(o => o.Id == order.Id);
        return Ok(full.ToDto());
    }

    // ---------- Plumbing ----------

    private void RemoveTabAndFreeTable(StoreTab tab, StoreTable? table)
    {
        db.StoreTabs.Remove(tab);
        if (table is null) return;
        table.StoreCustomerId = null;
        table.GuestName = null;
        table.OccupiedAt = null;
    }

    private IQueryable<StoreTab> OwnedTabs() =>
        db.StoreTabs.Include(t => t.Lines).Where(t => t.RestaurantId == CurrentRestaurantId);

    private sealed record TabLookups(
        Dictionary<int, string> TableNames, Dictionary<int, string> CustomerNames, decimal TaxPercent);

    private async Task<TabLookups> LookupsFor(List<StoreTab> tabs)
    {
        var tableIds = tabs.Select(t => t.TableId).Distinct().ToList();
        var customerIds = tabs.Where(t => t.StoreCustomerId is not null)
            .Select(t => t.StoreCustomerId!.Value).Distinct().ToList();
        return new TabLookups(
            await db.StoreTables.Where(t => tableIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name),
            await db.StoreCustomers.Where(c => customerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name),
            await db.Restaurants.Where(r => r.Id == CurrentRestaurantId)
                .Select(r => r.TaxPercent).FirstAsync());
    }

    private static StoreTabDto ToDto(StoreTab tab, TabLookups lookups)
    {
        var subtotal = tab.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var tax = Math.Round(subtotal * lookups.TaxPercent / 100m, 3);
        var serviceFee = tab.Lines.Count > 0 ? Pricing.ServiceFee : 0m;
        return new StoreTabDto(
            tab.Id,
            tab.TableId,
            tab.TableId == 0 ? "" : lookups.TableNames.GetValueOrDefault(tab.TableId, "—"),
            tab.StoreCustomerId,
            tab.StoreCustomerId is { } cid ? lookups.CustomerNames.GetValueOrDefault(cid) : null,
            tab.GuestName,
            tab.OpenedAt,
            tab.Lines.Select(l => new TabLineDto(l.Id, l.MenuItemId, l.Name, l.UnitPrice, l.Quantity, l.Notes)).ToList(),
            subtotal,
            serviceFee,
            lookups.TaxPercent,
            tax,
            subtotal + serviceFee + tax);
    }
}
