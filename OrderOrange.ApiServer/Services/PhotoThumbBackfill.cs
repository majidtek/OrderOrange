using OrderOrange.ApiServer.Data;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Makes the POS small photo for every product that has a photo, a little while after the API
/// starts (Majed 2026-10-22: "make small for all product images"; "image shown with very
/// delay"). Without it the first POS to open a group waited while the server shrank each photo.
/// Products whose small photo is current are skipped, so a restart costs only the reading.
/// The POS still makes any that are missing on the spot, so this is a head start, not a duty.
/// </summary>
public sealed class PhotoThumbBackfill(CatalogStore catalog, IConfiguration config, ILogger<PhotoThumbBackfill> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("PhotoThumbs:Backfill", true)) return;
        // Let the API finish starting and serve its first requests before this takes any CPU.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch { return; }

        var made = 0;
        var afterId = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var batch = await catalog.PhotoDocsAfterAsync(afterId, 50);
                if (batch.Count == 0) break;
                foreach (var doc in batch)
                {
                    afterId = doc.Id;
                    if (doc.PhotoData is null || (doc.PhotoThumb is not null && doc.PhotoThumbKey == PhotoThumbs.KeyOf(doc.PhotoData))) continue;
                    await PhotoThumbs.SmallFor(doc, catalog);
                    made++;
                    await Task.Delay(20, stoppingToken);   // gentle: a big catalogue must not starve live requests
                }
            }
            if (made > 0) log.LogInformation("POS small photos made for {Count} products", made);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogError(ex, "POS small photo backfill stopped after {Count}", made); }
    }
}
