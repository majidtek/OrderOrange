using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Keeps the SQL MenuItems/MenuCategories mirror in step with the Mongo catalog. Search,
/// the customer assistant and the sitemap all read the SQL side; the partner app writes
/// the Mongo side — so a shop that onboarded itself never showed up in any of them until
/// an administrator ran the sync by hand (Majed 2026-10-15: "update AI and SEO for new
/// restaurants and businesses", pointing at /p/5000256 with a full menu and zero
/// searchable dishes).
///
/// Three triggers: every catalog write (debounced per store), a reconcile at startup, and a
/// reconcile every six hours — the last two catch anything written while the API was down.
/// </summary>
public sealed class MenuSearchMirror(IServiceScopeFactory scopes, CatalogStore catalog, ILogger<MenuSearchMirror> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<int, DateTime> _due = new();
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(5);

    /// <summary>A store's catalog changed — mirror it shortly (several edits collapse into one run).</summary>
    public void Touch(int restaurantId)
    {
        if (restaurantId > 0) _due[restaurantId] = DateTime.UtcNow + Debounce;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextReconcile = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                foreach (var (id, at) in _due.ToArray())
                {
                    if (at > now || !_due.TryRemove(id, out _)) continue;
                    await SyncAsync(id, stoppingToken);
                }
                if (now >= nextReconcile)
                {
                    nextReconcile = now + TimeSpan.FromHours(6);
                    await ReconcileAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Menu search mirror loop failed."); }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    /// <summary>Every store whose SQL mirror disagrees with its Mongo menu gets re-mirrored.</summary>
    public async Task<int> ReconcileAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mongoCounts = await catalog.ApprovedItemCountsAsync();
        var sqlCounts = await db.MenuItems.AsNoTracking()
            .GroupBy(m => m.RestaurantId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var real = await db.Restaurants.AsNoTracking().Where(r => r.Id >= 5_000_200).Select(r => r.Id).ToListAsync(ct);

        var fixedCount = 0;
        foreach (var id in real)
        {
            var mongo = mongoCounts.GetValueOrDefault(id);
            var sql = sqlCounts.GetValueOrDefault(id);
            if (mongo == sql) continue;
            await SyncAsync(id, ct);
            fixedCount++;
        }
        if (fixedCount > 0) logger.LogInformation("Menu search mirror: re-mirrored {Count} stores.", fixedCount);
        return fixedCount;
    }

    /// <summary>Rewrites one store's SQL mirror from its Mongo menu. Idempotent.</summary>
    public async Task<(int Categories, int Items)> SyncAsync(int restaurantId, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            return await SyncAsync(db, restaurantId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Menu search mirror failed for store {Store}.", restaurantId);
            return (0, 0);
        }
    }

    public async Task<(int Categories, int Items)> SyncAsync(AppDbContext db, int restaurantId, CancellationToken ct = default)
    {
        var categories = await catalog.CategoriesAsync(restaurantId);
        var items = (await catalog.OwnerMenuAsync(restaurantId))
            .SelectMany(c => c.Items.Select(i => (Category: c.Id, Item: i)))
            .Where(x => x.Item.Status == ProductStatus.Approved && !x.Item.InStoreOnly)
            .ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Out with the old rows first — a dish the partner has since deleted must not linger
        // in search, and clearing by store keeps this idempotent.
        await db.MenuItems.Where(i => i.RestaurantId == restaurantId).ExecuteDeleteAsync(ct);
        await db.MenuCategories.Where(c => c.RestaurantId == restaurantId).ExecuteDeleteAsync(ct);

        // One table at a time: SQL Server permits IDENTITY_INSERT on a single table per
        // session, so categories are written and committed to the context before items.
        foreach (var c in categories)
            db.MenuCategories.Add(new MenuCategory { Id = c.Id, RestaurantId = restaurantId, Name = c.Name, SortOrder = c.SortOrder });
        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MenuCategories ON;", ct);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MenuCategories OFF;", ct);

        foreach (var (categoryId, i) in items)
            db.MenuItems.Add(new MenuItem
            {
                Id = i.Id,
                RestaurantId = restaurantId,
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
                    string.Join(' ', new[] { i.Name }.Concat(i.Names?.Values ?? Enumerable.Empty<string>()).Distinct())),
                AvailableFromMinutes = i.AvailableFromMinutes,
                AvailableToMinutes = i.AvailableToMinutes,
                AvailableDays = i.AvailableDays,
                LeadTimeDays = i.LeadTimeDays,
                Ingredients = i.Ingredients,
                Unit = i.Unit,
            });

        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MenuItems ON;", ct);
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET IDENTITY_INSERT MenuItems OFF;", ct);
        await tx.CommitAsync(ct);
        return (categories.Count, items.Count);
    }
}
