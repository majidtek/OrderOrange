using MongoDB.Bson;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Looks at the platform inbox every few minutes and tells the administrators about new
/// mail — a line on the admin bell and a Web Push, exactly like a new business. Remembers
/// the inbox's UIDNEXT in Mongo platformSettings (id "mailbox") so a restart never
/// re-announces old mail, and the first run only takes a baseline.
/// </summary>
public sealed class MailWatcher(MailboxService mail, AdminAlerts alerts, IMongoDatabase mongo, IConfiguration config, ILogger<MailWatcher> logger) : BackgroundService
{
    private IMongoCollection<BsonDocument> Settings => mongo.GetCollection<BsonDocument>("platformSettings");

    public DateTime? LastCheckAt { get; private set; }
    public string? LastError { get; private set; }
    public int Unread { get; private set; }
    public int PollMinutes => int.TryParse(config["Mail:PollMinutes"], out var m) ? m : 2;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!mail.IsConfigured || PollMinutes <= 0) return;
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LastError = ex.Message;
                logger.LogWarning(ex, "Mailbox check failed.");
            }
            await Task.Delay(TimeSpan.FromMinutes(PollMinutes), stoppingToken);
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        var (unread, uidNext) = await mail.InboxStatusAsync(ct);
        Unread = unread;
        LastCheckAt = DateTime.Now;
        LastError = null;

        var doc = await Settings.Find(Builders<BsonDocument>.Filter.Eq("_id", "mailbox")).FirstOrDefaultAsync(ct);
        var known = doc is not null && doc.TryGetValue("uidNext", out var v) ? (uint)v.ToInt64() : 0u;

        if (known > 0 && uidNext > known)
        {
            var fresh = await mail.NewSinceAsync(known, 5, ct);
            foreach (var m in fresh) alerts.NewMail(m.From, m.Subject);
            var more = (int)(uidNext - known) - fresh.Count;
            if (more > 0 && fresh.Count == 5) alerts.NewMail($"+{more} more", "new emails");
        }
        if (uidNext != known)
            await Settings.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", "mailbox"),
                Builders<BsonDocument>.Update.Set("uidNext", (long)uidNext).Set("checkedAt", DateTime.Now),
                new UpdateOptions { IsUpsert = true }, ct);
    }
}
