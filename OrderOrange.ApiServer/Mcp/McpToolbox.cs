using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Mcp;

/// <summary>
/// The OrderOrange tool catalogue: what a language model may ask the platform. Typed
/// tools cover the questions a manager actually asks (sales, menu, orders, bookings,
/// stock, visitors); two guarded raw tools (read-only SQL, read-only Mongo) cover the
/// rest, with <c>schema</c> telling the model what exists. Writes are few, explicit and
/// off unless <c>Mcp:AllowWrites</c> is true.
/// </summary>
public static class McpToolbox
{
    public static IReadOnlyList<McpTool> All { get; } = Build();

    public static McpTool? Find(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    // ───────────────────────────── schema helpers ─────────────────────────────

    private static string Schema(string propsJson, params string[] required) =>
        $$"""{"type":"object","properties":{{{propsJson}}},"required":[{{string.Join(",", required.Select(r => $"\"{r}\""))}}],"additionalProperties":false}""";

    private const string PStore = "\"storeId\":{\"type\":\"integer\",\"description\":\"Store id (e.g. 5000216). Omit for all real stores.\"}";
    private const string PFromTo = "\"from\":{\"type\":\"string\",\"description\":\"Start date/time, ISO 8601 (default: 30 days ago)\"},\"to\":{\"type\":\"string\",\"description\":\"End date/time, ISO 8601 (default: now; a bare date covers the whole day)\"}";
    private static string PLimit(int def, int max) => $"\"limit\":{{\"type\":\"integer\",\"minimum\":1,\"maximum\":{max},\"default\":{def}}}";

    private static List<McpTool> Build() =>
    [
        new("platform_overview", "Platform overview",
            "Counts and money for the whole platform right now: real stores (approved/open), customers, orders and revenue today / 7 days / 30 days, pending reservations, open support tickets. Start here for 'how is the business doing?'.",
            Schema(""), ReadOnly: true, PlatformOverview),

        new("list_stores", "List stores",
            "Real, hand-onboarded stores (restaurants, cafes, shops) with type, area, open state, rating, fees and short link. Filter by free text, store type or open state. Set includeGenerated to also see the generated demo catalogue.",
            Schema("\"query\":{\"type\":\"string\",\"description\":\"Words in the name, area or cuisine\"},\"type\":{\"type\":\"string\",\"description\":\"Restaurant|Cafe|Market|Pharmacy|Flowers|Shop\"},\"openOnly\":{\"type\":\"boolean\"},\"includeGenerated\":{\"type\":\"boolean\",\"default\":false}," + PLimit(50, 200)),
            ReadOnly: true, ListStores),

        new("get_store", "Store profile",
            "One store in full: profile, contact, fees, opening hours per weekday, cuisines, flags (self delivery, pickup, online booking), menu size and 30-day order/revenue figures. Give the id or the slug.",
            Schema("\"id\":{\"type\":\"integer\"},\"slug\":{\"type\":\"string\",\"description\":\"The short-link handle, e.g. 'ttalk'\"}"),
            ReadOnly: true, GetStore),

        new("get_menu", "Store menu",
            "The public menu of one store grouped by category: item id, name, alternate-language names, description, price, discount, availability, popular flag, ordering window. Use it to answer 'what do they sell / how much is X'.",
            Schema(PStore + ",\"includeUnavailable\":{\"type\":\"boolean\",\"default\":true}", "storeId"),
            ReadOnly: true, GetMenu),

        new("search_dishes", "Search dishes",
            "Find dishes/products across the real stores (or one store) by a word in the name or multilingual keywords; optional price ceiling. Returns item, price, store, availability.",
            Schema("\"query\":{\"type\":\"string\"}," + PStore + ",\"maxPrice\":{\"type\":\"number\"}," + PLimit(30, 100), "query"),
            ReadOnly: true, SearchDishes),

        new("list_orders", "List orders",
            "Orders in a date window, newest first, with status, type (delivery/pickup/dine-in table), payment, totals, customer name and item count. Filter by store, status and customer name.",
            Schema(PStore + "," + PFromTo + ",\"status\":{\"type\":\"string\",\"description\":\"Pending|Accepted|Preparing|Ready|PickedUp|OnTheWay|Delivered|Cancelled\"},\"customer\":{\"type\":\"string\",\"description\":\"Part of the customer's name\"}," + PLimit(50, 200)),
            ReadOnly: true, ListOrders),

        new("get_order", "Order detail",
            "One order with every line, the status timeline and the review if any. Give the numeric id or the human number (e.g. MF-1042).",
            Schema("\"id\":{\"type\":\"integer\"},\"number\":{\"type\":\"string\"}"),
            ReadOnly: true, GetOrder),

        new("sales_report", "Sales report",
            "Revenue and order counts bucketed by day, week, month, hour of day or weekday for a date window, plus splits by status, payment method and order type and the average ticket. Cancelled orders are counted separately, never in revenue.",
            Schema(PStore + "," + PFromTo + ",\"groupBy\":{\"type\":\"string\",\"enum\":[\"day\",\"week\",\"month\",\"hour\",\"weekday\"],\"default\":\"day\"}"),
            ReadOnly: true, SalesReport),

        new("top_items", "Top-selling items",
            "Best sellers in a date window: quantity sold, revenue, number of orders, per item (and store when not filtered).",
            Schema(PStore + "," + PFromTo + "," + PLimit(20, 100)),
            ReadOnly: true, TopItems),

        new("customers", "Customers",
            "Customers ranked by spend in a date window: name, orders, total spent, first/last order, favourite store. Phone numbers are masked unless includeContact is true.",
            Schema(PStore + "," + PFromTo + ",\"query\":{\"type\":\"string\",\"description\":\"Part of the name\"},\"includeContact\":{\"type\":\"boolean\",\"default\":false}," + PLimit(50, 200)),
            ReadOnly: true, Customers),

        new("reservations", "Table reservations",
            "Table bookings in a date window (default: today onward, 14 days) with guests, time, duration, status, table and note.",
            Schema(PStore + "," + PFromTo + ",\"status\":{\"type\":\"string\",\"description\":\"pending|confirmed|cancelled|seated\"}," + PLimit(100, 500)),
            ReadOnly: true, Reservations),

        new("inventory", "Inventory / raw materials",
            "A store's raw-material shelf: quantity, unit, minimum, unit cost, shelf value, supplier; lowStockOnly keeps what is at or under its minimum.",
            Schema(PStore + ",\"lowStockOnly\":{\"type\":\"boolean\",\"default\":false}", "storeId"),
            ReadOnly: true, Inventory),

        new("staff", "Store staff",
            "A store's team: name, role, hired date, active flag, linked login. Salaries only when includeSalary is true; national ids are never returned.",
            Schema(PStore + ",\"includeSalary\":{\"type\":\"boolean\",\"default\":false}", "storeId"),
            ReadOnly: true, Staff),

        new("assistant_chats", "Assistant conversations",
            "What visitors typed to the customer/partner/POS assistants and what came back (MongoDB botChats): filter by text, address, store, bot and date.",
            Schema("\"query\":{\"type\":\"string\"},\"ip\":{\"type\":\"string\"}," + PStore + ",\"bot\":{\"type\":\"string\",\"description\":\"customer|partner|pos\"}," + PFromTo + "," + PLimit(50, 300)),
            ReadOnly: true, AssistantChats),

        new("visitors", "Visitor analytics",
            "Page views from MongoDB visits grouped by day, page, app or address for a date window, with distinct visitors.",
            Schema(PFromTo + ",\"groupBy\":{\"type\":\"string\",\"enum\":[\"day\",\"page\",\"app\",\"ip\"],\"default\":\"day\"}," + PLimit(50, 500)),
            ReadOnly: true, Visitors),

        new("schema", "Database schema",
            "What exists: every SQL table with its columns and row count, and every MongoDB collection with its document count and sample field names. Call before sql_query or mongo_find.",
            Schema("\"table\":{\"type\":\"string\",\"description\":\"Only this SQL table (columns in full)\"},\"collection\":{\"type\":\"string\",\"description\":\"Only this Mongo collection (one sample document)\"}"),
            ReadOnly: true, SchemaTool),

        new("sql_query", "Read-only SQL",
            "Run one read-only T-SQL SELECT (or WITH … SELECT) against the SQL Server database. Rows are capped (default 200). INSERT/UPDATE/DELETE/DDL, multiple statements, EXEC and sensitive columns are refused. Prefer the typed tools; use this for anything they do not cover.",
            Schema("\"sql\":{\"type\":\"string\"},\"maxRows\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":1000,\"default\":200}", "sql"),
            ReadOnly: true, SqlQuery),

        new("mongo_find", "Read-only MongoDB find",
            "Query one MongoDB collection: filter, projection and sort as MongoDB JSON documents (extended JSON accepted), capped rows. Collections holding secrets are refused.",
            Schema("\"collection\":{\"type\":\"string\"},\"filter\":{\"type\":\"object\",\"description\":\"Mongo query document, e.g. {\\\"RestaurantId\\\":5000216,\\\"IsAvailable\\\":true}\"},\"projection\":{\"type\":\"object\"},\"sort\":{\"type\":\"object\",\"description\":\"e.g. {\\\"At\\\":-1}\"}," + PLimit(100, 1000), "collection"),
            ReadOnly: true, MongoFind),

        new("mongo_aggregate", "Read-only MongoDB aggregate",
            "Run an aggregation pipeline (array of stages) on one collection; $out and $merge are refused; results capped.",
            Schema("\"collection\":{\"type\":\"string\"},\"pipeline\":{\"type\":\"array\",\"items\":{\"type\":\"object\"}}," + PLimit(200, 1000), "collection", "pipeline"),
            ReadOnly: true, MongoAggregate),

        // ───── writes: explicit, narrow, off by default ─────
        new("set_store_open", "Open or close a store",
            "Flip a store's 'taking orders now' switch (the owner's open/closed toggle). Requires Mcp:AllowWrites.",
            Schema(PStore + ",\"open\":{\"type\":\"boolean\"}", "storeId", "open"),
            ReadOnly: false, SetStoreOpen),

        new("set_item_availability", "Mark an item available / sold out",
            "Set one menu item's availability in the catalogue (and the search mirror). Requires Mcp:AllowWrites.",
            Schema("\"itemId\":{\"type\":\"integer\"},\"available\":{\"type\":\"boolean\"}", "itemId", "available"),
            ReadOnly: false, SetItemAvailability),
    ];

    // ───────────────────────────── typed tools ─────────────────────────────

    private static async Task<object?> PlatformOverview(ToolContext c, Args a, CancellationToken ct)
    {
        var floor = c.RealStoreFloor;
        var now = DateTime.Now;
        var today = now.Date;
        var stores = await c.Db.Restaurants.Where(r => r.Id >= floor).Select(r => new { r.IsApproved, r.IsOpen, r.StoreType }).ToListAsync(ct);
        var customers = await c.Db.Users.CountAsync(u => u.Role == UserRole.Customer, ct);
        var partners = await c.Db.Users.CountAsync(u => u.Role == UserRole.RestaurantOwner, ct);
        var drivers = await c.Db.Users.CountAsync(u => u.Role == UserRole.Driver, ct);

        async Task<object> Period(DateTime from)
        {
            var q = c.Db.Orders.Where(o => o.RestaurantId >= floor && o.PlacedAt >= from);
            var all = await q.CountAsync(ct);
            var cancelled = await q.CountAsync(o => o.Status == OrderStatus.Cancelled, ct);
            var revenue = await q.Where(o => o.Status != OrderStatus.Cancelled).SumAsync(o => (decimal?)o.Total, ct) ?? 0m;
            return new { orders = all, cancelled, revenue = Math.Round(revenue, 3), avgTicket = all - cancelled > 0 ? Math.Round(revenue / (all - cancelled), 3) : 0m };
        }

        var pendingReservations = await c.Db.TableReservations.CountAsync(r => r.RestaurantId >= floor && r.Status == "pending" && r.At >= now.AddHours(-2), ct);
        var activeOrders = await c.Db.Orders.CountAsync(o => o.RestaurantId >= floor && o.Status < OrderStatus.Delivered, ct);
        var openTickets = (int)await c.Mongo.GetCollection<BsonDocument>("supportTickets")
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.In("Status", new[] { "open", "in_progress", "waiting" }), cancellationToken: ct);
        var menuItems = (int)await c.Mongo.GetCollection<BsonDocument>("menuItems")
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Gte("RestaurantId", floor), cancellationToken: ct);

        return new
        {
            asOf = now,
            currency = "OMR",
            stores = new
            {
                total = stores.Count,
                approved = stores.Count(s => s.IsApproved),
                openNow = stores.Count(s => s.IsApproved && s.IsOpen),
                byType = stores.Where(s => s.IsApproved).GroupBy(s => s.StoreType.ToString()).Select(g => new { type = g.Key, count = g.Count() }),
                menuItems,
            },
            people = new { customers, partners, drivers },
            orders = new { active = activeOrders, today = await Period(today), last7Days = await Period(today.AddDays(-6)), last30Days = await Period(today.AddDays(-29)) },
            reservationsPending = pendingReservations,
            supportTicketsOpen = openTickets,
        };
    }

    private static async Task<object?> ListStores(ToolContext c, Args a, CancellationToken ct)
    {
        var floor = a.Bool("includeGenerated", false) ? int.MinValue : c.RealStoreFloor;
        var limit = Math.Clamp(a.Int("limit", 50), 1, 200);
        var q = c.Db.Restaurants.Include(r => r.Cuisine).AsNoTracking().Where(r => r.Id >= floor);
        if (a.Str("query") is { Length: > 0 } text)
        {
            var t = text.ToLower();
            q = q.Where(r => r.Name.ToLower().Contains(t) || r.Area.ToLower().Contains(t) || r.Cuisine.Name.ToLower().Contains(t) || r.NameLocalized.ToLower().Contains(t));
        }
        if (a.Str("type") is { Length: > 0 } type && Enum.TryParse<StoreType>(type, true, out var st)) q = q.Where(r => r.StoreType == st);
        if (a.Bool("openOnly", false)) q = q.Where(r => r.IsOpen && r.IsApproved);
        var rows = await q.OrderBy(r => r.SuggestedOrder == null).ThenBy(r => r.SuggestedOrder).ThenByDescending(r => r.Id).Take(limit).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var ratings = await c.Db.Reviews.Where(v => ids.Contains(v.RestaurantId)).GroupBy(v => v.RestaurantId)
            .Select(g => new { g.Key, Avg = g.Average(v => v.RestaurantRating), Count = g.Count() }).ToDictionaryAsync(x => x.Key, ct);
        return new
        {
            count = rows.Count,
            stores = rows.Select(r => new
            {
                id = r.Id, name = r.Name, type = r.StoreType.ToString(), cuisine = r.Cuisine?.Name, area = r.Area,
                approved = r.IsApproved, openNow = r.IsOpen,
                rating = ratings.TryGetValue(r.Id, out var rt) ? Math.Round(rt.Avg, 1) : (double?)null, reviews = ratings.TryGetValue(r.Id, out var rc) ? rc.Count : 0,
                deliveryFee = r.DeliveryFee, minOrder = r.MinOrder, prepMinutes = r.AvgPrepMinutes,
                selfDelivery = r.SelfDelivery, pickup = r.AllowsPickup, onlineBooking = r.OnlineReservations,
                link = string.IsNullOrWhiteSpace(r.Slug) ? $"/restaurant/{r.Id}" : "/" + r.Slug,
                suggestedOrder = r.SuggestedOrder,
            }),
        };
    }

    private static async Task<object?> GetStore(ToolContext c, Args a, CancellationToken ct)
    {
        var q = c.Db.Restaurants.Include(r => r.Cuisine).Include(r => r.ExtraCuisines).ThenInclude(x => x.Cuisine).AsNoTracking();
        Restaurant? r = null;
        if (a.IntOrNull("id") is { } id and > 0) r = await q.FirstOrDefaultAsync(x => x.Id == id, ct);
        else if (a.Str("slug") is { Length: > 0 } slug) { var s = slug.Trim().TrimStart('/').ToLowerInvariant(); r = await q.FirstOrDefaultAsync(x => x.Slug == s, ct); }
        if (r is null) return Error("Store not found. Give 'id' (e.g. 5000216) or 'slug'. list_stores shows both.");

        var hours = await c.Db.RestaurantHours.Where(h => h.RestaurantId == r.Id).OrderBy(h => h.Day).ToListAsync(ct);
        var since = DateTime.Now.AddDays(-30);
        var orders = c.Db.Orders.Where(o => o.RestaurantId == r.Id && o.PlacedAt >= since);
        var count = await orders.CountAsync(ct);
        var revenue = await orders.Where(o => o.Status != OrderStatus.Cancelled).SumAsync(o => (decimal?)o.Total, ct) ?? 0m;
        var menu = await c.Catalog.PublicMenuAsync(r.Id, includeInStoreOnly: true);
        var ratings = await c.Db.Reviews.Where(v => v.RestaurantId == r.Id).Select(v => (double)v.RestaurantRating).ToListAsync(ct);
        return new
        {
            id = r.Id, name = r.Name, names = ParseJsonDict(r.NameLocalized), description = r.Description, slug = r.Slug,
            type = r.StoreType.ToString(), cuisine = r.Cuisine?.Name, extraCuisines = r.ExtraCuisines.Select(x => x.Cuisine.Name),
            approved = r.IsApproved, openNow = r.IsOpen,
            contact = new { phone = r.Phone, whatsapp = r.Whatsapp, email = r.Email, website = r.Website, instagram = r.Instagram },
            location = new { area = r.Area, street = r.Street, lat = r.Lat, lng = r.Lng },
            commerce = new { deliveryFee = r.DeliveryFee, minOrder = r.MinOrder, prepMinutes = r.AvgPrepMinutes, selfDelivery = r.SelfDelivery, pickup = r.AllowsPickup, onlineBooking = r.OnlineReservations, taxPercent = r.TaxPercent, commissionPercent = r.CommissionPercent, vatNumber = r.VatNumber, crNumber = r.CrNumber },
            hours = hours.Select(h => new { day = ((DayOfWeek)h.Day).ToString(), closed = h.IsClosed, open = h.Open.ToString(@"hh\:mm"), close = h.Close.ToString(@"hh\:mm") }),
            rating = ratings.Count > 0 ? Math.Round(ratings.Average(), 2) : (double?)null, reviews = ratings.Count,
            menu = new { categories = menu.Count, items = menu.Sum(m => m.Items.Count), available = menu.Sum(m => m.Items.Count(i => i.IsAvailable)) },
            last30Days = new { orders = count, revenue = Math.Round(revenue, 3) },
            link = string.IsNullOrWhiteSpace(r.Slug) ? $"/restaurant/{r.Id}" : "/" + r.Slug,
        };
    }

    private static async Task<object?> GetMenu(ToolContext c, Args a, CancellationToken ct)
    {
        var storeId = a.Int("storeId", 0);
        var store = await c.Db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Id == storeId, ct);
        if (store is null) return Error($"Store {storeId} not found.");
        var includeUnavailable = a.Bool("includeUnavailable", true);
        var menu = await c.Catalog.PublicMenuAsync(storeId, includeInStoreOnly: true);
        return new
        {
            storeId, store = store.Name, currency = "OMR",
            categories = menu.Select(cat => new
            {
                id = cat.Id, name = cat.Name, names = cat.Names,
                items = cat.Items.Where(i => includeUnavailable || i.IsAvailable).Select(i => new
                {
                    id = i.Id, name = i.Name, names = i.Names, description = i.Description,
                    price = i.Price, discountPercent = i.DiscountPercent, finalPrice = Math.Round(i.Price * (1 - i.DiscountPercent / 100m), 3),
                    available = i.IsAvailable, popular = i.IsPopular, unit = i.Unit, leadTimeDays = i.LeadTimeDays,
                    orderWindow = i.AvailableFromMinutes is null && i.AvailableToMinutes is null && string.IsNullOrEmpty(i.AvailableDays) ? null
                        : new { from = Minutes(i.AvailableFromMinutes), to = Minutes(i.AvailableToMinutes), days = i.AvailableDays },
                }),
            }),
        };
    }

    private static async Task<object?> SearchDishes(ToolContext c, Args a, CancellationToken ct)
    {
        var query = (a.Str("query") ?? "").Trim();
        if (query.Length < 2) return Error("query needs at least two characters.");
        var limit = Math.Clamp(a.Int("limit", 30), 1, 100);
        var storeId = a.IntOrNull("storeId");
        var maxPrice = a.Dec("maxPrice");
        var hits = await c.Catalog.SearchAsync(query, 400);
        var floor = c.RealStoreFloor;
        var filtered = hits.Where(h => storeId is { } s ? h.RestaurantId == s : h.RestaurantId >= floor)
            .Where(h => maxPrice is not { } mp || h.Price * (1 - h.DiscountPercent / 100m) <= mp)
            .OrderByDescending(h => h.IsAvailable).ThenByDescending(h => h.IsPopular).ThenBy(h => h.Price)
            .Take(limit).ToList();
        var ids = filtered.Select(h => h.RestaurantId).Distinct().ToList();
        var names = await c.Db.Restaurants.Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Name, r.IsOpen }).ToDictionaryAsync(r => r.Id, ct);
        return new
        {
            query, count = filtered.Count,
            items = filtered.Select(h => new
            {
                id = h.Id, name = h.Name, names = h.Names, price = h.Price, discountPercent = h.DiscountPercent,
                finalPrice = Math.Round(h.Price * (1 - h.DiscountPercent / 100m), 3), available = h.IsAvailable, popular = h.IsPopular,
                storeId = h.RestaurantId, store = names.GetValueOrDefault(h.RestaurantId)?.Name, storeOpen = names.GetValueOrDefault(h.RestaurantId)?.IsOpen,
            }),
        };
    }

    private static IQueryable<Order> OrdersIn(ToolContext c, Args a, out DateTime from, out DateTime to, int defaultDays = 30)
    {
        (from, to) = a.Window(defaultDays);
        var f = from; var t = to;
        var q = c.Db.Orders.AsNoTracking().Where(o => o.PlacedAt >= f && o.PlacedAt < t);
        q = a.IntOrNull("storeId") is { } s and > 0 ? q.Where(o => o.RestaurantId == s) : q.Where(o => o.RestaurantId >= c.RealStoreFloor);
        return q;
    }

    private static async Task<object?> ListOrders(ToolContext c, Args a, CancellationToken ct)
    {
        var q = OrdersIn(c, a, out var from, out var to);
        if (a.Str("status") is { Length: > 0 } st && Enum.TryParse<OrderStatus>(st, true, out var status)) q = q.Where(o => o.Status == status);
        if (a.Str("customer") is { Length: > 0 } cu) { var l = cu.ToLower(); q = q.Where(o => o.Customer.FullName.ToLower().Contains(l)); }
        var limit = Math.Clamp(a.Int("limit", 50), 1, 200);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.PlacedAt).Take(limit).Select(o => new
        {
            id = o.Id, number = o.Number, placedAt = o.PlacedAt, status = o.Status.ToString(), type = o.OrderType.ToString(), table = o.TableName,
            storeId = o.RestaurantId, store = o.Restaurant.Name, customer = o.Customer.FullName,
            items = o.Items.Count, subtotal = o.Subtotal, deliveryFee = o.DeliveryFee, discount = o.Discount, tax = o.TaxAmount, total = o.Total,
            payment = o.PaymentMethod.ToString(), paid = o.IsPaid, coupon = o.CouponCode, scheduledFor = o.ScheduledFor, deliveredAt = o.DeliveredAt,
        }).ToListAsync(ct);
        return new { from, to, total, returned = rows.Count, orders = rows };
    }

    private static async Task<object?> GetOrder(ToolContext c, Args a, CancellationToken ct)
    {
        var q = c.Db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Events).Include(o => o.Review).Include(o => o.Restaurant).Include(o => o.Customer);
        Order? o = null;
        if (a.IntOrNull("id") is { } id and > 0) o = await q.FirstOrDefaultAsync(x => x.Id == id, ct);
        else if (a.Str("number") is { Length: > 0 } n) { var num = n.Trim(); o = await q.FirstOrDefaultAsync(x => x.Number == num, ct); }
        if (o is null) return Error("Order not found. Give 'id' or 'number' (e.g. MF-1042).");
        if (c.ScopeStoreId is { } own && o.RestaurantId != own) return Error("That order belongs to another store.");
        return new
        {
            id = o.Id, number = o.Number, placedAt = o.PlacedAt, status = o.Status.ToString(), type = o.OrderType.ToString(), table = o.TableName,
            store = new { id = o.RestaurantId, name = o.Restaurant.Name }, customer = new { id = o.CustomerId, name = o.Customer.FullName },
            address = o.DeliveryAddress, notes = o.Notes, cancelReason = o.CancelReason, estimatedMinutes = o.EstimatedMinutes, scheduledFor = o.ScheduledFor,
            money = new { subtotal = o.Subtotal, deliveryFee = o.DeliveryFee, serviceFee = o.ServiceFee, discount = o.Discount, taxPercent = o.TaxPercent, tax = o.TaxAmount, total = o.Total, coupon = o.CouponCode },
            payment = new { method = o.PaymentMethod.ToString(), paid = o.IsPaid, reference = o.PaymentRef },
            courier = o.DriverUserId is not null || o.CourierName is not null ? new { driverUserId = o.DriverUserId, name = o.CourierName, lastSeen = o.CourierAt } : null,
            items = o.Items.Select(i => new { i.MenuItemId, i.Name, i.UnitPrice, i.Quantity, lineTotal = i.UnitPrice * i.Quantity, i.Notes }),
            timeline = o.Events.OrderBy(e => e.Id).Select(e => new { e.Id, status = e.Status.ToString(), e.At, e.By }),
            review = o.Review is null ? null : new { rating = o.Review.RestaurantRating, driverRating = o.Review.DriverRating, o.Review.Comment, o.Review.CreatedAt },
            replacesOrderId = o.ReplacesOrderId,
        };
    }

    private static async Task<object?> SalesReport(ToolContext c, Args a, CancellationToken ct)
    {
        var q = OrdersIn(c, a, out var from, out var to);
        var groupBy = (a.Str("groupBy") ?? "day").ToLowerInvariant();
        var rows = await q.Select(o => new { o.PlacedAt, o.Status, o.Total, o.PaymentMethod, o.OrderType, o.RestaurantId }).ToListAsync(ct);
        var ok = rows.Where(r => r.Status != OrderStatus.Cancelled).ToList();

        string Key(DateTime d) => groupBy switch
        {
            "week" => $"{ISOWeek.GetYear(d)}-W{ISOWeek.GetWeekOfYear(d):00}",
            "month" => d.ToString("yyyy-MM"),
            "hour" => d.ToString("HH") + ":00",
            "weekday" => d.DayOfWeek.ToString(),
            _ => d.ToString("yyyy-MM-dd"),
        };
        var buckets = rows.GroupBy(r => Key(r.PlacedAt)).OrderBy(g => groupBy == "weekday" ? ((int)Enum.Parse<DayOfWeek>(g.Key)).ToString() : g.Key)
            .Select(g => new
            {
                bucket = g.Key,
                orders = g.Count(r => r.Status != OrderStatus.Cancelled),
                cancelled = g.Count(r => r.Status == OrderStatus.Cancelled),
                revenue = Math.Round(g.Where(r => r.Status != OrderStatus.Cancelled).Sum(r => r.Total), 3),
            }).ToList();
        var revenue = ok.Sum(r => r.Total);
        return new
        {
            from, to, groupBy, currency = "OMR",
            totals = new { orders = ok.Count, cancelled = rows.Count - ok.Count, revenue = Math.Round(revenue, 3), avgTicket = ok.Count > 0 ? Math.Round(revenue / ok.Count, 3) : 0m },
            buckets,
            byStatus = rows.GroupBy(r => r.Status.ToString()).Select(g => new { status = g.Key, count = g.Count() }),
            byPayment = ok.GroupBy(r => r.PaymentMethod.ToString()).Select(g => new { method = g.Key, orders = g.Count(), revenue = Math.Round(g.Sum(r => r.Total), 3) }),
            byType = ok.GroupBy(r => r.OrderType.ToString()).Select(g => new { type = g.Key, orders = g.Count(), revenue = Math.Round(g.Sum(r => r.Total), 3) }),
            byStore = a.IntOrNull("storeId") is { } ? null : ok.GroupBy(r => r.RestaurantId).Select(g => new { storeId = g.Key, orders = g.Count(), revenue = Math.Round(g.Sum(r => r.Total), 3) }).OrderByDescending(x => x.revenue),
        };
    }

    private static async Task<object?> TopItems(ToolContext c, Args a, CancellationToken ct)
    {
        var q = OrdersIn(c, a, out var from, out var to).Where(o => o.Status != OrderStatus.Cancelled);
        var limit = Math.Clamp(a.Int("limit", 20), 1, 100);
        var items = await c.Db.OrderItems.AsNoTracking().Where(i => q.Select(o => o.Id).Contains(i.OrderId))
            .GroupBy(i => new { i.Name, i.Order.RestaurantId })
            .Select(g => new { g.Key.Name, storeId = g.Key.RestaurantId, quantity = g.Sum(i => i.Quantity), revenue = g.Sum(i => i.Quantity * i.UnitPrice), orders = g.Select(i => i.OrderId).Distinct().Count() })
            .OrderByDescending(x => x.quantity).Take(limit).ToListAsync(ct);
        var ids = items.Select(i => i.storeId).Distinct().ToList();
        var names = await c.Db.Restaurants.Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        return new { from, to, items = items.Select(i => new { item = i.Name, i.quantity, revenue = Math.Round(i.revenue, 3), i.orders, i.storeId, store = names.GetValueOrDefault(i.storeId) }) };
    }

    private static async Task<object?> Customers(ToolContext c, Args a, CancellationToken ct)
    {
        var q = OrdersIn(c, a, out var from, out var to, 365).Where(o => o.Status != OrderStatus.Cancelled);
        if (a.Str("query") is { Length: > 0 } cu) { var l = cu.ToLower(); q = q.Where(o => o.Customer.FullName.ToLower().Contains(l)); }
        var limit = Math.Clamp(a.Int("limit", 50), 1, 200);
        var includeContact = a.Bool("includeContact", false);
        var rows = await q.GroupBy(o => new { o.CustomerId, o.Customer.FullName, o.Customer.Phone })
            .Select(g => new { g.Key.CustomerId, g.Key.FullName, g.Key.Phone, orders = g.Count(), spent = g.Sum(o => o.Total), first = g.Min(o => o.PlacedAt), last = g.Max(o => o.PlacedAt), favouriteStoreId = g.GroupBy(o => o.RestaurantId).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault() })
            .OrderByDescending(x => x.spent).Take(limit).ToListAsync(ct);
        var ids = rows.Select(r => r.favouriteStoreId).Distinct().ToList();
        var names = await c.Db.Restaurants.Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Name }).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        return new
        {
            from, to, count = rows.Count,
            customers = rows.Select(r => new
            {
                id = r.CustomerId, name = r.FullName, phone = includeContact ? r.Phone : Mask(r.Phone),
                r.orders, spent = Math.Round(r.spent, 3), avgTicket = r.orders > 0 ? Math.Round(r.spent / r.orders, 3) : 0m,
                firstOrder = r.first, lastOrder = r.last, favouriteStore = names.GetValueOrDefault(r.favouriteStoreId),
            }),
        };
    }

    private static async Task<object?> Reservations(ToolContext c, Args a, CancellationToken ct)
    {
        var from = a.Date("from") ?? DateTime.Now.Date;
        var to = a.Date("to") ?? from.AddDays(14);
        if (a.Str("to") is { Length: 10 }) to = to.Date.AddDays(1);
        var q = c.Db.TableReservations.AsNoTracking().Include(r => r.Restaurant).Where(r => r.At >= from && r.At < to);
        q = a.IntOrNull("storeId") is { } s and > 0 ? q.Where(r => r.RestaurantId == s) : q.Where(r => r.RestaurantId >= c.RealStoreFloor);
        if (a.Str("status") is { Length: > 0 } st) { var l = st.ToLower(); q = q.Where(r => r.Status == l); }
        var limit = Math.Clamp(a.Int("limit", 100), 1, 500);
        var rows = await q.OrderBy(r => r.At).Take(limit).ToListAsync(ct);
        return new
        {
            from, to, count = rows.Count,
            reservations = rows.Select(r => new { r.Id, storeId = r.RestaurantId, store = r.Restaurant.Name, at = r.At, r.DurationMinutes, r.Guests, r.Status, table = string.IsNullOrEmpty(r.TableIds) ? r.TableName : r.TableName, online = r.TableId == 0, name = r.Name, phone = Mask(r.Phone), r.Note, r.Price, r.CreatedAt }),
        };
    }

    private static async Task<object?> Inventory(ToolContext c, Args a, CancellationToken ct)
    {
        var storeId = a.Int("storeId", 0);
        var q = c.Db.StoreMaterials.AsNoTracking().Where(m => m.RestaurantId == storeId);
        if (a.Bool("lowStockOnly", false)) q = q.Where(m => m.Quantity <= m.MinQuantity);
        var rows = await q.OrderBy(m => m.Category).ThenBy(m => m.Name).ToListAsync(ct);
        return new
        {
            storeId, count = rows.Count, shelfValue = Math.Round(rows.Sum(m => m.Quantity * m.UnitCost), 3),
            materials = rows.Select(m => new { m.Id, m.Name, m.Category, m.Unit, m.Quantity, m.MinQuantity, low = m.Quantity <= m.MinQuantity, m.UnitCost, value = Math.Round(m.Quantity * m.UnitCost, 3), m.Supplier }),
        };
    }

    private static async Task<object?> Staff(ToolContext c, Args a, CancellationToken ct)
    {
        var storeId = a.Int("storeId", 0);
        var includeSalary = a.Bool("includeSalary", false);
        var rows = await c.Db.StoreStaff.AsNoTracking().Where(s => s.RestaurantId == storeId).OrderBy(s => s.Role).ThenBy(s => s.FullName).ToListAsync(ct);
        return new
        {
            storeId, count = rows.Count,
            staff = rows.Select(s => new { s.Id, name = s.FullName, s.Role, phone = Mask(s.Phone), s.HiredOn, active = s.IsActive, linkedLogin = s.UserId is not null, salary = includeSalary ? s.MonthlySalary : (decimal?)null, s.Notes }),
        };
    }

    private static async Task<object?> AssistantChats(ToolContext c, Args a, CancellationToken ct)
    {
        var (from, to) = a.Window(30);
        var f = Builders<BsonDocument>.Filter.Gte("At", from) & Builders<BsonDocument>.Filter.Lt("At", to);
        if (a.Str("ip") is { Length: > 0 } ip) f &= Builders<BsonDocument>.Filter.Eq("Ip", ip.Trim());
        if (a.Str("bot") is { Length: > 0 } bot) f &= Builders<BsonDocument>.Filter.Eq("Bot", bot.Trim().ToLowerInvariant());
        if (a.IntOrNull("storeId") is { } s and > 0) f &= Builders<BsonDocument>.Filter.Eq("StoreId", s);
        if (a.Str("query") is { Length: > 0 } text)
        {
            var rx = new BsonRegularExpression(Regex.Escape(text.Trim()), "i");
            f &= Builders<BsonDocument>.Filter.Or(Builders<BsonDocument>.Filter.Regex("Text", rx), Builders<BsonDocument>.Filter.Regex("Reply", rx));
        }
        var limit = Math.Clamp(a.Int("limit", 50), 1, 300);
        var col = c.Mongo.GetCollection<BsonDocument>("botChats");
        var total = await col.CountDocumentsAsync(f, cancellationToken: ct);
        var docs = await col.Find(f).Sort(Builders<BsonDocument>.Sort.Descending("At")).Limit(limit)
            .Project(Builders<BsonDocument>.Projection.Exclude("Agent")).ToListAsync(ct);
        return new { from, to, total, returned = docs.Count, turns = docs.Select(ToNode) };
    }

    private static async Task<object?> Visitors(ToolContext c, Args a, CancellationToken ct)
    {
        var (from, to) = a.Window(7);
        var groupBy = (a.Str("groupBy") ?? "day").ToLowerInvariant();
        var limit = Math.Clamp(a.Int("limit", 50), 1, 500);
        var key = groupBy switch
        {
            "page" => new BsonDocument("$ifNull", new BsonArray { "$Page", "/" }),
            "app" => new BsonDocument("$ifNull", new BsonArray { "$App", "" }),
            "ip" => new BsonDocument("$ifNull", new BsonArray { "$Ip", "" }),
            _ => new BsonDocument("$dateToString", new BsonDocument { { "format", "%Y-%m-%d" }, { "date", "$At" }, { "timezone", "+04:00" } }),
        };
        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("At", new BsonDocument { { "$gte", from }, { "$lt", to } })),
            new BsonDocument("$group", new BsonDocument { { "_id", key }, { "views", new BsonDocument("$sum", 1) }, { "visitors", new BsonDocument("$addToSet", "$Ip") } }),
            new BsonDocument("$project", new BsonDocument { { "_id", 0 }, { "key", "$_id" }, { "views", 1 }, { "visitors", new BsonDocument("$size", "$visitors") } }),
            new BsonDocument("$sort", groupBy == "day" ? new BsonDocument("key", 1) : new BsonDocument("views", -1)),
            new BsonDocument("$limit", limit),
        };
        var rows = await c.Mongo.GetCollection<BsonDocument>("visits").Aggregate<BsonDocument>(pipeline, cancellationToken: ct).ToListAsync(ct);
        return new { from, to, groupBy, rows = rows.Select(ToNode) };
    }

    // ───────────────────────────── schema + raw access ─────────────────────────────

    private static readonly string[] HiddenCollections = ["loginCodes", "storeSocial", "storeSocialQueue"];
    private static readonly Regex ForbiddenSql = new(@"\b(insert|update|delete|drop|alter|create|truncate|merge|exec|execute|grant|revoke|into|openrowset|openquery|opendatasource|xp_\w+|sp_\w+|waitfor|shutdown|backup|restore|dbcc|bulk|kill|reconfigure)\b|--|/\*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SensitiveSql = new(@"passwordhash|savedcards|pushsubscriptions|smtppassword|nationalid|logincodes", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static async Task<object?> SchemaTool(ToolContext c, Args a, CancellationToken ct)
    {
        var onlyTable = a.Str("table");
        var onlyCollection = a.Str("collection");
        object? sql = null, mongo = null;

        if (onlyCollection is null)
        {
            var conn = c.Db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE, ISNULL(p.rows, 0) AS Rows
                FROM INFORMATION_SCHEMA.COLUMNS c
                LEFT JOIN sys.tables t ON t.name = c.TABLE_NAME
                LEFT JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
                WHERE c.TABLE_SCHEMA = 'dbo'
                ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
                """;
            var cols = new List<(string Table, string Column, string Type, bool Nullable, long Rows)>();
            await using (var r = await cmd.ExecuteReaderAsync(ct))
                while (await r.ReadAsync(ct))
                    cols.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3) == "YES", Convert.ToInt64(r.GetValue(4))));
            var tables = cols.Where(x => !SensitiveSql.IsMatch(x.Table) && (onlyTable is null || string.Equals(x.Table, onlyTable, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(x => x.Table).Select(g => new
                {
                    table = g.Key, rows = g.First().Rows,
                    columns = onlyTable is null ? g.Select(x => x.Column).ToArray() : null,
                    detail = onlyTable is null ? null : g.Select(x => new { x.Column, x.Type, x.Nullable }).Where(x => !SensitiveSql.IsMatch(x.Column)).ToArray(),
                }).ToList();
            sql = new { database = conn.Database, tables };
        }

        if (onlyTable is null)
        {
            var names = (await (await c.Mongo.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct))
                .Where(n => !HiddenCollections.Contains(n) && !n.StartsWith("system."))
                .Where(n => onlyCollection is null || n == onlyCollection).OrderBy(n => n).ToList();
            var cols = new List<object>();
            foreach (var n in names)
            {
                var col = c.Mongo.GetCollection<BsonDocument>(n);
                var count = await col.EstimatedDocumentCountAsync(cancellationToken: ct);
                var sample = await col.Find(FilterDefinition<BsonDocument>.Empty).Sort(Builders<BsonDocument>.Sort.Descending("_id")).Limit(1).FirstOrDefaultAsync(ct);
                cols.Add(onlyCollection is null
                    ? new { collection = n, documents = count, fields = sample?.Names.ToArray() ?? [] }
                    : new { collection = n, documents = count, fields = sample?.Names.ToArray() ?? [], sample = sample is null ? null : ToNode(Trim(sample)) });
            }
            mongo = new { database = c.Mongo.DatabaseNamespace.DatabaseName, collections = cols };
        }
        return new { sql, mongo, notes = "SQL: orders/users/stores/reservations/inventory/staff (the business of record). Mongo: menuItems/menuCategories (the live catalogue), botChats, visits, surveys, supportTickets, kdsTickets, wasteLog, loyalty*, warehouses*. Store ids ≥ " + c.RealStoreFloor + " are real stores; below are generated demo stores." };
    }

    private static async Task<object?> SqlQuery(ToolContext c, Args a, CancellationToken ct)
    {
        var sql = (a.Str("sql") ?? "").Trim().TrimEnd(';').Trim();
        if (sql.Length == 0) return Error("sql is required.");
        if (!Regex.IsMatch(sql, @"^\s*(select|with)\b", RegexOptions.IgnoreCase)) return Error("Only SELECT (or WITH … SELECT) statements are allowed.");
        if (sql.Contains(';')) return Error("One statement only — no semicolons.");
        if (ForbiddenSql.IsMatch(sql)) return Error("The statement contains a keyword that is not allowed on this read-only connection.");
        if (SensitiveSql.IsMatch(sql)) return Error("That table or column is confidential and cannot be read here.");
        var maxRows = Math.Clamp(a.Int("maxRows", 200), 1, 1000);

        var conn = c.Db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadUncommitted, ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = (DbTransaction)tx;
        cmd.CommandTimeout = 20;
        cmd.CommandText = sql;
        var started = DateTime.UtcNow;
        var rows = new List<Dictionary<string, object?>>();
        string[] columns;
        try
        {
            await using var r = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            columns = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
            var truncated = false;
            while (await r.ReadAsync(ct))
            {
                if (rows.Count >= maxRows) { truncated = true; break; }
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < r.FieldCount; i++)
                {
                    var v = await r.IsDBNullAsync(i, ct) ? null : r.GetValue(i);
                    row[columns[i]] = v is byte[] b ? $"<{b.Length} bytes>" : v is string s && s.Length > 2000 ? s[..2000] + "…" : v;
                }
                rows.Add(row);
            }
            await tx.RollbackAsync(ct);
            return new { columns, rowCount = rows.Count, truncated, elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds, rows };
        }
        catch (DbException ex)
        {
            return Error("SQL error: " + ex.Message);
        }
    }

    private static async Task<object?> MongoFind(ToolContext c, Args a, CancellationToken ct)
    {
        var name = (a.Str("collection") ?? "").Trim();
        if (name.Length == 0 || HiddenCollections.Contains(name) || name.StartsWith("system.")) return Error("That collection is not available.");
        var limit = Math.Clamp(a.Int("limit", 100), 1, 1000);
        BsonDocument filter, projection, sort;
        try
        {
            filter = ToBson(a.Element("filter")) ?? new BsonDocument();
            projection = ToBson(a.Element("projection")) ?? new BsonDocument();
            sort = ToBson(a.Element("sort")) ?? new BsonDocument();
        }
        catch (Exception ex) { return Error("Bad filter/projection/sort: " + ex.Message); }
        var col = c.Mongo.GetCollection<BsonDocument>(name);
        var find = col.Find(filter).Limit(limit);
        if (projection.ElementCount > 0) find = find.Project(projection);
        if (sort.ElementCount > 0) find = find.Sort(sort);
        var started = DateTime.UtcNow;
        var docs = await find.ToListAsync(ct);
        var total = await col.CountDocumentsAsync(filter, new CountOptions { MaxTime = TimeSpan.FromSeconds(5) }, ct);
        return new { collection = name, matched = total, returned = docs.Count, elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds, documents = docs.Select(d => ToNode(Trim(d))) };
    }

    private static async Task<object?> MongoAggregate(ToolContext c, Args a, CancellationToken ct)
    {
        var name = (a.Str("collection") ?? "").Trim();
        if (name.Length == 0 || HiddenCollections.Contains(name) || name.StartsWith("system.")) return Error("That collection is not available.");
        var limit = Math.Clamp(a.Int("limit", 200), 1, 1000);
        if (a.Element("pipeline") is not { ValueKind: JsonValueKind.Array } arr) return Error("pipeline must be an array of stage objects.");
        var stages = new List<BsonDocument>();
        foreach (var st in arr.EnumerateArray())
        {
            var doc = ToBson(st);
            if (doc is null) continue;
            if (doc.Names.Any(n => n is "$out" or "$merge" or "$function" or "$accumulator")) return Error("$out, $merge and server-side JavaScript are not allowed.");
            stages.Add(doc);
        }
        stages.Add(new BsonDocument("$limit", limit));
        var started = DateTime.UtcNow;
        var rows = await c.Mongo.GetCollection<BsonDocument>(name).Aggregate<BsonDocument>(stages, new AggregateOptions { MaxTime = TimeSpan.FromSeconds(20) }, ct).ToListAsync(ct);
        return new { collection = name, returned = rows.Count, elapsedMs = (int)(DateTime.UtcNow - started).TotalMilliseconds, rows = rows.Select(d => ToNode(Trim(d))) };
    }

    // ───────────────────────────── writes ─────────────────────────────

    private static async Task<object?> SetStoreOpen(ToolContext c, Args a, CancellationToken ct)
    {
        if (!c.WritesAllowed) return Error("Writes are not allowed for this caller.");
        var storeId = a.Int("storeId", 0);
        if (c.ScopeStoreId is { } own && storeId != own) return Error("A store may only change its own open sign.");
        var store = await c.Db.Restaurants.FirstOrDefaultAsync(r => r.Id == storeId, ct);
        if (store is null) return Error($"Store {storeId} not found.");
        store.IsOpen = a.Bool("open", true);
        await c.Db.SaveChangesAsync(ct);
        return new { storeId, store = store.Name, openNow = store.IsOpen };
    }

    private static async Task<object?> SetItemAvailability(ToolContext c, Args a, CancellationToken ct)
    {
        if (!c.WritesAllowed) return Error("Writes are not allowed for this caller.");
        var itemId = a.Int("itemId", 0);
        var available = a.Bool("available", true);
        var col = c.Mongo.GetCollection<BsonDocument>("menuItems");
        var filter = Builders<BsonDocument>.Filter.Eq("_id", itemId);
        if (c.ScopeStoreId is { } own) filter &= Builders<BsonDocument>.Filter.Eq("RestaurantId", own);   // one store may only touch its own items
        var res = await col.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Set("IsAvailable", available), cancellationToken: ct);
        if (res.MatchedCount == 0) return Error($"Menu item {itemId} not found in this store's catalogue.");
        var mirror = await c.Db.MenuItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (mirror is not null) { mirror.IsAvailable = available; await c.Db.SaveChangesAsync(ct); }
        return new { itemId, available, searchMirrorUpdated = mirror is not null };
    }

    // ───────────────────────────── helpers ─────────────────────────────

    public static object Error(string message) => new ToolError(message);
    public sealed record ToolError(string Error);

    private static string? Mask(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var p = phone.Trim();
        return p.Length <= 3 ? "***" : new string('*', p.Length - 3) + p[^3..];
    }

    private static string? Minutes(int? m) => m is null ? null : $"{m / 60:00}:{m % 60:00}";

    private static Dictionary<string, string>? ParseJsonDict(string json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch { return null; }
    }

    /// <summary>Big binary-ish fields (photos, logos) are replaced with their size so a row stays readable.</summary>
    private static BsonDocument Trim(BsonDocument d)
    {
        foreach (var name in d.Names.ToList())
        {
            var v = d[name];
            if (v.IsString && v.AsString.Length > 4000) d[name] = $"<{v.AsString.Length} chars>";
            else if (v.IsBsonBinaryData) d[name] = $"<{v.AsBsonBinaryData.Bytes.Length} bytes>";
        }
        return d;
    }

    private static JsonNode? ToNode(BsonDocument d) =>
        JsonNode.Parse(d.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson }));

    private static BsonDocument? ToBson(JsonElement? e)
    {
        if (e is not { } el || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (el.ValueKind == JsonValueKind.String) return BsonDocument.Parse(el.GetString() ?? "{}");
        return BsonDocument.Parse(el.GetRawText());
    }
}
