using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The clock behind planned Instagram posts. Once a minute it asks the queue what is due,
/// and publishes each one with its own store's token. A post that fails is tried twice
/// more, five minutes apart, before it is called failed — Instagram is sometimes simply
/// busy, and a plan should not die of one bad minute.
/// </summary>
public sealed class InstagramScheduler(
    IServiceScopeFactory scopes,
    InstagramQueueStore queue,
    InstagramStore accounts,
    InstagramGraph graph,
    IConfiguration config,
    ILogger<InstagramScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // Nothing is due in the first seconds of a restart; let the app finish starting.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stopping); } catch (OperationCanceledException) { return; }

        while (!stopping.IsCancellationRequested)
        {
            try { await TickAsync(stopping); }
            catch (Exception ex) { log.LogError(ex, "Instagram scheduler tick failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stopping); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task TickAsync(CancellationToken stopping)
    {
        var due = await queue.DueAsync(DateTime.UtcNow);
        if (due.Count == 0) return;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogStore>();

        foreach (var post in due)
        {
            if (stopping.IsCancellationRequested) return;
            try
            {
                var account = await accounts.GetAsync(post.StoreId)
                    ?? throw new InvalidOperationException("The store is no longer connected to Instagram.");
                var imageUrl = await ImageUrlAsync(db, catalog, post)
                    ?? throw new InvalidOperationException("The picture for this post is gone.");

                var mediaId = await graph.PublishImageAsync(account.IgUserId, account.IgToken, imageUrl, post.Caption);
                var permalink = await graph.PermalinkAsync(mediaId, account.IgToken);
                await queue.MarkPostedAsync(post.Id, permalink);
                await accounts.RecordPostAsync(post.StoreId, permalink);
                log.LogInformation("Scheduled post {Post} published for store {Store}", post.Id, post.StoreId);
            }
            catch (Exception ex)
            {
                var attempts = post.Attempts + 1;
                await queue.MarkFailedAsync(post.Id, ex.Message, attempts);
                log.LogWarning(ex, "Scheduled post {Post} attempt {Attempt} failed for store {Store}", post.Id, attempts, post.StoreId);
            }
        }
    }

    private async Task<string?> ImageUrlAsync(AppDbContext db, CatalogStore catalog, ScheduledPostDoc post)
    {
        if (post.StorePhotoId is { } photoId)
        {
            var data = await db.RestaurantPhotos.Where(p => p.Id == photoId).Select(p => p.Data).FirstOrDefaultAsync();
            return data is { Length: > 0 } ? MediaLinks.Banner(config, photoId, data) : null;
        }
        if (post.MenuItemId is { } itemId)
        {
            var item = await catalog.ApprovedItemAsync(itemId);
            return item is null ? null : MediaLinks.Dish(config, itemId, item.PhotoData);
        }
        return null;
    }
}
