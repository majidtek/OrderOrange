using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// One post waiting for its hour. The picture is NOT kept here: it is filed as one of the
/// store's photos when the post is scheduled, and only its id travels — a queue holding
/// base64 images would grow into the megabytes and still have to be re-hosted at publish
/// time anyway.
/// </summary>
public sealed class ScheduledPostDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public int StoreId { get; set; }
    public string Caption { get; set; } = "";
    public int? StorePhotoId { get; set; }
    public int? MenuItemId { get; set; }
    /// <summary>Always UTC. The page shows it in the store's own time.</summary>
    public DateTime DueAtUtc { get; set; }
    /// <summary>scheduled · posted · failed · cancelled</summary>
    public string Status { get; set; } = "scheduled";
    public string? Permalink { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PostedAt { get; set; }

    public ScheduledPostDto ToDto(Func<ScheduledPostDoc, string?> thumb) => new(
        Id.ToString(), Caption, DueAtUtc.ToLocalTime(), Status, thumb(this), Permalink, Error,
        PostedAt?.ToLocalTime());
}

/// <summary>The planned posts of every store, and the ones that are due right now.</summary>
public sealed class InstagramQueueStore
{
    private readonly IMongoCollection<ScheduledPostDoc> _posts;

    public InstagramQueueStore(IMongoDatabase database)
    {
        _posts = database.GetCollection<ScheduledPostDoc>("storeSocialQueue");
        // The scheduler asks the same question every minute: what is due?
        _posts.Indexes.CreateOne(new CreateIndexModel<ScheduledPostDoc>(
            Builders<ScheduledPostDoc>.IndexKeys.Ascending(p => p.Status).Ascending(p => p.DueAtUtc)));
    }

    public Task AddAsync(ScheduledPostDoc doc) => _posts.InsertOneAsync(doc);

    public Task<List<ScheduledPostDoc>> ForStoreAsync(int storeId, int take = 50) =>
        _posts.Find(p => p.StoreId == storeId)
              .SortByDescending(p => p.DueAtUtc).Limit(take).ToListAsync();

    /// <summary>Everything whose hour has come, oldest first, a handful at a time.</summary>
    public Task<List<ScheduledPostDoc>> DueAsync(DateTime nowUtc, int take = 10) =>
        _posts.Find(p => p.Status == "scheduled" && p.DueAtUtc <= nowUtc)
              .SortBy(p => p.DueAtUtc).Limit(take).ToListAsync();

    public Task<ScheduledPostDoc?> GetAsync(ObjectId id, int storeId) =>
        _posts.Find(p => p.Id == id && p.StoreId == storeId).FirstOrDefaultAsync()!;

    public Task CancelAsync(ObjectId id, int storeId) =>
        _posts.UpdateOneAsync(p => p.Id == id && p.StoreId == storeId && p.Status == "scheduled",
            Builders<ScheduledPostDoc>.Update.Set(p => p.Status, "cancelled"));

    public Task MarkPostedAsync(ObjectId id, string permalink) =>
        _posts.UpdateOneAsync(p => p.Id == id, Builders<ScheduledPostDoc>.Update
            .Set(p => p.Status, "posted").Set(p => p.Permalink, permalink)
            .Set(p => p.PostedAt, DateTime.UtcNow).Set(p => p.Error, null));

    /// <summary>
    /// A failure is not final until the third try: Instagram is occasionally busy, and a
    /// post that dies because of one slow minute would be a bad surprise.
    /// </summary>
    public Task MarkFailedAsync(ObjectId id, string error, int attempts) =>
        _posts.UpdateOneAsync(p => p.Id == id, Builders<ScheduledPostDoc>.Update
            .Set(p => p.Error, error)
            .Set(p => p.Attempts, attempts)
            .Set(p => p.Status, attempts >= 3 ? "failed" : "scheduled")
            .Set(p => p.DueAtUtc, DateTime.UtcNow.AddMinutes(5)));
}
