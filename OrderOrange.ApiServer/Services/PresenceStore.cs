using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The last API heartbeat per user: when, from which address, from which app. SQL keeps the
/// bare LastSeenAt stamp; this is the "where from" beside it, so the admin board can say
/// "the app is open on 5.x.x.x" even when nobody has opened a page in an hour.
/// </summary>
public sealed class PresenceStore
{
    public sealed class BeatDoc
    {
        [BsonId] public int UserId { get; set; }
        public DateTime BeatAt { get; set; }
        public string Ip { get; set; } = "";
        public string App { get; set; } = "";
    }

    private readonly IMongoCollection<BeatDoc> _beats;

    public PresenceStore(IMongoDatabase database)
    {
        _beats = database.GetCollection<BeatDoc>("presence");
    }

    public Task BeatAsync(int userId, string ip, string app) =>
        _beats.ReplaceOneAsync(b => b.UserId == userId,
            new BeatDoc { UserId = userId, BeatAt = DateTime.Now, Ip = ip, App = app },
            new ReplaceOptions { IsUpsert = true });

    public async Task<Dictionary<int, BeatDoc>> ForAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.ToList();
        if (ids.Count == 0) return [];
        var rows = await _beats.Find(b => ids.Contains(b.UserId)).ToListAsync();
        return rows.ToDictionary(b => b.UserId);
    }
}
