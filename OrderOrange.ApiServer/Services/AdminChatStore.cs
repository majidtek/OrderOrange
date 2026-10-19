using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>One message in a store's chat with the OrderOrange team.</summary>
public sealed class AdminChatDoc
{
    [BsonId] public int Id { get; set; }

    /// <summary>The store this conversation belongs to — one thread per store.</summary>
    public int RestaurantId { get; set; }

    /// <summary>True when the OrderOrange team wrote it; false when someone from the store did.</summary>
    public bool FromAdmin { get; set; }
    public int FromUserId { get; set; }
    public string FromName { get; set; } = "";

    public string Text { get; set; } = "";
    public string? Audio { get; set; }
    public string? Image { get; set; }
    public string? File { get; set; }
    public string? FileName { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime At { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime? EditedAt { get; set; }
    public bool Deleted { get; set; }

    /// <summary>Bumped on edit/delete so polls can pick up changes to OLD messages.</summary>
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime UpdatedAt { get; set; }

    /// <summary>The other side has opened the thread since this arrived.</summary>
    public bool Read { get; set; }

    public int? ReplyToId { get; set; }
    public string? ReplyPreview { get; set; }
}

/// <summary>
/// The admin ↔ partner chat (Majed 2026-10-14: "I need to chat with partner as admin.
/// They can send me message and photo … and voice"). One thread per store: the store's
/// owner and team on one side, any administrator on the other. Integer ids from the
/// counters collection so afterId polling works, exactly like the team chat.
/// </summary>
public class AdminChatStore
{
    private readonly IMongoCollection<AdminChatDoc> _messages;
    private readonly IMongoCollection<BsonDocument> _counters;

    public AdminChatStore(IMongoDatabase database)
    {
        _messages = database.GetCollection<AdminChatDoc>("adminChat");
        _counters = database.GetCollection<BsonDocument>("counters");
        _messages.Indexes.CreateOne(new CreateIndexModel<AdminChatDoc>(
            Builders<AdminChatDoc>.IndexKeys.Ascending(m => m.RestaurantId).Descending(m => m.Id)));
    }

    private static FilterDefinition<AdminChatDoc> Thread(int rid) =>
        Builders<AdminChatDoc>.Filter.Eq(m => m.RestaurantId, rid);

    public async Task<AdminChatDoc> AddAsync(int restaurantId, bool fromAdmin, int fromId, string fromName, string text,
        string? audio = null, string? image = null, int? replyToId = null, string? file = null, string? fileName = null)
    {
        // The quote is frozen at send time, and only from this same thread.
        string? replyPreview = null;
        if (replyToId is { } qid)
        {
            var quoted = await _messages.Find(Thread(restaurantId) &
                Builders<AdminChatDoc>.Filter.Eq(m => m.Id, qid)).FirstOrDefaultAsync();
            if (quoted is not null)
                replyPreview = $"{quoted.FromName}: {Preview(quoted, 60)}";
        }

        var seq = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", "adminChat"),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });

        var message = new AdminChatDoc
        {
            Id = seq["seq"].ToInt32(),
            RestaurantId = restaurantId,
            FromAdmin = fromAdmin,
            FromUserId = fromId,
            FromName = fromName,
            Text = text,
            Audio = audio,
            Image = image,
            File = file,
            FileName = fileName,
            At = DateTime.Now,
            UpdatedAt = DateTime.Now,
            ReplyToId = replyToId,
            ReplyPreview = replyPreview,
        };
        await _messages.InsertOneAsync(message);
        return message;
    }

    /// <summary>A short line for previews and quotes.</summary>
    public static string Preview(AdminChatDoc m, int max = 80) =>
        m.Deleted ? "🚫"
        : !string.IsNullOrWhiteSpace(m.Text) ? (m.Text.Length > max ? m.Text[..max] + "…" : m.Text)
        : m.Audio is not null ? "🎤"
        : m.Image is not null ? "📷"
        : m.File is not null ? "📄 " + m.FileName
        : "";

    /// <summary>First load (afterId 0) is the latest page; after that only what is newer.</summary>
    public async Task<List<AdminChatDoc>> ThreadAsync(int restaurantId, int afterId, int take = 100)
    {
        if (afterId > 0)
            return await _messages.Find(Thread(restaurantId) & Builders<AdminChatDoc>.Filter.Gt(m => m.Id, afterId))
                .SortBy(m => m.Id).Limit(take).ToListAsync();
        var page = await _messages.Find(Thread(restaurantId)).SortByDescending(m => m.Id).Limit(take).ToListAsync();
        page.Reverse();
        return page;
    }

    public Task<List<AdminChatDoc>> ChangedAsync(int restaurantId, DateTime since, int maxId) =>
        _messages.Find(Thread(restaurantId) &
            Builders<AdminChatDoc>.Filter.Where(m => m.UpdatedAt > since && m.Id <= maxId))
            .SortBy(m => m.Id).Limit(100).ToListAsync();

    /// <summary>Opening the thread marks everything the OTHER side wrote as read.</summary>
    public Task MarkReadAsync(int restaurantId, bool readerIsAdmin) =>
        _messages.UpdateManyAsync(
            Builders<AdminChatDoc>.Filter.Where(m => m.RestaurantId == restaurantId && m.FromAdmin != readerIsAdmin && !m.Read),
            Builders<AdminChatDoc>.Update.Set(m => m.Read, true));

    /// <summary>The store's badge: OrderOrange-team messages it has not opened.</summary>
    public async Task<int> UnreadForStoreAsync(int restaurantId) =>
        (int)await _messages.CountDocumentsAsync(m => m.RestaurantId == restaurantId && m.FromAdmin && !m.Read && !m.Deleted);

    /// <summary>The admin badge: store messages no administrator has opened, across all stores.</summary>
    public async Task<int> UnreadForAdminAsync() =>
        (int)await _messages.CountDocumentsAsync(m => !m.FromAdmin && !m.Read && !m.Deleted);

    /// <summary>The newest unread admin message for a store — for the partner bell.</summary>
    public Task<AdminChatDoc?> LatestUnreadForStoreAsync(int restaurantId) =>
        _messages.Find(m => m.RestaurantId == restaurantId && m.FromAdmin && !m.Read && !m.Deleted)
            .SortByDescending(m => m.Id).Limit(1).FirstOrDefaultAsync()!;

    /// <summary>The admin inbox: one row per store that has a conversation, newest first.</summary>
    public async Task<List<(int RestaurantId, AdminChatDoc Last, int Unread)>> InboxAsync(int take = 200)
    {
        var recent = await _messages.Find(FilterDefinition<AdminChatDoc>.Empty)
            .SortByDescending(m => m.Id).Limit(5000).ToListAsync();
        return recent.GroupBy(m => m.RestaurantId)
            .Select(g => (g.Key, g.First(), g.Count(m => !m.FromAdmin && !m.Read && !m.Deleted)))
            .Take(take)
            .ToList();
    }

    public Task<AdminChatDoc?> EditAsync(int restaurantId, int messageId, bool fromAdmin, int fromId, string text) =>
        _messages.FindOneAndUpdateAsync<AdminChatDoc>(
            Builders<AdminChatDoc>.Filter.Where(m =>
                m.Id == messageId && m.RestaurantId == restaurantId && m.FromAdmin == fromAdmin && m.FromUserId == fromId &&
                !m.Deleted && m.Audio == null && m.Image == null && m.File == null),
            Builders<AdminChatDoc>.Update
                .Set(m => m.Text, text).Set(m => m.EditedAt, DateTime.Now).Set(m => m.UpdatedAt, DateTime.Now),
            new FindOneAndUpdateOptions<AdminChatDoc, AdminChatDoc> { ReturnDocument = ReturnDocument.After })!;

    public Task<AdminChatDoc?> DeleteAsync(int restaurantId, int messageId, bool fromAdmin, int fromId) =>
        _messages.FindOneAndUpdateAsync<AdminChatDoc>(
            Builders<AdminChatDoc>.Filter.Where(m =>
                m.Id == messageId && m.RestaurantId == restaurantId && m.FromAdmin == fromAdmin && m.FromUserId == fromId),
            Builders<AdminChatDoc>.Update
                .Set(m => m.Deleted, true).Set(m => m.Text, "")
                .Set(m => m.Audio, (string?)null).Set(m => m.Image, (string?)null)
                .Set(m => m.File, (string?)null).Set(m => m.FileName, (string?)null)
                .Set(m => m.UpdatedAt, DateTime.Now),
            new FindOneAndUpdateOptions<AdminChatDoc, AdminChatDoc> { ReturnDocument = ReturnDocument.After })!;
}
