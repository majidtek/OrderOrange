using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>One direct message between two teammates of the same business.</summary>
public sealed class CommunityChatDoc
{
    [BsonId] public int Id { get; set; }

    /// <summary>Which business these two people work for — the wall between teams.</summary>
    public int RestaurantId { get; set; }

    // A private thread is one pair of people: who sent it, and who it is for.
    public int FromUserId { get; set; }
    public string FromName { get; set; } = "";
    public int ToUserId { get; set; }

    public string Text { get; set; } = "";
    public string? Audio { get; set; }
    public string? Image { get; set; }
    public string? File { get; set; }
    public string? FileName { get; set; }

    public DateTime At { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool Deleted { get; set; }

    /// <summary>Bumped on edit/delete so polls can pick up changes to OLD messages.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>The recipient has opened the thread since this arrived.</summary>
    public bool Read { get; set; }

    public int? ReplyToId { get; set; }
    public string? ReplyPreview { get; set; }
}

/// <summary>
/// Direct messages between teammates: each PAIR of people in a business has its own
/// private thread. Integer ids from the counters collection so afterId polling works;
/// a message belongs to exactly two people and never leaves their store.
/// </summary>
public class CommunityChatStore
{
    private readonly IMongoCollection<CommunityChatDoc> _messages;
    private readonly IMongoCollection<BsonDocument> _counters;

    public CommunityChatStore(IMongoDatabase database)
    {
        _messages = database.GetCollection<CommunityChatDoc>("communityChat");
        _counters = database.GetCollection<BsonDocument>("counters");
        _messages.Indexes.CreateOne(new CreateIndexModel<CommunityChatDoc>(
            Builders<CommunityChatDoc>.IndexKeys
                .Ascending(m => m.RestaurantId).Descending(m => m.Id)));
    }

    /// <summary>The filter for a two-person thread — messages either way between them.</summary>
    private static FilterDefinition<CommunityChatDoc> Between(int rid, int a, int b) =>
        Builders<CommunityChatDoc>.Filter.Where(m => m.RestaurantId == rid &&
            ((m.FromUserId == a && m.ToUserId == b) || (m.FromUserId == b && m.ToUserId == a)));

    public async Task<CommunityChatDoc> AddAsync(int restaurantId, int fromId, string fromName, int toId, string text,
        string? audio = null, string? image = null, int? replyToId = null, string? file = null, string? fileName = null)
    {
        // The quote is frozen at send time, and only from this same private thread.
        string? replyPreview = null;
        if (replyToId is { } rid)
        {
            var quoted = await _messages.Find(Between(restaurantId, fromId, toId) &
                Builders<CommunityChatDoc>.Filter.Eq(m => m.Id, rid)).FirstOrDefaultAsync();
            if (quoted is not null)
            {
                var body = quoted.Deleted ? "🚫"
                    : !string.IsNullOrWhiteSpace(quoted.Text)
                        ? (quoted.Text.Length > 60 ? quoted.Text[..60] + "…" : quoted.Text)
                        : quoted.Audio is not null ? "🎤" : quoted.File is not null ? "📄 " + quoted.FileName : "📷";
                replyPreview = $"{quoted.FromName}: {body}";
            }
        }

        var updated = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", "communityChat"),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });

        var message = new CommunityChatDoc
        {
            Id = updated["seq"].ToInt32(),
            RestaurantId = restaurantId,
            FromUserId = fromId,
            FromName = fromName,
            ToUserId = toId,
            Text = text,
            Audio = audio,
            Image = image,
            At = DateTime.Now,
            UpdatedAt = DateTime.Now,
            ReplyToId = replyToId,
            ReplyPreview = replyPreview,
            File = file,
            FileName = fileName,
        };
        await _messages.InsertOneAsync(message);
        return message;
    }

    /// <summary>
    /// The two-person thread. First load (afterId 0) hands back the most recent page;
    /// from then on the poll only asks for what's newer than the last id it saw.
    /// </summary>
    public async Task<List<CommunityChatDoc>> ThreadAsync(int restaurantId, int meId, int otherId, int afterId, int take = 100)
    {
        var filter = Between(restaurantId, meId, otherId);
        if (afterId > 0)
            return await _messages.Find(filter & Builders<CommunityChatDoc>.Filter.Gt(m => m.Id, afterId))
                .SortBy(m => m.Id).Limit(take).ToListAsync();
        var page = await _messages.Find(filter).SortByDescending(m => m.Id).Limit(take).ToListAsync();
        page.Reverse();
        return page;
    }

    /// <summary>Old messages in this thread whose content changed since the given moment.</summary>
    public Task<List<CommunityChatDoc>> ChangedAsync(int restaurantId, int meId, int otherId, DateTime since, int maxId) =>
        _messages.Find(Between(restaurantId, meId, otherId) &
            Builders<CommunityChatDoc>.Filter.Where(m => m.UpdatedAt > since && m.Id <= maxId))
            .SortBy(m => m.Id).Limit(100).ToListAsync();

    /// <summary>Opening a thread marks everything the other person sent me as read.</summary>
    public Task MarkReadAsync(int restaurantId, int meId, int otherId) =>
        _messages.UpdateManyAsync(
            Builders<CommunityChatDoc>.Filter.Where(m =>
                m.RestaurantId == restaurantId && m.FromUserId == otherId && m.ToUserId == meId && !m.Read),
            Builders<CommunityChatDoc>.Update.Set(m => m.Read, true));

    /// <summary>Fresh unread DMs addressed to me — newest per sender, for the bell.</summary>
    public async Task<List<CommunityChatDoc>> RecentUnreadForAsync(int restaurantId, int meId, int limit = 12)
    {
        var fresh = await _messages
            .Find(m => m.RestaurantId == restaurantId && m.ToUserId == meId && !m.Read && !m.Deleted)
            .SortByDescending(m => m.Id).Limit(80).ToListAsync();
        return fresh.GroupBy(m => m.FromUserId).Select(g => g.First()).Take(limit).ToList();
    }

    /// <summary>Every unread message addressed to me across all my threads here.</summary>
    public async Task<int> TotalUnreadAsync(int restaurantId, int meId) =>
        (int)await _messages.CountDocumentsAsync(m =>
            m.RestaurantId == restaurantId && m.ToUserId == meId && !m.Read && !m.Deleted);

    /// <summary>How many messages from this person I have not opened yet.</summary>
    public async Task<int> UnreadFromAsync(int restaurantId, int meId, int otherId) =>
        (int)await _messages.CountDocumentsAsync(m =>
            m.RestaurantId == restaurantId && m.FromUserId == otherId && m.ToUserId == meId && !m.Read && !m.Deleted);

    /// <summary>The most recent line between me and this person — for the inbox preview.</summary>
    public Task<CommunityChatDoc?> LastBetweenAsync(int restaurantId, int meId, int otherId) =>
        _messages.Find(Between(restaurantId, meId, otherId)).SortByDescending(m => m.Id).Limit(1).FirstOrDefaultAsync()!;

    /// <summary>Rewrite your own words. Only text — a voice note is what it is.</summary>
    public async Task<CommunityChatDoc?> EditAsync(int restaurantId, int messageId, int fromId, string text)
    {
        var update = Builders<CommunityChatDoc>.Update
            .Set(m => m.Text, text)
            .Set(m => m.EditedAt, DateTime.Now)
            .Set(m => m.UpdatedAt, DateTime.Now);
        return await _messages.FindOneAndUpdateAsync<CommunityChatDoc>(
            Builders<CommunityChatDoc>.Filter.Where(m =>
                m.Id == messageId && m.RestaurantId == restaurantId && m.FromUserId == fromId && !m.Deleted &&
                m.Audio == null && m.Image == null && m.File == null),
            update,
            new FindOneAndUpdateOptions<CommunityChatDoc, CommunityChatDoc> { ReturnDocument = ReturnDocument.After });
    }

    /// <summary>Take a message back: content gone, a "deleted" stub remains.</summary>
    public async Task<CommunityChatDoc?> DeleteAsync(int restaurantId, int messageId, int fromId)
    {
        var update = Builders<CommunityChatDoc>.Update
            .Set(m => m.Deleted, true)
            .Set(m => m.Text, "")
            .Set(m => m.Audio, (string?)null)
            .Set(m => m.Image, (string?)null)
            .Set(m => m.File, (string?)null)
            .Set(m => m.FileName, (string?)null)
            .Set(m => m.UpdatedAt, DateTime.Now);
        return await _messages.FindOneAndUpdateAsync<CommunityChatDoc>(
            Builders<CommunityChatDoc>.Filter.Where(m => m.Id == messageId && m.RestaurantId == restaurantId && m.FromUserId == fromId),
            update,
            new FindOneAndUpdateOptions<CommunityChatDoc, CommunityChatDoc> { ReturnDocument = ReturnDocument.After });
    }
}
