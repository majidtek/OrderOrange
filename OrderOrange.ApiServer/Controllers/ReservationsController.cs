using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Table reservations born from a table's QR code. The scan opens a PUBLIC page — no
/// account, no login — so the public half here trusts only the HMAC inside the code,
/// and the store half is locked to the owner like every other partner endpoint.
/// </summary>
public class ReservationsController(AppDbContext db, CatalogStore catalog, PushSender push, IConfiguration config) : ApiControllerBase
{
    private static readonly string[] Statuses = ["pending", "confirmed", "cancelled", "seated"];

    private static ReservationDto ToDto(TableReservation r) => new(
        r.Id, r.TableId, r.TableName, r.Name, r.Phone, r.Guests, r.At, r.Note, r.Status, r.CreatedAt,
        r.DurationMinutes, r.TableIds, r.Price);

    /// <summary>
    /// What the house charges for this booking: its flat price for every table held,
    /// plus its per-minute price for the whole stay. A store that set neither charges
    /// nothing, and nothing about a price is ever shown.
    /// </summary>
    private static decimal PriceOf(Restaurant store, int tables, int minutes) =>
        Math.Round(store.ReservePricePerTable * Math.Max(tables, 1)
                   + store.ReservePricePerMinute * minutes, 3);

    /// <summary>Half an hour to six — long enough for a dinner, short of a booking that never ends.</summary>
    private static int SaneDuration(int minutes) => Math.Clamp(minutes <= 0 ? 90 : minutes, 30, 360);

    /// <summary>Every table this booking holds: the row's list when it took several, else the one.</summary>
    private static List<int> TablesOf(TableReservation r) =>
        r.TableIds.Length > 0
            ? r.TableIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList()
            : r.TableId > 0 ? [r.TableId] : [];

    /// <summary>
    /// Which of this store's tables are already spoken for between <paramref name="at"/>
    /// and the end of that stay. A booking holds its tables from its own hour until its
    /// own hour plus the guest's estimate; two parties may never overlap on one table.
    /// Cancelled bookings hold nothing. <paramref name="ignoreId"/> skips one row — the
    /// booking being edited must not clash with itself.
    /// </summary>
    private async Task<HashSet<int>> TakenTablesAsync(int storeId, DateTime at, int minutes, int? ignoreId = null)
    {
        var end = at.AddMinutes(SaneDuration(minutes));
        // A day either side is far wider than the six-hour cap on any single stay, so
        // this window cannot miss an overlap while still keeping the scan small.
        var near = await db.TableReservations
            .Where(r => r.RestaurantId == storeId
                && (r.Status == "pending" || r.Status == "confirmed" || r.Status == "seated")
                && r.At < end && r.At > at.AddHours(-24))
            .ToListAsync();
        return near
            .Where(r => r.Id != ignoreId && r.At.AddMinutes(r.DurationMinutes <= 0 ? 90 : r.DurationMinutes) > at)
            .SelectMany(TablesOf)
            .ToHashSet();
    }

    /// <summary>
    /// The tables already booked in a given window, so the booking page can grey them
    /// out BEFORE the guest fills in a form only to be told no. Public for the same
    /// reason the floor is: the store opted into online booking.
    /// </summary>
    [HttpGet("~/api/restaurants/{storeId:int}/floor/busy")]
    [AllowAnonymous]
    public async Task<ActionResult<List<int>>> Busy(int storeId, DateTime at, int minutes = 90)
    {
        var store = await db.Restaurants.FirstOrDefaultAsync(r => r.Id == storeId && r.IsApproved);
        if (store is null || !store.OnlineReservations) return NotFound();
        return Ok((await TakenTablesAsync(storeId, at, minutes)).ToList());
    }

    // ---------- The public side: whoever scanned the QR ----------

    /// <summary>What the reservation page shows: which store, which table.</summary>
    /// <summary>
    /// The printed table cards say ?id=shop&amp;t=N — a table NUMBER, not a table id.
    /// This turns that number into the signed reserve code, creating the table on the
    /// first scan so a store never has to pre-draw its floor plan to hand out cards.
    /// A table already on the plan whose name carries the number ("T2", "Table 2",
    /// "Terrace 2") is reused, so the card and the floor agree.
    /// </summary>
    [HttpGet("~/api/reserve/resolve")]
    [AllowAnonymous]
    public async Task<ActionResult<TableCodeDto>> Resolve(int store, int table)
    {
        if (table is <= 0 or > 99) return NotFound();
        if (await db.Restaurants.CountAsync(r => r.Id == store && r.IsApproved) == 0) return NotFound();

        var tables = await db.StoreTables.Where(t => t.RestaurantId == store).ToListAsync();
        var hit = tables.FirstOrDefault(t => NumberOf(t.Name) == table);
        if (hit is null)
        {
            if (tables.Count >= 99) return NotFound();   // a scan cannot flood the floor plan
            hit = new StoreTable { RestaurantId = store, Name = $"Table {table}", CreatedAt = DateTime.Now };
            db.StoreTables.Add(hit);
            await db.SaveChangesAsync();
        }
        return Ok(new TableCodeDto(TableCode.For(store, hit.Id)));
    }

    /// <summary>"T1", "Table 5", "Terrace 2" → the number the floor calls it by.</summary>
    private static int NumberOf(string name)
    {
        var digits = new string(name.Where(char.IsDigit).ToArray());
        return digits.Length is > 0 and <= 2 && int.TryParse(digits, out var n) ? n : 0;
    }

    [HttpGet("~/api/reserve/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<TableReserveInfoDto>> Info(string code)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();

        var table = await db.StoreTables.FirstOrDefaultAsync(t => t.Id == tableId && t.RestaurantId == storeId);
        var store = await db.Restaurants.FirstOrDefaultAsync(r => r.Id == storeId);
        if (table is null || store is null) return NotFound();

        // The menu rides along — the guest at the table wants to see the food, and the
        // QR itself proves they are standing in the store, approved-for-browse or not.
        var menu = (await catalog.PublicMenuAsync(storeId, includeInStoreOnly: true))
            .Select(c => MediaLinks.Lighten(config, c)).ToList();
        return Ok(new TableReserveInfoDto(store.Name, store.LogoEmoji, store.Area, table.Name, table.Seats, menu,
            RestaurantsController.NamesFromJson(store.NameLocalized),
            MediaLinks.Logo(config, store.Id, store.LogoData), store.DishStockPhotos,
            store.Phone, store.Lat, store.Lng, store.Id));
    }

    [HttpPost("~/api/reserve/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<ReservationDto>> Reserve(string code, PublicReserveRequest req)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();

        var table = await db.StoreTables.FirstOrDefaultAsync(t => t.Id == tableId && t.RestaurantId == storeId);
        if (table is null) return NotFound();

        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { code = "rsv.e.name", message = "Please tell us your name." });
        if (string.IsNullOrWhiteSpace(req.Phone)) return BadRequest(new { code = "rsv.e.phone", message = "A phone number is needed so the restaurant can reach you." });
        if (req.At < DateTime.Now.AddMinutes(-10)) return BadRequest(new { code = "rsv.e.past", message = "That time has already passed." });
        if (req.At > DateTime.Now.AddDays(365)) return BadRequest(new { code = "rsv.e.tooFar", message = "Reservations open at most a year ahead." });

        // A gentle brake, not a bouncer: one phone cannot flood one store's book.
        var openCount = await db.TableReservations.CountAsync(r =>
            r.RestaurantId == storeId && r.Phone == req.Phone.Trim() &&
            (r.Status == "pending" || r.Status == "confirmed") && r.At > DateTime.Now);
        if (openCount >= 3) return BadRequest(new { code = "rsv.e.tooMany", message = "This phone already holds 3 upcoming reservations here." });

        // The same one-party-per-table rule the online page follows.
        if ((await TakenTablesAsync(storeId, req.At, req.DurationMinutes)).Contains(table.Id))
            return BadRequest(new { code = "rsv.e.taken", args = new[] { table.Name }, message = $"{table.Name} is already booked at that time." });

        var house = await db.Restaurants.FirstAsync(r => r.Id == storeId);
        var reservation = new TableReservation
        {
            RestaurantId = storeId,
            TableId = table.Id,
            TableName = table.Name,
            Name = req.Name.Trim(),
            Phone = req.Phone.Trim(),
            Guests = Math.Clamp(req.Guests, 1, 50),
            At = req.At,
            DurationMinutes = SaneDuration(req.DurationMinutes),
            Price = PriceOf(house, 1, SaneDuration(req.DurationMinutes)),
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            Status = "pending",
            CreatedAt = DateTime.Now,
        };
        db.TableReservations.Add(reservation);
        await db.SaveChangesAsync();
        push.SendToStore(storeId, $"📅 {reservation.Name} · {reservation.Guests} guests",
            $"{reservation.TableName} · {reservation.At:dd MMM HH:mm}–{reservation.At.AddMinutes(reservation.DurationMinutes):HH:mm}",
            "/reservations");
        return Ok(ToDto(reservation));
    }

    // ---------- Booked online, from the store's own page ----------

    /// <summary>
    /// The floor, for the booking page: salons in the owner's order, each with its
    /// tables; open-floor tables ride under a nameless salon (id 0) first. Public,
    /// but only for stores that opted into online booking — the floor plan of a shop
    /// that never asked to be booked is nobody's business.
    /// </summary>
    [HttpGet("~/api/restaurants/{storeId:int}/floor")]
    [AllowAnonymous]
    public async Task<ActionResult<List<PublicSalonDto>>> Floor(int storeId)
    {
        var store = await db.Restaurants.FirstOrDefaultAsync(r => r.Id == storeId && r.IsApproved);
        if (store is null || !store.OnlineReservations) return NotFound();

        var rooms = await db.StoreRooms.Where(r => r.RestaurantId == storeId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync();
        var tables = await db.StoreTables.Where(t => t.RestaurantId == storeId)
            .OrderBy(t => t.Name).ToListAsync();

        PublicTableDto ToTable(Models.StoreTable t) => new(t.Id, t.Name, t.Type, t.Seats, t.Shape);
        var result = new List<PublicSalonDto>();
        var open = tables.Where(t => t.RoomId is null).Select(ToTable).ToList();
        if (open.Count > 0) result.Add(new PublicSalonDto(0, "", open));
        result.AddRange(rooms
            .Select(r => new PublicSalonDto(r.Id, r.Name,
                tables.Where(t => t.RoomId == r.Id).Select(ToTable).ToList()))
            .Where(s => s.Tables.Count > 0));
        return Ok(result);
    }

    /// <summary>
    /// A reservation made from the store's page on the open platform — no QR, no
    /// particular table: the house assigns one when the party walks in. Signed-in
    /// customers are remembered on the row so their phone rings when the store
    /// answers; guests book with just a name and phone, like the QR path.
    /// </summary>
    [HttpPost("~/api/restaurants/{storeId:int}/reserve")]
    [AllowAnonymous]
    public async Task<ActionResult<ReservationDto>> ReserveOnline(int storeId, PublicReserveRequest req)
    {
        // Open to everyone: a name and phone are enough, like the QR path. A signed-in
        // customer is still remembered on the row so their phone rings on confirm.

        // Only stores whose owner switched online booking ON in Settings take these.
        var store = await db.Restaurants.FirstOrDefaultAsync(r => r.Id == storeId && r.IsApproved);
        if (store is null) return NotFound();
        if (!store.OnlineReservations)
            return BadRequest(new { code = "rsv.e.closed", message = "This store is not taking online reservations." });

        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { code = "rsv.e.name", message = "Please tell us your name." });
        if (string.IsNullOrWhiteSpace(req.Phone)) return BadRequest(new { code = "rsv.e.phone", message = "A phone number is needed so the restaurant can reach you." });
        if (req.At < DateTime.Now.AddMinutes(-10)) return BadRequest(new { code = "rsv.e.past", message = "That time has already passed." });
        if (req.At > DateTime.Now.AddDays(365)) return BadRequest(new { code = "rsv.e.tooFar", message = "Reservations open at most a year ahead." });

        var openCount = await db.TableReservations.CountAsync(r =>
            r.RestaurantId == storeId && r.Phone == req.Phone.Trim() &&
            (r.Status == "pending" || r.Status == "confirmed") && r.At > DateTime.Now);
        if (openCount >= 3) return BadRequest(new { code = "rsv.e.tooMany", message = "This phone already holds 3 upcoming reservations here." });

        // The guest picks their table — or several, when the party is too big for one.
        // Required whenever the store has a floor to pick from; only a store with no
        // tables at all books without one.
        var wanted = (req.TableIds ?? [])
            .Concat(req.TableId > 0 ? [req.TableId] : Array.Empty<int>())
            .Where(id => id > 0).Distinct().ToList();
        if (wanted.Count > 10) return BadRequest(new { code = "rsv.e.tooManyTables", message = "That is more tables than one booking can hold." });

        var picked = wanted.Count > 0
            ? await db.StoreTables.Where(t => t.RestaurantId == storeId && wanted.Contains(t.Id))
                .OrderBy(t => t.Name).ToListAsync()
            : [];
        if (wanted.Count != picked.Count) return BadRequest(new { code = "rsv.e.gone", message = "That table no longer exists." });
        if (picked.Count == 0 && await db.StoreTables.AnyAsync(t => t.RestaurantId == storeId))
            return BadRequest(new { code = "rsv.e.needTable", message = "Please choose a table." });

        // The party has to fit: a table booked for more people than it seats is a
        // promise the floor cannot keep. Several tables seat their sum.
        var guests = Math.Clamp(req.Guests, 1, 50);
        var seats = picked.Sum(t => t.Seats);
        if (picked.Count > 0 && seats > 0 && guests > seats)
            return BadRequest(new
            {
                code = "rsv.e.seats",
                args = new[] { seats.ToString(), guests.ToString() },
                message = $"Those tables seat {seats}, and you are {guests}.",
            });

        // One table, one party, one hour: whoever asked first keeps it.
        var taken = await TakenTablesAsync(storeId, req.At, req.DurationMinutes);
        var clash = picked.Where(t => taken.Contains(t.Id)).Select(t => t.Name).ToList();
        if (clash.Count > 0)
            return BadRequest(new { code = "rsv.e.taken", args = new[] { string.Join(", ", clash) }, message = $"Already booked at that time: {string.Join(", ", clash)}." });

        var reservation = new TableReservation
        {
            RestaurantId = storeId,
            TableId = picked.Count > 0 ? picked[0].Id : 0,
            TableName = string.Join(" + ", picked.Select(t => t.Name)),
            TableIds = picked.Count > 1 ? string.Join(",", picked.Select(t => t.Id)) : "",
            CustomerId = CurrentUserId > 0 ? CurrentUserId : null,
            Name = req.Name.Trim(),
            Phone = req.Phone.Trim(),
            Guests = guests,
            At = req.At,
            DurationMinutes = SaneDuration(req.DurationMinutes),
            Price = PriceOf(store, picked.Count, SaneDuration(req.DurationMinutes)),
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            Status = "pending",
            CreatedAt = DateTime.Now,
        };
        db.TableReservations.Add(reservation);
        await db.SaveChangesAsync();
        var where = reservation.TableName is { Length: > 0 } tn ? $"{tn} · " : "";
        push.SendToStore(storeId, $"📅 {reservation.Name} · {reservation.Guests} guests",
            $"{where}{reservation.At:dd MMM HH:mm}–{reservation.At.AddMinutes(reservation.DurationMinutes):HH:mm}"
            + (reservation.Note is { } n ? $" · {n}" : ""), "/reservations");
        return Ok(ToDto(reservation));
    }

    /// <summary>The signed-in customer's own bookings, newest date first, store attached.</summary>
    [HttpGet("~/api/reservations/mine")]
    [Authorize]
    public async Task<ActionResult<List<MyReservationDto>>> Mine()
    {
        if (CurrentUserId <= 0) return Forbid();
        var list = await db.TableReservations
            .Where(r => r.CustomerId == CurrentUserId)
            .Join(db.Restaurants, r => r.RestaurantId, s => s.Id, (r, s) => new { r, s })
            .OrderByDescending(x => x.r.At).Take(50).ToListAsync();
        return Ok(list.Select(x => new MyReservationDto(
            x.r.Id, x.s.Id, x.s.Name, x.s.LogoEmoji,
            RestaurantsController.NamesFromJson(x.s.NameLocalized), x.r.TableName,
            x.r.Guests, x.r.At, x.r.Note, x.r.Status, x.r.DurationMinutes, x.r.Price)).ToList());
    }

    /// <summary>A customer withdraws their own upcoming booking; the store is told.</summary>
    [HttpPost("~/api/reservations/{id:int}/cancel-mine")]
    [Authorize]
    public async Task<ActionResult<ReservationDto>> CancelMine(int id)
    {
        var reservation = await db.TableReservations.FirstOrDefaultAsync(
            r => r.Id == id && r.CustomerId == CurrentUserId);
        if (reservation is null) return NotFound();
        if (reservation.Status is not ("pending" or "confirmed"))
            return BadRequest(new { code = "rsv.e.noCancel", message = "This reservation can no longer be cancelled." });

        reservation.Status = "cancelled";
        await db.SaveChangesAsync();
        push.SendToStore(reservation.RestaurantId, $"📅❌ {reservation.Name}",
            $"{reservation.At:dd MMM HH:mm} — cancelled by the guest", "/reservations");
        return Ok(ToDto(reservation));
    }

    /// <summary>
    /// Self-ordering from the table's QR: the guest picks dishes on the public page
    /// and they land as lines on this table's OPEN INVOICE — the same tab the staff
    /// POS works — with the table seated if it wasn't already. No account anywhere.
    /// </summary>
    [HttpPost("~/api/reserve/{code}/order")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicOrderResultDto>> Order(string code, PublicTableOrderRequest req)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();
        var table = await db.StoreTables.FirstOrDefaultAsync(t => t.Id == tableId && t.RestaurantId == storeId);
        if (table is null) return NotFound();
        if (req.Items is not { Count: > 0 } || req.Items.Count > 40)
            return BadRequest(new { message = "Add at least one item." });

        var restaurant = await db.Restaurants.Include(r => r.Cuisine).FirstAsync(r => r.Id == storeId);

        // Priced from the catalog exactly like the staff till — the guest's phone must
        // never quote a price the bill would disagree with.
        var menuIds = req.Items.Where(i => i.MenuItemId > 0).Select(i => i.MenuItemId).ToList();
        var menu = (await catalog.ItemsForOrderAsync(storeId, menuIds)).ToDictionary(m => m.Id);

        var lines = new List<StoreTabLine>();
        var stamp = DateTime.Now;   // one stamp per round — the bell shows the round, not each line
        foreach (var item in req.Items)
        {
            if (item.Quantity is <= 0 or > 20) return BadRequest(new { message = "Quantities must be 1–20." });

            if (item.MenuItemId < 0)
            {
                var (rid, slot) = MenuTemplates.Decode(item.MenuItemId);
                var template = rid == storeId
                    ? MenuTemplates.ItemAt(restaurant.StoreType, restaurant.Cuisine.Name, slot)
                    : null;
                if (template is null) return BadRequest(new { message = "An item no longer exists." });
                lines.Add(new StoreTabLine
                {
                    MenuItemId = item.MenuItemId, Name = template.Name,
                    UnitPrice = MenuTemplates.PriceFor(storeId, slot, template.Price),
                    Quantity = item.Quantity, Notes = item.Notes?.Trim(),
                    AddedAt = stamp, Source = "qr",
                });
                continue;
            }

            if (!menu.TryGetValue(item.MenuItemId, out var dish) || !dish.IsAvailable)
                return BadRequest(new { message = "An item is no longer available." });
            var unit = dish.DiscountPercent > 0
                ? Math.Round(dish.Price * (1 - dish.DiscountPercent / 100m), 3)
                : dish.Price;
            lines.Add(new StoreTabLine
            {
                MenuItemId = dish.Id, Name = dish.Name, UnitPrice = unit,
                Quantity = item.Quantity, Notes = item.Notes?.Trim(),
                AddedAt = stamp, Source = "qr",
            });
        }

        // Onto the table's running invoice — opened (and the table seated) on first use.
        var tab = await db.StoreTabs.Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.RestaurantId == storeId && t.TableId == table.Id);
        if (tab is null)
        {
            var guest = string.IsNullOrWhiteSpace(req.GuestName) ? $"QR — {table.Name}" : req.GuestName.Trim();
            tab = new StoreTab
            {
                RestaurantId = storeId,
                TableId = table.Id,
                GuestName = guest,
                OpenedAt = DateTime.Now,
            };
            db.StoreTabs.Add(tab);
            table.GuestName = guest;
            table.StoreCustomerId = null;
            table.OccupiedAt ??= DateTime.Now;
        }
        foreach (var line in lines) tab.Lines.Add(line);
        await db.SaveChangesAsync();

        var subtotal = tab.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var tax = Math.Round(subtotal * restaurant.TaxPercent / 100m, 3);
        var total = subtotal + Pricing.ServiceFee + tax;
        return Ok(new PublicOrderResultDto(tab.Id, table.Name, tab.Lines.Count, subtotal, total,
            InvoicePdfUrlOf(code, tab.Id)));
    }

    // ---------- The guest's own copy of the running invoice ----------

    private string ClientBase => (config["ClientUrl"] ?? "https://www.orderorange.com").TrimEnd('/');

    /// <summary>
    /// Where the guest's phone reaches THIS API from the outside. Production names it in
    /// Media:PublicBase; staging leaves that key out (its media travels inline), so the
    /// PDF link used to come out relative — pointing at the client site, a 404. Now an
    /// explicit Api:PublicBase wins, then the media base, then the request's own host.
    /// </summary>
    private string PublicApiBase =>
        (config["Api:PublicBase"] ?? config["Media:PublicBase"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');

    private string InvoicePdfUrlOf(string code, int tabId) =>
        $"{PublicApiBase}/api/reserve/{code}/invoice/{tabId}/pdf";

    /// <summary>The tab as the GUEST may see it: only the table the code in their hand
    /// unlocks, and only amounts and lines — no staff, no customer file.</summary>
    [HttpGet("~/api/reserve/{code}/invoice/{tabId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicTabInvoiceDto>> GuestInvoice(string code, int tabId)
    {
        var inv = await BuildGuestInvoiceAsync(code, tabId);
        return inv is null ? NotFound() : Ok(inv);
    }

    private async Task<PublicTabInvoiceDto?> BuildGuestInvoiceAsync(string code, int tabId)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return null;
        var tab = await db.StoreTabs.Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == tabId && t.RestaurantId == storeId && t.TableId == tableId);
        if (tab is null) return null;
        var store = await db.Restaurants.FirstAsync(r => r.Id == storeId);
        var table = await db.StoreTables.FirstAsync(t => t.Id == tableId);

        var subtotal = tab.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var tax = Math.Round(subtotal * store.TaxPercent / 100m, 3);

        // The guest's copy wears the store's own receipt DESIGN — the same slip
        // the till prints, not a second invoice look.
        var d = await db.ReceiptDesigns.FirstOrDefaultAsync(r => r.RestaurantId == storeId);
        var design = d is null
            ? ReceiptDesignDto.Default
            : new ReceiptDesignDto(d.LogoData, d.HeaderMessage, d.FooterMessage, d.Promo,
                d.Font, d.FontSize, d.PaperWidth, d.ShowBarcode, d.ShowVat, d.ShowCourier,
                d.Separator, d.TotalStyle, d.Spacing, d.LabelStyle, d.ShowQr, d.ShowAddress,
                d.HeaderStyle, d.ItemStyle, d.Frame, d.Ink, d.LogoSize, d.Stamp, d.Copies, d.QrLink,
                d.QrMode, d.QrCaption, d.QrSize);

        // The slip's QR leads back to this very table's menu — scan the paper, order again.
        string? qr = null;
        try
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using var qrData = generator.CreateQrCode($"{ClientBase}/reserve/{code}", QRCoder.QRCodeGenerator.ECCLevel.M);
            qr = "data:image/png;base64," + Convert.ToBase64String(new QRCoder.PngByteQRCode(qrData).GetGraphic(4));
        }
        catch { /* a slip without a QR is still a slip */ }

        return new PublicTabInvoiceDto(
            store.Name, RestaurantsController.NamesFromJson(store.NameLocalized),
            MediaLinks.Logo(config, store.Id, store.LogoData),
            table.Name, tab.OpenedAt,
            tab.Lines.Select(l => new PublicTabLineDto(l.Name, l.Quantity, l.UnitPrice)).ToList(),
            subtotal, store.TaxPercent, tax, subtotal + Pricing.ServiceFee + tax,
            store.Area, store.Street, store.Phone, store.CrNumber, store.VatNumber, design, qr);
    }

    /// <summary>
    /// The same invoice as a PDF — drawn directly (TabInvoicePdf), fresh each time
    /// because a running tab grows with every round. `?format=png` returns the slip
    /// as a picture instead. The headless-Chrome print of the public page stays only
    /// as the fallback should the drawing ever fail.
    /// </summary>
    [HttpGet("~/api/reserve/{code}/invoice/{tabId:int}/pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> GuestInvoicePdf(string code, int tabId, [FromQuery] string? format = null,
        [FromServices] ILogger<ReservationsController> log = null!)
    {
        var inv = await BuildGuestInvoiceAsync(code, tabId);
        if (inv is null) return NotFound();

        // The logo travels as a media URL for the page; the PDF wants the bytes.
        if (inv.LogoData is { Length: > 0 } link && !link.StartsWith("data:", StringComparison.Ordinal)
            && TableCode.TryRead(code, out var logoStoreId, out _))
        {
            var raw = await db.Restaurants.Where(r => r.Id == logoStoreId).Select(r => r.LogoData).FirstOrDefaultAsync();
            inv = inv with { LogoData = raw };
        }

        Response.Headers.CacheControl = "no-store";
        try
        {
            if (string.Equals(format, "png", StringComparison.OrdinalIgnoreCase))
                return File(TabInvoicePdf.RenderPng(inv, tabId), "image/png", $"invoice-{tabId}.png");
            return File(TabInvoicePdf.Render(inv, tabId), "application/pdf", $"invoice-{tabId}.pdf");
        }
        catch (Exception ex)
        {
            log?.LogError(ex, "Guest invoice PDF drawing failed for tab {TabId}; falling back to Chrome", tabId);
        }

        var pdf = await RenderPdfAsync($"{ClientBase}/tab-invoice/{code}/{tabId}");
        if (pdf is null) return StatusCode(503, new { message = "The PDF could not be produced right now." });
        return File(pdf, "application/pdf", $"invoice-{tabId}.pdf");
    }

    /// <summary>Same recipe as the contracts' printer: Chrome prints the public page.</summary>
    private async Task<byte[]?> RenderPdfAsync(string url)
    {
        var chrome = config["Chrome:Path"] ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        if (!System.IO.File.Exists(chrome)) return null;
        var output = Path.Combine(Path.GetTempPath(), $"oo-tabinv-{Guid.NewGuid():N}.pdf");
        var profile = Path.Combine(Path.GetTempPath(), $"oo-tabp-{Guid.NewGuid():N}");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = chrome,
                Arguments = string.Join(' ',
                    "--headless=new", $"\"--user-data-dir={profile}\"", "--no-first-run",
                    "--disable-gpu", "--timeout=30000", "--no-pdf-header-footer",
                    $"\"--print-to-pdf={output}\"", $"\"{url}\""),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(40)).Token);
            return System.IO.File.Exists(output) ? await System.IO.File.ReadAllBytesAsync(output) : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            try { System.IO.File.Delete(output); } catch (IOException) { }
            try { Directory.Delete(profile, true); } catch (IOException) { }
        }
    }

    // ---------- Chat: the guest's phone talking to the store ----------

    [HttpGet("~/api/reserve/{code}/chat")]
    [AllowAnonymous]
    public async Task<ActionResult<List<TableChatMessageDto>>> Chat(
        string code, [FromServices] TableChatStore chat, int afterId = 0)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();
        var messages = await chat.MessagesAsync(storeId, tableId, afterId);
        return Ok(messages.Select(ToChatDto).ToList());
    }

    [HttpPost("~/api/reserve/{code}/chat")]
    [AllowAnonymous]
    public async Task<ActionResult<TableChatMessageDto>> Send(
        string code, SendTableChatRequest req, [FromServices] TableChatStore chat)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();
        if (await db.StoreTables.CountAsync(t => t.Id == tableId && t.RestaurantId == storeId) == 0) return NotFound();

        var problem = ValidateMessage(req);
        if (problem is not null) return BadRequest(new { message = problem });

        var message = await chat.AddAsync(storeId, tableId, "guest", (req.Text ?? "").Trim(), req.Audio, req.Image, req.ReplyToId, req.File, SafeFileName(req.FileName));
        return Ok(ToChatDto(message));
    }

    /// <summary>One poll: new words, changed old words, and how far the store has read.</summary>
    [HttpGet("~/api/reserve/{code}/chat/sync")]
    [AllowAnonymous]
    public async Task<ActionResult<TableChatSyncDto>> ChatSync(
        string code, [FromServices] TableChatStore chat, int afterId = 0, long stamp = 0)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();
        var now = DateTime.Now;
        var fresh = await chat.MessagesAsync(storeId, tableId, afterId);
        var changed = stamp > 0
            ? await chat.ChangedAsync(storeId, tableId, new DateTime(stamp), afterId)
            : [];
        var (readId, readAt) = await chat.ReadOfAsync(storeId, tableId, "store");
        return Ok(new TableChatSyncDto(
            fresh.Select(ToChatDto).ToList(), changed.Select(ToChatDto).ToList(), now.Ticks, readId, readAt));
    }

    [HttpPut("~/api/reserve/{code}/chat/{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<TableChatMessageDto>> EditChat(
        string code, int id, EditTableChatRequest req, [FromServices] TableChatStore chat)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Text) || req.Text.Trim().Length > 500)
            return BadRequest(new { message = "Keep messages under 500 characters." });
        var message = await chat.EditAsync(storeId, tableId, id, "guest", req.Text.Trim());
        return message is null ? NotFound() : Ok(ToChatDto(message));
    }

    [HttpDelete("~/api/reserve/{code}/chat/{id:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<TableChatMessageDto>> DeleteChat(
        string code, int id, [FromServices] TableChatStore chat)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();
        var message = await chat.DeleteAsync(storeId, tableId, id, "guest");
        return message is null ? NotFound() : Ok(ToChatDto(message));
    }

    [HttpPost("~/api/reserve/{code}/chat/read")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkChatRead(
        string code, MarkChatReadRequest req, [FromServices] TableChatStore chat)
    {
        if (!TableCode.TryRead(code, out var storeId, out var tableId)) return NotFound();
        await chat.MarkReadAsync(storeId, tableId, "guest", req.LastId);
        return NoContent();
    }

    internal static TableChatMessageDto ToChatDto(TableChatDoc m) =>
        new(m.Id, m.From, m.Text, m.At, m.Audio, m.Image, m.Deleted, m.EditedAt, m.ReplyPreview, m.File, m.FileName);

    internal static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var clean = string.Concat(name.Trim().Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        return clean.Length > 80 ? clean[^80..] : clean.Length == 0 ? "file" : clean;
    }

    /// <summary>Shared sanity for both directions: some text OR a small voice note.</summary>
    internal static string? ValidateMessage(SendTableChatRequest req)
    {
        var hasText = !string.IsNullOrWhiteSpace(req.Text);
        var hasAudio = !string.IsNullOrWhiteSpace(req.Audio);
        var hasImage = !string.IsNullOrWhiteSpace(req.Image);
        var hasFile = !string.IsNullOrWhiteSpace(req.File);
        if (!hasText && !hasAudio && !hasImage && !hasFile) return "Say something — text, voice, a photo or a file.";
        if (hasText && req.Text!.Trim().Length > 500) return "Keep messages under 500 characters.";
        if (hasAudio && !req.Audio!.StartsWith("data:audio/", StringComparison.OrdinalIgnoreCase))
            return "Voice notes only.";
        if (hasAudio && req.Audio!.Length > 900_000) return "That voice note is too long.";
        if (hasImage && !req.Image!.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return "Photos only.";
        if (hasImage && req.Image!.Length > 2_800_000) return "That photo is too large (max 2 MB).";
        if (hasFile && !req.File!.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "Bad file.";
        if (hasFile && req.File!.Length > 2_800_000) return "That file is too large (max 2 MB).";
        return null;
    }

    // ---------- The store side ----------

    [HttpGet("~/api/reservations")]
    [Authorize(Roles = "RestaurantOwner")]
    [RequirePerm(Perm.Reservations)]
    public async Task<ActionResult<List<ReservationDto>>> List(bool history = false)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var cutoff = DateTime.Now.AddHours(-6);
        var query = db.TableReservations.Where(r => r.RestaurantId == CurrentRestaurantId);
        query = history ? query.OrderByDescending(r => r.At).Take(200)
                        : query.Where(r => r.At > cutoff && r.Status != "cancelled" && r.Status != "seated")
                               .OrderBy(r => r.At);
        return Ok((await query.ToListAsync()).Select(ToDto).ToList());
    }

    /// <summary>
    /// The owner books a table themselves — a phone call, a walk-up, or the chat
    /// assistant. Born "confirmed": the house doesn't wait for its own approval.
    /// </summary>
    [HttpPost("~/api/reservations")]
    [Authorize(Roles = "RestaurantOwner")]
    [RequirePerm(Perm.Reservations)]
    public async Task<ActionResult<ReservationDto>> Create(OwnerReserveRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();

        var table = await db.StoreTables.FirstOrDefaultAsync(
            t => t.Id == req.TableId && t.RestaurantId == CurrentRestaurantId);
        if (table is null) return NotFound();
        if (req.At < DateTime.Now.AddMinutes(-10)) return BadRequest(new { code = "rsv.e.past", message = "That time has already passed." });
        if (req.At > DateTime.Now.AddDays(365)) return BadRequest(new { code = "rsv.e.tooFar", message = "Reservations open at most a year ahead." });

        // The house books by the same rule as its guests — no table twice over.
        if ((await TakenTablesAsync(CurrentRestaurantId, req.At, 90)).Contains(table.Id))
            return BadRequest(new { code = "rsv.e.taken", args = new[] { table.Name }, message = $"{table.Name} is already booked at that time." });

        var reservation = new TableReservation
        {
            RestaurantId = CurrentRestaurantId,
            TableId = table.Id,
            TableName = table.Name,
            Name = string.IsNullOrWhiteSpace(req.Name) ? "—" : req.Name.Trim(),
            Phone = "",
            Guests = Math.Clamp(req.Guests, 1, 50),
            At = req.At,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            Status = "confirmed",
            CreatedAt = DateTime.Now,
        };
        db.TableReservations.Add(reservation);
        await db.SaveChangesAsync();
        return Ok(ToDto(reservation));
    }

    /// <summary>Confirm, cancel, or seat. Seating occupies the table and opens its tab.</summary>
    [HttpPost("~/api/reservations/{id:int}/status")]
    [Authorize(Roles = "RestaurantOwner")]
    [RequirePerm(Perm.Reservations)]
    public async Task<ActionResult<ReservationDto>> SetStatus(int id, SetReservationStatusRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!Statuses.Contains(req.Status)) return BadRequest(new { message = "Unknown status." });

        var reservation = await db.TableReservations.FirstOrDefaultAsync(
            r => r.Id == id && r.RestaurantId == CurrentRestaurantId);
        if (reservation is null) return NotFound();

        var was = reservation.Status;
        reservation.Status = req.Status;

        if (req.Status == "seated")
        {
            // The party arrived: the floor plan shows them, and their invoice opens —
            // on every table they booked, not just the first.
            var ids = TablesOf(reservation);
            var tables = await db.StoreTables
                .Where(t => t.RestaurantId == CurrentRestaurantId && ids.Contains(t.Id)).ToListAsync();
            foreach (var table in tables)
            {
                table.GuestName = reservation.Name;
                table.StoreCustomerId = null;
                table.OccupiedAt = DateTime.Now;
                if (!await db.StoreTabs.AnyAsync(t => t.RestaurantId == CurrentRestaurantId && t.TableId == table.Id))
                {
                    db.StoreTabs.Add(new StoreTab
                    {
                        RestaurantId = CurrentRestaurantId,
                        TableId = table.Id,
                        GuestName = reservation.Name,
                        OpenedAt = DateTime.Now,
                    });
                }
            }
        }

        await db.SaveChangesAsync();

        // The house answered — a booked-online customer hears it on their phone.
        if (reservation.CustomerId is { } customerId && was != req.Status)
        {
            var word = req.Status switch
            {
                "confirmed" => ("✅", "confirmed"),
                "cancelled" => ("❌", "declined"),
                "seated" => ("🍽️", "ready — welcome!"),
                _ => default,
            };
            if (word != default)
                push.SendToUser(customerId, $"{word.Item1} Your table on {reservation.At:dd MMM HH:mm}",
                    $"{word.Item2} · {reservation.Guests} guests", "/reservations");
        }
        return Ok(ToDto(reservation));
    }
}
