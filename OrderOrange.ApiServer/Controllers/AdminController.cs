using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace OrderOrange.ApiServer.Controllers;

[Authorize(Roles = "Administrator")]
public class AdminController(AppDbContext db, ChatStore chat, CatalogStore catalog, IMemoryCache cache) : ApiControllerBase
{
    // ---------- Dashboard ----------

    [HttpGet("dashboard")]
    public async Task<AdminDashboardDto> Dashboard()
    {
        // Heavy aggregates are cached and kept warm by a background loop —
        // the admin never waits on a 750k-invoice scan after logging in.
        if (cache.TryGetValue(AdminDashboardService.CacheKey, out AdminDashboardDto? cached) && cached is not null)
            return cached;
        var dto = await AdminDashboardService.ComputeAsync(db);
        cache.Set(AdminDashboardService.CacheKey, dto, AdminDashboardService.CacheFor);
        return dto;
    }

    // ---------- Users ----------

    private IQueryable<User> UsersQuery(UserRole? role, string? search)
    {
        var query = db.Users.AsQueryable();
        if (role is not null) query = query.Where(u => u.Role == role);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // PREFIX search, deliberately: LIKE '%…%' over half a million wide rows took
            // minutes; 'starts with' rides the FullName/Email/Phone indexes as seeks.
            var s = search.Trim();
            query = query.Where(u => u.FullName.StartsWith(s) || u.Email.StartsWith(s) || u.Phone.StartsWith(s));
        }
        return query;
    }

    [HttpGet("users/count")]
    public async Task<int> UsersCount(UserRole? role = null, string? search = null)
    {
        // The unfiltered total came from COUNT(*) scanning the whole fat table — a
        // minute of IO to say "501,234". The engine's own row bookkeeping says the
        // same thing instantly.
        if (role is null && string.IsNullOrWhiteSpace(search))
            return await db.Database
                .SqlQuery<int>($@"SELECT CAST(SUM(row_count) AS int) AS [Value]
                                  FROM sys.dm_db_partition_stats
                                  WHERE object_id = OBJECT_ID('Users') AND index_id IN (0, 1)")
                .FirstAsync();
        return await UsersQuery(role, search).CountAsync();
    }

    [HttpGet("users")]
    public async Task<List<UserDto>> Users(UserRole? role = null, string? search = null, int skip = 0, int take = 50)
    {
        var users = await UsersQuery(role, search)
            .Include(u => u.DriverProfile)
            .OrderByDescending(u => u.Id) // newest first; cheap on the clustered key even at 10M rows
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100))
            .ToListAsync();

        // Owner → restaurant names resolved only for the visible page.
        var ownerIds = users.Where(u => u.Role == UserRole.RestaurantOwner).Select(u => u.Id).ToList();
        var restaurantsByOwner = ownerIds.Count == 0
            ? []
            : await db.Restaurants.Where(r => ownerIds.Contains(r.OwnerUserId))
                .GroupBy(r => r.OwnerUserId)
                .Select(g => new { g.Key, Name = g.Min(r => r.Name) })
                .ToDictionaryAsync(x => x.Key, x => x.Name);

        return users.Select(u => new UserDto(u.Id, u.FullName, u.Email, u.Phone, u.Role, u.IsActive,
            u.CreatedAt, restaurantsByOwner.GetValueOrDefault(u.Id), u.DriverProfile?.IsOnline, u.LastSeenAt)).ToList();
    }

    // ---------- Activity: page visits, presence, last seen ----------

    [HttpGet("activity")]
    public async Task<List<ActivityDto>> Activity(int skip = 0, int take = 40,
        DateTime? from = null, DateTime? to = null, UserRole? role = null,
        string? app = null, string? search = null)
    {
        var query = db.ActivityLogs.AsQueryable();
        if (from is not null) query = query.Where(a => a.At >= from.Value.Date);
        if (to is not null) query = query.Where(a => a.At < to.Value.Date.AddDays(1));
        if (role is not null) query = query.Where(a => a.Role == role);
        if (!string.IsNullOrWhiteSpace(app)) query = query.Where(a => a.App == app);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(a => a.UserName.Contains(s) || a.Page.Contains(s));
        }
        return await query
            .OrderByDescending(a => a.At)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100))
            .Select(a => new ActivityDto(a.UserId, a.UserName, a.Role, a.App, a.Page, a.At, a.Ip))
            .ToListAsync();
    }

    /// <summary>
    /// Who has been on the site, one row per address. Most visitors never sign in, so the
    /// address is the only identity there is — <c>Who</c> names the accounts that have
    /// been seen from it, and is empty for a pure guest.
    /// </summary>
    [HttpGet("visitors")]
    public async Task<VisitorsDto> Visitors(int days = 7, string app = "Customer",
        string? search = null, int skip = 0, int take = 50)
    {
        var since = DateTime.Today.AddDays(-Math.Clamp(days, 1, 90) + 1);
        var query = db.ActivityLogs.AsNoTracking().Where(a => a.At >= since && a.Ip != "");
        if (!string.IsNullOrWhiteSpace(app) && app != "All") query = query.Where(a => a.App == app);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(a => a.Ip.Contains(s) || a.Page.Contains(s) || a.UserName.Contains(s));
        }

        // Rolled up in the database — the log runs to millions of rows and must never
        // be pulled into memory to be counted.
        var grouped = query.GroupBy(a => a.Ip).Select(g => new
        {
            Ip = g.Key,
            Visits = g.Count(),
            Pages = g.Select(x => x.Page).Distinct().Count(),
            FirstAt = g.Min(x => x.At),
            LastAt = g.Max(x => x.At),
            // Nobody signed in from here — a pure guest. Read off the group rather than
            // with a correlated Any() per address, which re-scanned the log every row.
            SignedIn = g.Max(x => x.UserId),
        });

        var totalVisitors = await grouped.CountAsync();
        var totalVisits = await query.CountAsync();
        var guests = await grouped.CountAsync(g => g.SignedIn == 0);

        var page = await grouped
            .OrderByDescending(g => g.LastAt)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 200))
            .ToListAsync();

        // The last page and the names are per-address details, fetched only for the rows
        // actually on screen rather than for every address in the window.
        var ips = page.Select(p => p.Ip).ToList();
        var detail = await query.Where(a => ips.Contains(a.Ip))
            .Select(a => new { a.Ip, a.Page, a.App, a.At, a.UserName })
            .ToListAsync();

        var rows = page.Select(p =>
        {
            var mine = detail.Where(d => d.Ip == p.Ip).ToList();
            var last = mine.OrderByDescending(d => d.At).First();
            var who = string.Join(", ", mine.Where(d => d.UserName.Length > 0)
                .Select(d => d.UserName).Distinct().Take(3));
            return new VisitorDto(p.Ip, p.Visits, p.Pages, p.FirstAt, p.LastAt, last.Page, last.App, who);
        }).ToList();

        return new VisitorsDto(totalVisitors, totalVisits, guests, rows);
    }

    /// <summary>Every page one address opened, most visited first — the drill-down row.</summary>
    [HttpGet("visitors/pages")]
    public async Task<List<VisitorPageDto>> VisitorPages(string ip, int days = 7, string app = "Customer")
    {
        if (string.IsNullOrWhiteSpace(ip)) return [];
        var since = DateTime.Today.AddDays(-Math.Clamp(days, 1, 90) + 1);

        var query = db.ActivityLogs.AsNoTracking().Where(a => a.At >= since && a.Ip == ip);
        if (!string.IsNullOrWhiteSpace(app) && app != "All") query = query.Where(a => a.App == app);

        // Grouped and ordered as an anonymous shape, then mapped. Ordering by a property
        // of a record built inside the projection is not something the provider can turn
        // into SQL — it throws at run time, which is how this was found.
        var rows = await query
            .GroupBy(a => new { a.App, a.Page })
            .Select(g => new
            {
                g.Key.App,
                g.Key.Page,
                Views = g.Count(),
                FirstAt = g.Min(x => x.At),
                LastAt = g.Max(x => x.At),
            })
            .OrderByDescending(g => g.LastAt)
            .Take(200)
            .ToListAsync();

        return rows.Select(r => new VisitorPageDto(r.App, r.Page, r.Views, r.FirstAt, r.LastAt)).ToList();
    }

    /// <summary>
    /// The insights board in one call: who came, which pages they opened, what they
    /// searched for — and, most valuable of all, which searches came back EMPTY.
    /// </summary>
    [HttpGet("insights")]
    public async Task<InsightsDto> Insights(int days = 7, string app = "Customer")
    {
        var since = DateTime.Today.AddDays(-Math.Clamp(days, 1, 90) + 1);

        var searches = db.SearchLogs.Where(s => s.At >= since);
        var visits = db.ActivityLogs.Where(a => a.At >= since);
        if (!string.IsNullOrWhiteSpace(app) && app != "All")
        {
            searches = searches.Where(s => s.App == app);
            visits = visits.Where(a => a.App == app);
        }

        var totalSearches = await searches.CountAsync();
        var zero = await searches.CountAsync(s => s.Results == 0);
        // A guest has no id, so identity falls back to the address they came from.
        var searchers = await searches.Select(s => s.UserId > 0 ? s.UserId.ToString() : "ip:" + s.Ip)
            .Distinct().CountAsync();

        // Grouped into an anonymous shape, ordered, and only then turned into the DTO.
        // Sorting by a property of a record built inside Select is not translatable and
        // throws at run time — it took the whole insights board down with a bare 500.
        var byTermRows = await searches
            .GroupBy(s => s.Term)
            .Select(g => new { Term = g.Key, Count = g.Count(), Zero = g.Count(x => x.Results == 0), LastAt = g.Max(x => x.At) })
            .OrderByDescending(t => t.Count).ThenByDescending(t => t.LastAt)
            .Take(15).ToListAsync();
        var byTerm = byTermRows.Select(t => new SearchTermDto(t.Term, t.Count, t.Zero, t.LastAt)).ToList();

        var emptyRows = await searches.Where(s => s.Results == 0)
            .GroupBy(s => s.Term)
            .Select(g => new { Term = g.Key, Count = g.Count(), LastAt = g.Max(x => x.At) })
            .OrderByDescending(t => t.Count).ThenByDescending(t => t.LastAt)
            .Take(12).ToListAsync();
        var empties = emptyRows.Select(t => new SearchTermDto(t.Term, t.Count, t.Count, t.LastAt)).ToList();

        var recentSearches = await searches
            .OrderByDescending(s => s.At).Take(30)
            .Select(s => new SearchHitDto(s.UserId, s.UserName, s.Term, s.Results, s.Locale, s.Ip, s.At))
            .ToListAsync();

        var topPageRows = await visits
            .GroupBy(a => new { a.App, a.Page })
            .Select(g => new
            {
                g.Key.App,
                g.Key.Page,
                Views = g.Count(),
                Visitors = g.Select(x => x.UserId).Distinct().Count(),
                LastAt = g.Max(x => x.At),
            })
            .OrderByDescending(p => p.Views)
            .Take(15).ToListAsync();
        var topPages = topPageRows
            .Select(p => new PageHitDto(p.App, p.Page, p.Views, p.Visitors, p.LastAt)).ToList();

        var recentVisits = await visits
            .OrderByDescending(a => a.At).Take(30)
            .Select(a => new ActivityDto(a.UserId, a.UserName, a.Role, a.App, a.Page, a.At, a.Ip))
            .ToListAsync();

        var hours = await searches.GroupBy(s => s.At.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() }).ToListAsync();
        var byHour = Enumerable.Range(0, 24)
            .Select(h => hours.FirstOrDefault(x => x.Hour == h)?.Count ?? 0).ToList();

        return new InsightsDto(
            totalSearches, searchers, zero,
            await visits.CountAsync(),
            await visits.Select(a => a.UserId).Distinct().CountAsync(),
            byTerm, empties, recentSearches, topPages, recentVisits, byHour);
    }

    // ---------- Live chat monitor ----------

    [HttpGet("chats")]
    public async Task<List<AdminChatSummaryDto>> Chats(int skip = 0, int take = 30, string? search = null)
    {
        // The conversations live in Mongo and the order/customer names in SQL, so a
        // search term has to be resolved to order ids first and used to narrow the
        // Mongo aggregation — the two stores cannot be joined in one query.
        List<int>? onlyOrders = null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            onlyOrders = await db.Orders
                .Where(o => o.Number.Contains(s) || o.Customer.FullName.Contains(s) || o.Restaurant.Name.Contains(s))
                .Select(o => o.Id)
                .Take(500)
                .ToListAsync();
            if (onlyOrders.Count == 0) return [];
        }

        var page = await chat.SummariesAsync(onlyOrders, skip, take);
        if (page.Count == 0) return [];

        var ids = page.Select(x => x.OrderId).ToList();
        var orders = await db.Orders.Where(o => ids.Contains(o.Id))
            .Select(o => new { o.Id, o.Number, o.Status, RestaurantName = o.Restaurant.Name,
                CustomerName = o.Customer.FullName, DriverName = o.Driver != null ? o.Driver.FullName : null })
            .ToDictionaryAsync(o => o.Id);

        return page.Select(x =>
        {
            var order = orders.GetValueOrDefault(x.OrderId);
            return new AdminChatSummaryDto(x.OrderId, order?.Number ?? $"#{x.OrderId}",
                order?.Status ?? OrderStatus.Pending, order?.RestaurantName ?? "—",
                order?.CustomerName ?? "—", order?.DriverName, x.Count,
                x.Last?.SenderName ?? "—", x.Last?.Text, x.Last?.AttachmentType, x.LastAt);
        }).ToList();
    }

    [HttpGet("chats/{orderId:int}")]
    public async Task<List<ChatMessageDto>> ChatMessages(int orderId) =>
        (await chat.TranscriptAsync(orderId)).Select(m => m.ToDto()).ToList();

    [HttpGet("activity/presence")]
    public async Task<List<UserPresenceDto>> Presence([FromServices] Services.PresenceStore beats)
    {
        var today = DateTime.Today;
        var visitsToday = await db.ActivityLogs.Where(a => a.At >= today)
            .GroupBy(a => a.UserId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var lastVisits = await db.ActivityLogs
            .GroupBy(a => a.UserId)
            .Select(g => g.OrderByDescending(a => a.At).First())
            .ToListAsync();
        var lastByUser = lastVisits.ToDictionary(a => a.UserId);
        var now = DateTime.Now;

        var users = await db.Users
            .Where(u => u.LastSeenAt != null)
            .OrderByDescending(u => u.LastSeenAt)
            .Take(60)
            .ToListAsync();
        var beat = await beats.ForAsync(users.Select(u => u.Id));

        return users.Select(u =>
        {
            var last = lastByUser.GetValueOrDefault(u.Id);
            var b = beat.GetValueOrDefault(u.Id);
            // a real page view is the visit; the heartbeat only says the app is open somewhere
            return new UserPresenceDto(u.Id, u.FullName, u.Role, u.LastSeenAt,
                visitsToday.GetValueOrDefault(u.Id), last?.Page, last?.App, now,
                last?.At, string.IsNullOrEmpty(b?.Ip) ? last?.Ip : b!.Ip, b?.App ?? last?.App);
        }).ToList();
    }

    // ---------- Reports ----------

    [HttpGet("reports")]
    public async Task<AdminReportDto> Report(DateTime? from = null, DateTime? to = null)
    {
        var start = (from ?? DateTime.Today.AddDays(-29)).Date;
        var end = (to ?? DateTime.Today).Date.AddDays(1); // exclusive upper bound

        if (db.Database.IsRelational()) db.Database.SetCommandTimeout(180);

        // Everything is aggregated in SQL — the range can hold millions of invoices.
        var inRange = db.Orders.Where(o => o.PlacedAt >= start && o.PlacedAt < end);
        var billable = inRange.Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);

        var statusCounts = await inRange.GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var totalOrders = statusCounts.Values.Sum();

        // Commission WITHOUT joining 5M stores per order: group by the handful of
        // distinct commission percentages instead (same trick as the dashboard).
        var billAgg = await billable.GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(o => o.Total) })
            .FirstOrDefaultAsync();
        var commissionByPct = await billable
            .GroupBy(o => o.Restaurant.CommissionPercent)
            .Select(g => new { Pct = g.Key, Subtotal = g.Sum(o => o.Subtotal) })
            .ToListAsync();
        var rangeCommission = commissionByPct.Sum(x => x.Subtotal * x.Pct / 100m);

        var days = (int)(end - start).TotalDays;
        var perDayRaw = await billable.GroupBy(o => o.PlacedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count(), Revenue = g.Sum(o => o.Total) })
            .ToListAsync();
        var perDay = Enumerable.Range(0, days).Select(i =>
        {
            var day = start.AddDays(i);
            var hit = perDayRaw.FirstOrDefault(d => d.Day == day);
            return new DayStatDto(day, hit?.Count ?? 0, hit?.Revenue ?? 0m);
        }).ToList();

        // Top 50 stores by revenue — grouped by ID only (no 5M-store join), then the
        // 50 names, commission rates and ratings are fetched for just those ids.
        var topRaw = await billable
            .GroupBy(o => o.RestaurantId)
            .Select(g => new
            {
                RestaurantId = g.Key,
                Orders = g.Count(),
                Delivered = g.Count(o => o.Status == OrderStatus.Delivered),
                Revenue = g.Sum(o => o.Total),
                Subtotal = g.Sum(o => o.Subtotal)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(50)
            .ToListAsync();

        var topIds = topRaw.Select(t => t.RestaurantId).ToList();
        var topStores = await db.Restaurants.Where(r => topIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name, r.LogoEmoji, r.CommissionPercent })
            .ToDictionaryAsync(r => r.Id);
        var ratings = await db.Reviews
            .Where(r => r.CreatedAt >= start && r.CreatedAt < end && topIds.Contains(r.RestaurantId))
            .GroupBy(r => r.RestaurantId)
            .Select(g => new { g.Key, Avg = g.Average(x => (double)x.RestaurantRating) })
            .ToDictionaryAsync(x => x.Key, x => Math.Round(x.Avg, 1));

        var perRestaurant = topRaw
            .Select(t => new RestaurantReportRow(
                t.RestaurantId,
                topStores.GetValueOrDefault(t.RestaurantId)?.Name ?? $"#{t.RestaurantId}",
                topStores.GetValueOrDefault(t.RestaurantId)?.LogoEmoji ?? "🍽️",
                t.Orders, t.Delivered, t.Revenue,
                t.Subtotal * (topStores.GetValueOrDefault(t.RestaurantId)?.CommissionPercent ?? 15m) / 100m + Pricing.ServiceFee * t.Orders,
                ratings.GetValueOrDefault(t.RestaurantId)))
            .ToList();

        return new AdminReportDto(
            totalOrders,
            statusCounts.GetValueOrDefault(OrderStatus.Delivered),
            statusCounts.GetValueOrDefault(OrderStatus.Cancelled),
            statusCounts.GetValueOrDefault(OrderStatus.Rejected),
            billAgg?.Total ?? 0m,
            rangeCommission + Pricing.ServiceFee * (billAgg?.Count ?? 0),
            billAgg is null or { Count: 0 } ? 0 : Math.Round(billAgg.Total / billAgg.Count, 3),
            await db.Users.CountAsync(u => u.Role == UserRole.Customer && u.CreatedAt >= start && u.CreatedAt < end),
            perDay,
            perRestaurant);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(CreateUserRequest req)
    {
        var email = req.Email.Trim().ToLower();
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "Name and email are required." });
        if (req.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        if (await db.Users.FirstOrDefaultAsync(u => u.Email == email) is { } existing)
        {
            // One email, several businesses: creating a "new partner" on an address that
            // already IS a partner adds another store to them rather than failing. Any
            // other duplicate is still a mistake worth stopping.
            if (existing.Role == UserRole.RestaurantOwner && req.Role == UserRole.RestaurantOwner)
            {
                if (string.IsNullOrWhiteSpace(req.RestaurantName) || req.CuisineId is null)
                    return BadRequest(new { message = "A store name and cuisine are required." });
                if (!await db.Cuisines.AnyAsync(c => c.Id == req.CuisineId))
                    return BadRequest(new { message = "Unknown cuisine." });

                db.Restaurants.Add(new Restaurant
                {
                    OwnerUserId = existing.Id, Name = req.RestaurantName.Trim(), CuisineId = req.CuisineId.Value,
                    StoreType = req.StoreType,
                    Description = "", Area = "", Street = "", Phone = req.Phone.Trim(),
                    DeliveryFee = 0.500m, MinOrder = 1.000m, AvgPrepMinutes = 20,
                    IsOpen = false, IsApproved = false, CommissionPercent = 0m, CreatedAt = DateTime.Now
                });
                await db.SaveChangesAsync();
                return NoContent();
            }
            return BadRequest(new { message = "An account with this email already exists." });
        }

        var user = new User
        {
            FullName = req.FullName.Trim(), Email = email, Phone = req.Phone.Trim(),
            PasswordHash = PasswordHasher.Hash(req.Password), Role = req.Role,
            IsActive = true, CreatedAt = DateTime.Now
        };
        db.Users.Add(user);

        switch (req.Role)
        {
            case UserRole.Driver:
                // Admin-created riders are trusted — no document review needed.
                db.DriverProfiles.Add(new DriverProfile { User = user, VehicleType = req.VehicleType ?? VehicleType.Motorbike, Verification = DriverVerificationStatus.Approved });
                break;

            case UserRole.RestaurantOwner:
                if (string.IsNullOrWhiteSpace(req.RestaurantName) || req.CuisineId is null)
                    return BadRequest(new { message = "A restaurant name and cuisine are required for a new owner." });
                if (!await db.Cuisines.AnyAsync(c => c.Id == req.CuisineId))
                    return BadRequest(new { message = "Unknown cuisine." });
                // Starts unapproved and closed — the admin flips the switch when onboarding is done.
                db.Restaurants.Add(new Restaurant
                {
                    Owner = user, Name = req.RestaurantName.Trim(), CuisineId = req.CuisineId.Value,
                    StoreType = req.StoreType,
                    Description = "", Area = "", Street = "", Phone = req.Phone.Trim(),
                    DeliveryFee = 0.500m, MinOrder = 1.000m, AvgPrepMinutes = 20,
                    IsOpen = false, IsApproved = false, CommissionPercent = 0m, CreatedAt = DateTime.Now
                });
                break;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// "Open as this user" — mints a single-use code the target app trades for a session.
    ///
    /// The power tool of support: see exactly what the partner sees, fix their menu with
    /// them on the phone. Bounded three ways — only an administrator can mint one, it
    /// dies in two minutes or on first use, and an administrator account can never be
    /// the target, so this can't be used to become another admin.
    /// </summary>
    [HttpPost("impersonate/{userId:int}")]
    public async Task<ActionResult<ImpersonationCodeDto>> Impersonate(int userId)
    {
        var target = await db.Users.FindAsync(userId);
        if (target is null) return NotFound();
        if (target.Role == UserRole.Administrator)
            return BadRequest(new { message = "Administrators cannot be impersonated." });
        if (!target.IsActive)
            return BadRequest(new { message = "This account is deactivated — activate it first." });

        // 256 bits from the crypto RNG: unguessable, and useless after two minutes anyway.
        var code = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        cache.Set($"impersonate:{code}", target.Id, TimeSpan.FromMinutes(2));

        // Signing in as someone else must leave a trace with the admin's own name on it.
        db.ActivityLogs.Add(new ActivityLog
        {
            UserId = CurrentUserId,
            UserName = CurrentUserName,
            Role = UserRole.Administrator,
            App = "Admin",
            Page = $"/impersonate/{target.Email}",
            At = DateTime.Now,
        });
        await db.SaveChangesAsync();

        return Ok(new ImpersonationCodeDto(code, 120));
    }

    /// <summary>Support action: force-set a user's password (they can change it afterwards).</summary>
    /// <summary>
    /// Rewrite a user's identity: name, the EMAIL they sign in with, phone. The email
    /// must stay unique — it is the username everywhere.
    /// </summary>
    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, AdminUpdateUserRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.FullName)) return BadRequest(new { message = "Name is required." });
        var email = (req.Email ?? "").Trim().ToLower();
        if (email.Length < 3 || !email.Contains('@')) return BadRequest(new { message = "A real email is required." });
        if (await db.Users.AnyAsync(u => u.Email == email && u.Id != id))
            return BadRequest(new { message = "Another account already uses this email." });
        user.FullName = req.FullName.Trim();
        user.Email = email;
        user.Phone = (req.Phone ?? "").Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("users/{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req)
    {
        if (req.NewPassword.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("users/{id:int}/toggle-active")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.Id == CurrentUserId)
            return BadRequest(new { message = "You cannot deactivate your own account." });
        user.IsActive = !user.IsActive;
        await db.SaveChangesAsync();
        return Ok(new { isActive = user.IsActive });
    }

    /// <summary>
    /// Hard delete — but ONLY for an account with no footprint: nothing owned, no
    /// team key, no orders placed. Anyone who has actually done anything keeps their
    /// row so history keeps its author; the switch on the card deactivates them.
    /// </summary>
    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.Id == CurrentUserId)
            return BadRequest(new { message = "You cannot delete your own account." });
        var owns = await db.Restaurants.AnyAsync(r => r.OwnerUserId == id);
        var onTeam = await db.StoreMembers.AnyAsync(m => m.UserId == id);
        var ordered = await db.Orders.AnyAsync(o => o.CustomerId == id);
        if (owns || onTeam || ordered)
            return BadRequest(new { message = "This user owns a store, holds a team key, or has orders — deactivate them instead." });
        db.Users.Remove(user);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Some other record still points at them — same answer as above.
            return BadRequest(new { message = "This user still appears in records — deactivate them instead." });
        }
        return NoContent();
    }

    // ---------- Restaurants ----------

    private IQueryable<Restaurant> RestaurantsQuery(string? search, bool? approved,
        StoreType? storeType = null, int? cuisineId = null, bool? open = null)
    {
        var query = db.Restaurants.AsQueryable();
        if (approved is not null) query = query.Where(r => r.IsApproved == approved);
        if (storeType is not null) query = query.Where(r => r.StoreType == storeType);
        if (cuisineId is not null) query = query.Where(r => r.CuisineId == cuisineId);
        if (open is not null) query = query.Where(r => r.IsOpen == open);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(r => r.Name.Contains(s) || r.Area.Contains(s) ||
                r.NameLocalized.Contains(s) || r.Owner.Email.Contains(s) || r.Owner.FullName.Contains(s));
        }
        return query;
    }

    private static IQueryable<Restaurant> SortStores(IQueryable<Restaurant> query, string? sort) => sort switch
    {
        "oldest" => query.OrderBy(r => r.Id),
        "name" => query.OrderBy(r => r.Name).ThenBy(r => r.Id),
        "commission" => query.OrderByDescending(r => r.CommissionPercent).ThenByDescending(r => r.Id),
        _ => query.OrderByDescending(r => r.Id) // newest first
    };

    [HttpGet("restaurants/count")]
    public Task<int> RestaurantsCount(string? search = null, bool? approved = null,
        StoreType? storeType = null, int? cuisineId = null, bool? open = null) =>
        RestaurantsQuery(search, approved, storeType, cuisineId, open).CountAsync();

    [HttpGet("restaurants")]
    public async Task<List<AdminRestaurantDto>> Restaurants(string? search = null, bool? approved = null,
        int skip = 0, int take = 50, StoreType? storeType = null, int? cuisineId = null,
        bool? open = null, string? sort = null)
    {
        var restaurants = await SortStores(RestaurantsQuery(search, approved, storeType, cuisineId, open), sort)
            .Include(r => r.Cuisine).Include(r => r.Owner)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100))
            .ToListAsync();

        var pageIds = restaurants.Select(r => r.Id).ToList();
        var stats = await db.Orders
            .Where(o => pageIds.Contains(o.RestaurantId) &&
                        o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected)
            .GroupBy(o => o.RestaurantId)
            .Select(g => new { g.Key, Count = g.Count(), Revenue = g.Sum(o => o.Total) })
            .ToDictionaryAsync(x => x.Key, x => (x.Count, x.Revenue));

        var ratings = await db.Reviews.Where(r => pageIds.Contains(r.RestaurantId))
            .GroupBy(r => r.RestaurantId)
            .Select(g => new { g.Key, Avg = g.Average(x => (double)x.RestaurantRating), Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => (Math.Round(x.Avg, 1), x.Count));

        return restaurants.Select(r =>
        {
            var (orders, revenue) = stats.GetValueOrDefault(r.Id);
            var (rating, ratingCount) = ratings.GetValueOrDefault(r.Id);
            return new AdminRestaurantDto(r.Id, r.Name, r.LogoEmoji, r.Cuisine.Name, r.Area,
                r.Owner.FullName, r.Owner.Email, r.IsApproved, r.IsOpen, r.CommissionPercent,
                rating, ratingCount, orders, revenue, r.OwnerUserId, r.SuggestedOrder);
        }).ToList();
    }

    /// <summary>
    /// Puts a shop into the customer home's "Suggested" strip (at the end of the running
    /// order) or takes it out — the star on the admin Restaurants page.
    /// </summary>
    [HttpPost("restaurants/{id:int}/suggest")]
    public async Task<ActionResult<object>> ToggleSuggested(int id)
    {
        var r = await db.Restaurants.FindAsync(id);
        if (r is null) return NotFound();
        if (r.SuggestedOrder is null)
        {
            var max = await db.Restaurants.MaxAsync(x => (int?)x.SuggestedOrder) ?? 0;
            r.SuggestedOrder = max + 1;
        }
        else
        {
            r.SuggestedOrder = null;
        }
        await db.SaveChangesAsync();
        return new { suggested = r.SuggestedOrder != null };
    }

    /// <summary>The strip as the admin panel shows it: featured shops in running order.</summary>
    [HttpGet("suggested")]
    public Task<List<AdminSuggestedDto>> SuggestedList() =>
        db.Restaurants
            .Where(r => r.SuggestedOrder != null)
            .OrderBy(r => r.SuggestedOrder).ThenBy(r => r.Id)
            .Select(r => new AdminSuggestedDto(r.Id, r.Name, r.LogoEmoji, r.IsApproved))
            .ToListAsync();

    /// <summary>
    /// The strip's full running order, authoritative: shops get position by their index
    /// in the body, and any currently featured shop missing from it drops out.
    /// </summary>
    [HttpPut("suggested")]
    public async Task<IActionResult> ReorderSuggested([FromBody] List<int> ids)
    {
        var order = ids.Distinct().Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
        var touched = await db.Restaurants
            .Where(r => r.SuggestedOrder != null || order.Keys.Contains(r.Id))
            .ToListAsync();
        foreach (var r in touched)
            r.SuggestedOrder = order.TryGetValue(r.Id, out var pos) ? pos : null;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Assigns (or clears) a shop's short-link handle on the owner's behalf. Same rules as
    /// the owner's own endpoint — the validation and the reserved list live in
    /// RestaurantsController so "valid handle" can never mean two different things.
    /// </summary>
    [HttpPut("restaurants/{id:int}/slug")]
    public async Task<IActionResult> SetSlug(int id, SetSlugRequest req)
    {
        var r = await db.Restaurants.FindAsync(id);
        if (r is null) return NotFound();

        var slug = (req.Slug ?? "").Trim().ToLowerInvariant();
        if (slug.Length == 0) { r.Slug = ""; await db.SaveChangesAsync(); return NoContent(); }

        if (!RestaurantsController.IsValidSlug(slug))
            return BadRequest(new { message = "3–40 characters: lowercase letters, digits and hyphens." });
        if (RestaurantsController.IsReservedSlug(slug))
            return BadRequest(new { message = "That address is reserved." });
        if (await db.Restaurants.AnyAsync(x => x.Slug == slug && x.Id != id))
            return BadRequest(new { message = "That address is already taken." });

        r.Slug = slug;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return BadRequest(new { message = "That address is already taken." }); }
        return NoContent();
    }

    [HttpPost("restaurants/{id:int}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        var restaurant = await db.Restaurants.FindAsync(id);
        if (restaurant is null) return NotFound();
        restaurant.IsApproved = !restaurant.IsApproved;
        await db.SaveChangesAsync();
        return Ok(new { isApproved = restaurant.IsApproved });
    }

    /// <summary>The full inventory of what a hard delete would erase — the warning's numbers.</summary>
    [HttpGet("restaurants/{id:int}/footprint")]
    public async Task<ActionResult<AdminStoreFootprintDto>> StoreFootprint(int id)
    {
        if (!await db.Restaurants.AnyAsync(r => r.Id == id)) return NotFound();
        var (categories, items, tableChats) = await catalog.StoreFootprintAsync(id);
        return new AdminStoreFootprintDto(
            Orders: await db.Orders.CountAsync(o => o.RestaurantId == id),
            Reviews: await db.Reviews.CountAsync(r => r.RestaurantId == id),
            Tables: await db.StoreTables.CountAsync(t => t.RestaurantId == id),
            Salons: await db.StoreRooms.CountAsync(r => r.RestaurantId == id),
            OpenTabs: await db.StoreTabs.CountAsync(t => t.RestaurantId == id),
            Reservations: await db.TableReservations.CountAsync(t => t.RestaurantId == id),
            Staff: await db.StoreStaff.CountAsync(s => s.RestaurantId == id),
            Bills: await db.StoreBills.CountAsync(bill => bill.RestaurantId == id),
            MenuCategories: categories,
            MenuItems: items,
            TableChats: tableChats,
            StoreCustomers: await db.StoreCustomers.CountAsync(c => c.RestaurantId == id));
    }

    /// <summary>
    /// HARD delete: the business and everything it owns is erased — menu (SQL and
    /// Mongo), floor plan, tabs, orders with their chats, reviews, bills, staff.
    /// The owner's ACCOUNT survives: it may own other stores or be a customer.
    /// </summary>
    [HttpDelete("restaurants/{id:int}")]
    public async Task<IActionResult> DeleteRestaurant(int id)
    {
        var restaurant = await db.Restaurants.FindAsync(id);
        if (restaurant is null) return NotFound();

        // Orders first (their FK forbids cascading) — items/events/reviews ride along.
        var orderIds = await db.Orders.Where(o => o.RestaurantId == id).Select(o => o.Id).ToListAsync();
        await db.Orders.Where(o => o.RestaurantId == id).ExecuteDeleteAsync();

        // The POS world, in FK-safe order.
        await db.StoreTabLines.Where(l => db.StoreTabs.Any(t => t.Id == l.StoreTabId && t.RestaurantId == id)).ExecuteDeleteAsync();
        await db.StoreTabs.Where(t => t.RestaurantId == id).ExecuteDeleteAsync();
        await db.TableReservations.Where(t => t.RestaurantId == id).ExecuteDeleteAsync();
        await db.StoreTables.Where(t => t.RestaurantId == id).ExecuteDeleteAsync();
        await db.StoreRooms.Where(r => r.RestaurantId == id).ExecuteDeleteAsync();
        await db.StoreCustomers.Where(c => c.RestaurantId == id).ExecuteDeleteAsync();
        await db.SalaryPayments.Where(p => p.RestaurantId == id).ExecuteDeleteAsync();
        await db.StoreStaff.Where(s => s.RestaurantId == id).ExecuteDeleteAsync();
        await db.StoreBills.Where(bill => bill.RestaurantId == id).ExecuteDeleteAsync();
        await db.StoreMembers.Where(m => m.RestaurantId == id).ExecuteDeleteAsync();
        await db.ReceiptDesigns.Where(r => r.RestaurantId == id).ExecuteDeleteAsync();

        // Everything else that references the store without a cascade.
        await db.Favorites.Where(f => f.RestaurantId == id).ExecuteDeleteAsync();
        await db.Reviews.Where(r => r.RestaurantId == id).ExecuteDeleteAsync();
        await db.MenuItems.Where(i => i.RestaurantId == id).ExecuteDeleteAsync();
        await db.MenuCategories.Where(c => c.RestaurantId == id).ExecuteDeleteAsync();
        await db.RestaurantHours.Where(h => h.RestaurantId == id).ExecuteDeleteAsync();
        await db.RestaurantPhotos.Where(p => p.RestaurantId == id).ExecuteDeleteAsync();
        await db.RestaurantWords.Where(w => w.RestaurantId == id).ExecuteDeleteAsync();
        await db.StoreVisits.Where(v => v.RestaurantId == id).ExecuteDeleteAsync();

        db.Restaurants.Remove(restaurant);
        await db.SaveChangesAsync();

        // The Mongo side: menu, photos, restaurant document, table chats, order chats.
        try
        {
            await catalog.DeleteStoreAsync(id);
            await chat.DeleteForOrdersAsync(orderIds);
        }
        catch { /* SQL is the source of truth; a Mongo hiccup must not undo the delete */ }

        return NoContent();
    }

    /// <summary>
    /// Erases a store's INVOICES only — every order it owns, with the items, status
    /// events and reviews that cascade off each order, the immutable invoice-audit
    /// trail for the store, and the order chats in Mongo. The store itself and
    /// everything that is not an invoice — profile, menu/products, floor plan, tabs,
    /// staff, suppliers — is left untouched. Clears a store's billing history
    /// without re-onboarding it.
    /// </summary>
    [HttpDelete("restaurants/{id:int}/invoices")]
    public async Task<ActionResult<object>> DeleteRestaurantInvoices(int id)
    {
        if (!await db.Restaurants.AnyAsync(r => r.Id == id)) return NotFound();

        var orderIds = await db.Orders.Where(o => o.RestaurantId == id).Select(o => o.Id).ToListAsync();

        // Orders carry a DB-level cascade to their items, status events and review,
        // so one delete takes the whole invoice down. InvoiceAudit has no FK to the
        // order (it is meant to outlive a purge) — clear it explicitly so the
        // cancelled/edits reports do not point at orders that no longer exist.
        await db.Orders.Where(o => o.RestaurantId == id).ExecuteDeleteAsync();
        var deletedAudits = await db.InvoiceAudits.Where(a => a.RestaurantId == id).ExecuteDeleteAsync();

        // The Mongo side: the per-order chat threads.
        try { await chat.DeleteForOrdersAsync(orderIds); }
        catch { /* SQL is the source of truth; a Mongo hiccup must not undo the delete */ }

        return new { deletedInvoices = orderIds.Count, deletedAudits };
    }

    [HttpPost("restaurants/{id:int}/commission")]
    public async Task<IActionResult> SetCommission(int id, SetCommissionRequest req)
    {
        if (req.CommissionPercent is < 0 or > 50)
            return BadRequest(new { message = "Commission must be between 0 and 50 percent." });
        var restaurant = await db.Restaurants.FindAsync(id);
        if (restaurant is null) return NotFound();
        restaurant.CommissionPercent = req.CommissionPercent;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Products: full menu management for ANY restaurant ----------

    [HttpGet("restaurants/{id:int}/menu")]
    public async Task<ActionResult<List<MenuCategoryDto>>> Menu(int id)
    {
        if (!await db.Restaurants.AnyAsync(r => r.Id == id)) return NotFound();
        return (await db.MenuCategories
                .Where(c => c.RestaurantId == id)
                .Include(c => c.Items)
                .OrderBy(c => c.SortOrder)
                .ToListAsync())
            .Select(c => new MenuCategoryDto(c.Id, c.Name, c.SortOrder,
                c.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(i => i.ToDto()).ToList()))
            .ToList();
    }

    [HttpPost("restaurants/{id:int}/menu/categories")]
    public async Task<IActionResult> CreateCategory(int id, SaveCategoryRequest req)
    {
        if (!await db.Restaurants.AnyAsync(r => r.Id == id)) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Category name is required." });
        db.MenuCategories.Add(new MenuCategory { RestaurantId = id, Name = req.Name.Trim(), SortOrder = req.SortOrder });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("menu/categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id, SaveCategoryRequest req)
    {
        var category = await db.MenuCategories.FindAsync(id);
        if (category is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Category name is required." });
        category.Name = req.Name.Trim();
        category.SortOrder = req.SortOrder;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("menu/categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await db.MenuCategories.FindAsync(id);
        if (category is null) return NotFound();
        db.MenuCategories.Remove(category); // items cascade; past orders keep their snapshots
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("restaurants/{id:int}/menu/items")]
    public async Task<IActionResult> CreateMenuItem(int id, SaveMenuItemRequest req)
    {
        var error = await ValidateMenuItem(id, req);
        if (error is not null) return BadRequest(new { message = error });
        db.MenuItems.Add(new MenuItem
        {
            RestaurantId = id,
            CategoryId = req.CategoryId,
            Name = req.Name.Trim(),
            Description = req.Description.Trim(),
            Price = req.Price,
            ImageEmoji = string.IsNullOrWhiteSpace(req.ImageEmoji) ? "🍽️" : req.ImageEmoji.Trim(),
            IsPopular = req.IsPopular,
            IsAvailable = req.IsAvailable,
            SearchKeywords = SearchAliases.KeywordsFor(req.Name)
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("menu/items/{id:int}")]
    public async Task<IActionResult> UpdateMenuItem(int id, SaveMenuItemRequest req)
    {
        var item = await db.MenuItems.FindAsync(id);
        if (item is null) return NotFound();
        var error = await ValidateMenuItem(item.RestaurantId, req);
        if (error is not null) return BadRequest(new { message = error });
        item.CategoryId = req.CategoryId;
        item.Name = req.Name.Trim();
        item.Description = req.Description.Trim();
        item.Price = req.Price;
        item.ImageEmoji = string.IsNullOrWhiteSpace(req.ImageEmoji) ? "🍽️" : req.ImageEmoji.Trim();
        item.IsPopular = req.IsPopular;
        item.IsAvailable = req.IsAvailable;
        item.SearchKeywords = SearchAliases.KeywordsFor(item.Name);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("menu/items/{id:int}/toggle-available")]
    public async Task<IActionResult> ToggleMenuItem(int id)
    {
        var item = await db.MenuItems.FindAsync(id);
        if (item is null) return NotFound();
        item.IsAvailable = !item.IsAvailable;
        await db.SaveChangesAsync();
        return Ok(new { isAvailable = item.IsAvailable });
    }

    [HttpDelete("menu/items/{id:int}")]
    public async Task<IActionResult> DeleteMenuItem(int id)
    {
        var item = await db.MenuItems.FindAsync(id);
        if (item is null) return NotFound();
        db.MenuItems.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Copies a store's menu out of the catalog and into the SQL menu tables, which is what
    /// dish search reads. Menus written by a partner live only in Mongo, so a real shop that
    /// was onboarded through the portal is reachable by NAME but none of its dishes are —
    /// "shishlik" finds nothing even though a shop two streets away sells it. This closes
    /// that gap for one store, and is safe to run again: it replaces whatever is there.
    ///
    /// The catalog's ids are reused verbatim so a dish hit resolves straight back to the
    /// same product, which means IDENTITY_INSERT rather than plain EF adds.
    /// </summary>
    [HttpPost("restaurants/{id:int}/menu/sync-search")]
    public async Task<IActionResult> SyncMenuToSearch(int id)
    {
        if (!await db.Restaurants.AnyAsync(r => r.Id == id)) return NotFound();

        var categories = await catalog.CategoriesAsync(id);
        var items = (await catalog.OwnerMenuAsync(id))
            .SelectMany(c => c.Items.Select(i => (Category: c.Id, Item: i)))
            .Where(x => x.Item.Status == ProductStatus.Approved && !x.Item.InStoreOnly)
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync();

        // Out with the old rows first — a dish the partner has since deleted must not linger
        // in search, and clearing by store keeps this idempotent.
        await db.MenuItems.Where(i => i.RestaurantId == id).ExecuteDeleteAsync();
        await db.MenuCategories.Where(c => c.RestaurantId == id).ExecuteDeleteAsync();

        // One table at a time: SQL Server permits IDENTITY_INSERT on a single table per
        // session, so categories are written and committed to the context before items.
        foreach (var c in categories)
            db.MenuCategories.Add(new MenuCategory
            {
                Id = c.Id, RestaurantId = id, Name = c.Name, SortOrder = c.SortOrder,
            });
        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MenuCategories ON;");
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MenuCategories OFF;");

        foreach (var (categoryId, i) in items)
            db.MenuItems.Add(new MenuItem
            {
                Id = i.Id,
                RestaurantId = id,
                CategoryId = categoryId,
                Name = i.Name,
                Description = i.Description,
                Price = i.Price,
                IsAvailable = i.IsAvailable,
                ImageEmoji = i.ImageEmoji,
                IsPopular = i.IsPopular,
                DiscountPercent = i.DiscountPercent,
                // Every tongue the partner filled is searchable, not just the canonical name —
                // a customer typing «شیشلیک» has to land on the same dish as one typing "shishlik".
                SearchKeywords = SearchAliases.KeywordsFor(
                    string.Join(' ', new[] { i.Name }
                        .Concat(i.Names?.Values ?? Enumerable.Empty<string>()).Distinct())),
                AvailableFromMinutes = i.AvailableFromMinutes,
                AvailableToMinutes = i.AvailableToMinutes,
                AvailableDays = i.AvailableDays,
                LeadTimeDays = i.LeadTimeDays,
                Ingredients = i.Ingredients,
                Unit = i.Unit,
            });

        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MenuItems ON;");
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MenuItems OFF;");
        await tx.CommitAsync();

        return Ok(new { categories = categories.Count, items = items.Count });
    }

    private async Task<string?> ValidateMenuItem(int restaurantId, SaveMenuItemRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "Item name is required.";
        if (req.Price <= 0) return "Price must be greater than zero.";
        var owned = await db.MenuCategories.AnyAsync(c => c.Id == req.CategoryId && c.RestaurantId == restaurantId);
        return owned ? null : "Category not found on this restaurant.";
    }

    // ---------- Product moderation ----------
    // A partner's new product is saved as Pending and hidden from customers until it is
    // approved here. Editing an already approved product does not come back through.

    [HttpGet("products/pending")]
    public async Task<PendingProductPageDto> PendingProducts(int skip = 0, int take = 30)
    {
        var rows = await catalog.PendingAsync(skip, take);
        return new PendingProductPageDto(
            rows.Select(p => new PendingProductDto(
                p.Id, p.RestaurantId, p.RestaurantName, p.CategoryName,
                p.Name, p.Description, p.Price, p.ImageEmoji, p.Photo, p.SubmittedAt)).ToList(),
            await catalog.PendingTotalAsync());
    }

    [HttpGet("products/pending/count")]
    public Task<int> PendingProductCount() => catalog.PendingTotalAsync();

    [HttpPost("products/{id:int}/approve")]
    public async Task<IActionResult> ApproveProduct(int id) =>
        await catalog.ApproveAsync(id, CurrentUserId)
            ? NoContent()
            : NotFound(new { message = "That product is not waiting for review." });

    [HttpPost("products/{id:int}/reject")]
    public async Task<IActionResult> RejectProduct(int id, RejectProductRequest req) =>
        await catalog.RejectAsync(id, CurrentUserId, req.Reason)
            ? NoContent()
            : NotFound(new { message = "That product is not waiting for review." });

    /// <summary>Support action: kill a stuck or problem order at any active stage.</summary>
    [HttpPost("orders/{id:int}/cancel")]
    public async Task<IActionResult> CancelOrder(int id, CancelOrderRequest req)
    {
        var order = await db.Orders.Include(o => o.Events).FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();
        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Rejected)
            return BadRequest(new { message = "This order is already finished." });

        order.Status = OrderStatus.Cancelled;
        order.CancelReason = string.IsNullOrWhiteSpace(req.Reason)
            ? "Cancelled by MajidFood support"
            : req.Reason.Trim();
        order.Events.Add(new OrderEvent { Status = OrderStatus.Cancelled, At = DateTime.Now, By = CurrentUserName });
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Support action: push a stuck order to its next lifecycle step.</summary>
    [HttpPost("orders/{id:int}/advance")]
    public async Task<IActionResult> AdvanceOrder(int id)
    {
        var order = await db.Orders.Include(o => o.Events).Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        var next = order.Status switch
        {
            OrderStatus.Pending => OrderStatus.Accepted,
            OrderStatus.Accepted => OrderStatus.Preparing,
            OrderStatus.Preparing => OrderStatus.Ready,
            OrderStatus.Ready => OrderStatus.PickedUp,
            OrderStatus.PickedUp => OrderStatus.OnTheWay,
            OrderStatus.OnTheWay => OrderStatus.Delivered,
            _ => (OrderStatus?)null
        };
        if (next is null) return BadRequest(new { message = "This order is already finished." });

        order.Status = next.Value;
        if (next == OrderStatus.Delivered)
        {
            order.DeliveredAt = DateTime.Now;
            await StockConsumer.ApplyAsync(db, order);
        }
        order.Events.Add(new OrderEvent { Status = next.Value, At = DateTime.Now, By = $"{CurrentUserName} (support)" });
        await db.SaveChangesAsync();
        return Ok(new { status = next.Value.ToString() });
    }

    /// <summary>Support action: force a store open or closed regardless of the owner's switch.</summary>
    [HttpPost("restaurants/{id:int}/toggle-open")]
    public async Task<IActionResult> ToggleRestaurantOpen(int id)
    {
        var restaurant = await db.Restaurants.FindAsync(id);
        if (restaurant is null) return NotFound();
        restaurant.IsOpen = !restaurant.IsOpen;
        await db.SaveChangesAsync();
        return Ok(new { isOpen = restaurant.IsOpen });
    }

    // ---------- Orders ----------

    [HttpGet("orders/count")]
    public async Task<int> OrdersCount(OrderStatus? status = null, string? search = null)
    {
        var query = db.Orders.AsQueryable();
        if (status is not null) query = query.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(o =>
                o.Number.Contains(s) || o.Customer.FullName.Contains(s) || o.Restaurant.Name.Contains(s));
        }
        return await query.CountAsync();
    }

    [HttpGet("orders")]
    public async Task<List<OrderDto>> Orders(OrderStatus? status = null, string? search = null, int skip = 0, int take = 50)
    {
        var query = db.Orders
            .Include(o => o.Customer).Include(o => o.Restaurant).Include(o => o.Driver)
            .Include(o => o.Items).Include(o => o.Events).Include(o => o.Review)
            .AsQueryable();

        if (status is not null) query = query.Where(o => o.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(o =>
                o.Number.Contains(s) || o.Customer.FullName.Contains(s) || o.Restaurant.Name.Contains(s));
        }

        return (await query.OrderByDescending(o => o.PlacedAt)
                .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToListAsync())
            .Select(o => o.ToDto()).ToList();
    }

    // ---------- Driver document verification ----------

    [HttpGet("driver-verifications/count")]
    public async Task<int> DriverVerificationsCount(DriverVerificationStatus status = DriverVerificationStatus.Pending)
        => await db.DriverProfiles.CountAsync(d => d.Verification == status);

    /// <summary>Submissions for one tab (Pending / Approved / Rejected), newest first.</summary>
    [HttpGet("driver-verifications")]
    public async Task<List<AdminDriverDocsDto>> DriverVerifications(
        DriverVerificationStatus status = DriverVerificationStatus.Pending, int skip = 0, int take = 30)
    {
        return await db.DriverProfiles
            .Where(d => d.Verification == status)
            .OrderByDescending(d => d.DocsSubmittedAt ?? DateTime.MinValue).ThenByDescending(d => d.Id)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 60))
            .Select(d => new AdminDriverDocsDto(
                d.UserId, d.User.FullName, d.User.Email, d.User.Phone, d.VehicleType,
                d.Verification, d.VerificationReason, d.DocsSubmittedAt,
                d.IdCardFront, d.IdCardBack, d.LicenseFront, d.LicenseBack))
            .ToListAsync();
    }

    [HttpPost("driver-verifications/{userId:int}/approve")]
    public async Task<IActionResult> ApproveDriverDocs(int userId)
    {
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == userId);
        if (profile is null) return NotFound();
        profile.Verification = DriverVerificationStatus.Approved;
        profile.VerificationReason = null;
        await db.SaveChangesAsync();
        return Ok(new { status = profile.Verification.ToString() });
    }

    [HttpPost("driver-verifications/{userId:int}/reject")]
    public async Task<IActionResult> RejectDriverDocs(int userId, RejectDriverDocsRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "A rejection reason is required." });

        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == userId);
        if (profile is null) return NotFound();
        profile.Verification = DriverVerificationStatus.Rejected;
        profile.VerificationReason = req.Reason.Trim();
        profile.IsOnline = false;
        await db.SaveChangesAsync();
        return Ok(new { status = profile.Verification.ToString() });
    }
}
