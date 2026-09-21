using OrderOrange.ApiServer.Data;
using OrderOrange.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Computes the admin dashboard aggregates. With ~750k invoices and 5M stores this
/// costs seconds, so the result lives in a 60s memory cache and a background loop
/// keeps it warm — the admin lands on an instant dashboard right after login.
/// </summary>
public static class AdminDashboardService
{
    public const string CacheKey = "admin.dashboard";
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(75);

    public static async Task<AdminDashboardDto> ComputeAsync(AppDbContext db)
    {
        if (db.Database.IsRelational()) db.Database.SetCommandTimeout(180);
        var today = DateTime.Today;

        // "Billable" = anything not killed before the kitchen started.
        var billable = db.Orders.Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);

        var todayOrders = await db.Orders.CountAsync(o => o.PlacedAt >= today);

        var todayAgg = await billable.Where(o => o.PlacedAt >= today)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(o => o.Total),
                Commission = g.Sum(o => o.Subtotal * o.Restaurant.CommissionPercent / 100m) })
            .FirstOrDefaultAsync();

        // All-time money WITHOUT the 5M-row commission join: revenue is a plain SUM,
        // commission uses the tiny "distinct commission%" grouping instead.
        var allAgg = await billable
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(o => o.Total), Subtotal = g.Sum(o => o.Subtotal) })
            .FirstOrDefaultAsync();
        var commissionByPct = await billable
            .GroupBy(o => o.Restaurant.CommissionPercent)
            .Select(g => new { Pct = g.Key, Subtotal = g.Sum(o => o.Subtotal) })
            .ToListAsync();
        var allCommission = commissionByPct.Sum(x => x.Subtotal * x.Pct / 100m);

        var activeOrders = await db.Orders.CountAsync(o =>
            o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);

        var ordersByStatus = await db.Orders.GroupBy(o => o.Status)
            .Select(g => new StatusCountDto(g.Key, g.Count()))
            .ToListAsync();

        // 14-day trend for the dashboard chart — zero-filled so gaps show as flat days.
        var since = today.AddDays(-13);
        var trendRaw = await billable.Where(o => o.PlacedAt >= since)
            .GroupBy(o => o.PlacedAt.Date)
            .Select(g => new { g.Key, Count = g.Count(), Revenue = g.Sum(o => o.Total) })
            .ToListAsync();
        var trend = Enumerable.Range(0, 14).Select(i =>
        {
            var day = since.AddDays(i);
            var hit = trendRaw.FirstOrDefault(t => t.Key == day);
            return new DayStatDto(day, hit?.Count ?? 0, hit?.Revenue ?? 0m);
        }).ToList();

        // Top stores: group by ID ONLY (covered index scan), then fetch the 5 names —
        // grouping through the 5M-store join was the dashboard's 40-second killer.
        var topRaw = await billable
            .GroupBy(o => o.RestaurantId)
            .Select(g => new { Id = g.Key, Orders = g.Count(), Revenue = g.Sum(o => o.Total) })
            .OrderByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync();
        var topIds = topRaw.Select(t => t.Id).ToList();
        var topStores = await db.Restaurants.Where(r => topIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name, r.LogoEmoji })
            .ToDictionaryAsync(r => r.Id);
        var topRatings = await db.Reviews.Where(r => topIds.Contains(r.RestaurantId))
            .GroupBy(r => r.RestaurantId)
            .Select(g => new { g.Key, Avg = g.Average(x => (double)x.RestaurantRating) })
            .ToDictionaryAsync(x => x.Key, x => Math.Round(x.Avg, 1));

        return new AdminDashboardDto(
            todayOrders,
            todayAgg?.Total ?? 0m,
            (todayAgg?.Commission ?? 0m) + Pricing.ServiceFee * (todayAgg?.Count ?? 0),
            activeOrders,
            await db.Users.CountAsync(u => u.Role == UserRole.Customer),
            await db.Restaurants.CountAsync(r => r.IsApproved),
            await db.Restaurants.CountAsync(r => !r.IsApproved),
            await db.DriverProfiles.CountAsync(d => d.IsOnline),
            await db.DriverProfiles.CountAsync(),
            await db.Orders.CountAsync(),
            allAgg?.Total ?? 0m,
            allCommission + Pricing.ServiceFee * (allAgg?.Count ?? 0),
            ordersByStatus,
            topRaw.Select(t => new TopRestaurantDto(t.Id,
                topStores.GetValueOrDefault(t.Id)?.Name ?? $"#{t.Id}",
                topStores.GetValueOrDefault(t.Id)?.LogoEmoji ?? "🍽️",
                t.Orders, t.Revenue, topRatings.GetValueOrDefault(t.Id))).ToList(),
            trend);
    }

    /// <summary>Background warmer: recompute just before the cache would expire.</summary>
    public static void StartWarmer(IServiceProvider services, ILogger logger)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var scope = services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
                    var dto = await ComputeAsync(db);
                    cache.Set(CacheKey, dto, CacheFor);
                    // Keep the search vocabulary warm too — first search after a
                    // deploy shouldn't pay the 5M-name sampling cost.
                    await SearchVocabCache.GetAsync(db);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Dashboard warm-up failed — will retry.");
                }
                await Task.Delay(TimeSpan.FromSeconds(60));
            }
        });
    }
}
