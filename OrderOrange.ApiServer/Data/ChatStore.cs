using OrderOrange.Shared;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>One chat message as it lives in MongoDB.</summary>
public sealed class ChatDoc
{
    /// <summary>
    /// A plain increasing integer rather than an ObjectId. The clients poll with
    /// "give me everything after id N", so the id has to be orderable and comparable
    /// by the same rules the SQL identity column used.
    /// </summary>
    [BsonId] public int Id { get; set; }

    public int OrderId { get; set; }
    public int SenderUserId { get; set; }
    public string SenderName { get; set; } = "";
    public UserRole SenderRole { get; set; }
    public string? Text { get; set; }
    public string? AttachmentData { get; set; }
    public string? AttachmentType { get; set; }
    public string? FileName { get; set; }
    public DateTime At { get; set; }

    public ChatMessageDto ToDto() => new(
        Id, OrderId, SenderUserId, SenderName, SenderRole,
        Text, AttachmentData, AttachmentType, FileName, At);
}

/// <summary>Per-order conversation totals for the admin monitor.</summary>
public sealed record ChatSummary(int OrderId, int Count, DateTime LastAt, ChatDoc? Last);

/// <summary>
/// Order chat storage, backed entirely by MongoDB — no SQL table is involved.
///
/// Chat is append-only, high-volume and carries large base64 attachments, which is a
/// poor fit for the relational schema the rest of the app uses; keeping it in Mongo
/// leaves the SQL database to the transactional data (orders, menus, payments).
/// </summary>
public sealed class ChatStore
{
    private readonly IMongoCollection<ChatDoc> _messages;
    private readonly IMongoCollection<BsonDocument> _counters;

    public ChatStore(IMongoDatabase database)
    {
        _messages = database.GetCollection<ChatDoc>("chatMessages");
        _counters = database.GetCollection<BsonDocument>("counters");

        // Every read is "this order, after this id", so that is the index.
        _messages.Indexes.CreateOne(new CreateIndexModel<ChatDoc>(
            Builders<ChatDoc>.IndexKeys.Ascending(m => m.OrderId).Ascending(m => m.Id)));
        _messages.Indexes.CreateOne(new CreateIndexModel<ChatDoc>(
            Builders<ChatDoc>.IndexKeys.Descending(m => m.At)));
    }

    /// <summary>Messages for one order newer than <paramref name="afterId"/>, oldest first.</summary>
    public Task<List<ChatDoc>> MessagesAsync(int orderId, int afterId, int take = 100) =>
        _messages.Find(m => m.OrderId == orderId && m.Id > afterId)
            .SortBy(m => m.Id)
            .Limit(take)
            .ToListAsync();

    /// <summary>Every message of one order, oldest first — the admin transcript view.</summary>
    public Task<List<ChatDoc>> TranscriptAsync(int orderId) =>
        _messages.Find(m => m.OrderId == orderId).SortBy(m => m.At).ToListAsync();

    public Task<long> CountForOrderAsync(int orderId) =>
        _messages.CountDocumentsAsync(m => m.OrderId == orderId);

    public async Task<ChatDoc> AddAsync(ChatDoc message)
    {
        message.Id = await NextIdAsync();
        await _messages.InsertOneAsync(message);
        return message;
    }

    /// <summary>
    /// Conversations ordered by most recent activity, with the message count and the
    /// last message of each. Grouped inside Mongo so the monitor never pulls whole
    /// transcripts across the wire.
    /// </summary>
    public async Task<List<ChatSummary>> SummariesAsync(IReadOnlyCollection<int>? onlyOrders, int skip, int take)
    {
        var filter = onlyOrders is { Count: > 0 }
            ? Builders<ChatDoc>.Filter.In(m => m.OrderId, onlyOrders)
            : Builders<ChatDoc>.Filter.Empty;

        var rows = await _messages.Aggregate()
            .Match(filter)
            .SortByDescending(m => m.At)
            .Group(m => m.OrderId, g => new
            {
                OrderId = g.Key,
                Count = g.Count(),
                LastAt = g.Max(m => m.At),
                Last = g.First(),          // the sort above makes "first" the newest
            })
            .SortByDescending(x => x.LastAt)
            .Skip(Math.Max(0, skip))
            .Limit(Math.Clamp(take, 1, 100))
            .ToListAsync();

        return rows.Select(r => new ChatSummary(r.OrderId, r.Count, r.LastAt, r.Last)).ToList();
    }

    /// <summary>Order ids that have any chat at all — lets the caller resolve names in SQL.</summary>
    public async Task<List<int>> OrderIdsAsync() =>
        (await _messages.DistinctAsync(m => m.OrderId, Builders<ChatDoc>.Filter.Empty))
        .ToList();

    /// <summary>The classic Mongo sequence: one atomic find-and-increment per insert.</summary>
    private async Task<int> NextIdAsync()
    {
        var updated = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", "chatMessages"),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After,
            });
        return updated["seq"].ToInt32();
    }

    /// <summary>
    /// Raises the id sequence so it never collides with ids already stored — used after
    /// importing the old SQL rows, which keep their original ids.
    /// </summary>
    public async Task EnsureSequenceAtLeastAsync(int value)
    {
        if (value <= 0) return;
        await _counters.UpdateOneAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", "chatMessages"),
                Builders<BsonDocument>.Filter.Lt("seq", value)),
            Builders<BsonDocument>.Update.Set("seq", value),
            new UpdateOptions { IsUpsert = true });
    }

    public Task<long> TotalAsync() => _messages.CountDocumentsAsync(Builders<ChatDoc>.Filter.Empty);

    public Task ImportAsync(IEnumerable<ChatDoc> docs) => _messages.InsertManyAsync(docs);

    /// <summary>Deletes whole conversations — used when their orders are hard-deleted.</summary>
    public Task DeleteForOrdersAsync(IReadOnlyCollection<int> orderIds) =>
        orderIds.Count == 0
            ? Task.CompletedTask
            : _messages.DeleteManyAsync(m => orderIds.Contains(m.OrderId));
}
