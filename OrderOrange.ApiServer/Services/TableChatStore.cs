using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>One line of talk between a table's guest and the store.</summary>
public sealed class TableChatDoc
{
    [BsonId] public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int TableId { get; set; }

    /// <summary>"guest" (from the QR page) or "store" (from the partner portal).</summary>
    public string From { get; set; } = "guest";

    public string Text { get; set; } = "";

    /// <summary>A voice note as a data URL (audio/webm).</summary>
    public string? Audio { get; set; }

    /// <summary>A photo as a data URL (image/jpeg) — text, voice, or image, any one.</summary>
    public string? Image { get; set; }

    public DateTime At { get; set; }

    /// <summary>Set when the words were changed after sending.</summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>A removed message stays as a stub — both screens show "deleted".</summary>
    public bool Deleted { get; set; }

    /// <summary>Bumped on edit/delete so polls can pick up changes to OLD messages.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Reply threading: the quoted message and a frozen snippet of it.</summary>
    public int? ReplyToId { get; set; }
    public string? ReplyPreview { get; set; }

    /// <summary>An attached document as a data URL, with the name it was sent under.</summary>
    public string? File { get; set; }
    public string? FileName { get; set; }

    // Archived when the table's invoice closed: gone from the live thread, kept
    // forever in the history page. ArchiveRef carries the order number.
    public bool Archived { get; set; }
    public string? ArchiveId { get; set; }
    public string? ArchiveRef { get; set; }
    public DateTime? ArchivedAt { get; set; }
}

/// <summary>
/// Chat between whoever scanned a table's QR and the store — Mongo-backed like the
/// order chat, integer ids from the counters collection so afterId polling works.
/// </summary>
public class TableChatStore
{
    private readonly IMongoCollection<TableChatDoc> _messages;
    private readonly IMongoCollection<BsonDocument> _counters;

    public TableChatStore(IMongoDatabase database)
    {
        _messages = database.GetCollection<TableChatDoc>("tableChats");
        _counters = database.GetCollection<BsonDocument>("counters");
        _messages.Indexes.CreateOne(new CreateIndexModel<TableChatDoc>(
            Builders<TableChatDoc>.IndexKeys.Ascending(m => m.RestaurantId).Ascending(m => m.TableId).Descending(m => m.Id)));
    }

    /// <summary>Fresh guest messages for the notification bell — live threads only.</summary>
    public async Task<List<TableChatDoc>> RecentGuestAsync(int restaurantId, DateTime since, int limit = 12)
        => await _messages
            .Find(m => m.RestaurantId == restaurantId && m.From == "guest" && !m.Archived && m.At >= since)
            .SortByDescending(m => m.Id)
            .Limit(limit)
            .ToListAsync();

    public async Task<TableChatDoc> AddAsync(int restaurantId, int tableId, string from, string text, string? audio = null, string? image = null, int? replyToId = null, string? file = null, string? fileName = null)
    {
        // The quote is frozen at send time — edits to the original never rewrite it.
        string? replyPreview = null;
        if (replyToId is { } rid)
        {
            var quoted = await _messages.Find(m => m.Id == rid && m.RestaurantId == restaurantId && m.TableId == tableId)
                .FirstOrDefaultAsync();
            if (quoted is not null)
            {
                replyPreview = quoted.Deleted ? "🚫"
                    : !string.IsNullOrWhiteSpace(quoted.Text)
                        ? (quoted.Text.Length > 60 ? quoted.Text[..60] + "…" : quoted.Text)
                        : quoted.Audio is not null ? "🎤" : quoted.File is not null ? "📄 " + quoted.FileName : "📷";
            }
        }

        var updated = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", "tableChats"),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });

        var message = new TableChatDoc
        {
            Id = updated["seq"].ToInt32(),
            RestaurantId = restaurantId,
            TableId = tableId,
            From = from,
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

    public Task<List<TableChatDoc>> MessagesAsync(int restaurantId, int tableId, int afterId, int take = 100) =>
        _messages.Find(m => m.RestaurantId == restaurantId && m.TableId == tableId && m.Id > afterId && !m.Archived)
            .SortBy(m => m.Id).Limit(take).ToListAsync();

    /// <summary>
    /// The invoice closed: the table's live chat resets by moving every message into
    /// the archive, stamped with one id so the history page can replay the session.
    /// </summary>
    public async Task<string?> ArchiveTableAsync(int restaurantId, int tableId, string? reference)
    {
        var archiveId = Guid.NewGuid().ToString("N");
        var result = await _messages.UpdateManyAsync(
            m => m.RestaurantId == restaurantId && m.TableId == tableId && !m.Archived,
            Builders<TableChatDoc>.Update
                .Set(m => m.Archived, true)
                .Set(m => m.ArchiveId, archiveId)
                .Set(m => m.ArchiveRef, reference)
                .Set(m => m.ArchivedAt, DateTime.Now)
                .Set(m => m.UpdatedAt, DateTime.Now));
        return result.ModifiedCount > 0 ? archiveId : null;
    }

    /// <summary>The history shelf: one row per archived session, newest first.</summary>
    public async Task<List<TableChatDoc>> ArchiveSessionsAsync(int restaurantId)
    {
        var archived = await _messages.Find(m => m.RestaurantId == restaurantId && m.Archived)
            .SortByDescending(m => m.Id).Limit(1000).ToListAsync();
        return archived.GroupBy(m => m.ArchiveId).Select(g => g.First()).ToList();
    }

    public Task<List<TableChatDoc>> ArchiveMessagesAsync(int restaurantId, string archiveId) =>
        _messages.Find(m => m.RestaurantId == restaurantId && m.Archived && m.ArchiveId == archiveId)
            .SortBy(m => m.Id).ToListAsync();

    public Task<long> ArchiveCountAsync(int restaurantId, string archiveId) =>
        _messages.CountDocumentsAsync(m => m.RestaurantId == restaurantId && m.Archived && m.ArchiveId == archiveId);

    /// <summary>Rewrite your own words. Only text — a voice note is what it is.</summary>
    public async Task<TableChatDoc?> EditAsync(int restaurantId, int tableId, int messageId, string from, string text)
    {
        var update = Builders<TableChatDoc>.Update
            .Set(m => m.Text, text)
            .Set(m => m.EditedAt, DateTime.Now)
            .Set(m => m.UpdatedAt, DateTime.Now);
        return await _messages.FindOneAndUpdateAsync<TableChatDoc>(
            Builders<TableChatDoc>.Filter.Where(m =>
                m.Id == messageId && m.RestaurantId == restaurantId && m.TableId == tableId &&
                m.From == from && !m.Deleted && m.Audio == null && m.Image == null),
            update,
            new FindOneAndUpdateOptions<TableChatDoc, TableChatDoc> { ReturnDocument = ReturnDocument.After });
    }

    /// <summary>Take a message back: content gone, a "deleted" stub remains.</summary>
    public async Task<TableChatDoc?> DeleteAsync(int restaurantId, int tableId, int messageId, string from)
    {
        var update = Builders<TableChatDoc>.Update
            .Set(m => m.Deleted, true)
            .Set(m => m.Text, "")
            .Set(m => m.Audio, (string?)null)
            .Set(m => m.Image, (string?)null)
            .Set(m => m.File, (string?)null)
            .Set(m => m.FileName, (string?)null)
            .Set(m => m.UpdatedAt, DateTime.Now);
        return await _messages.FindOneAndUpdateAsync<TableChatDoc>(
            Builders<TableChatDoc>.Filter.Where(m =>
                m.Id == messageId && m.RestaurantId == restaurantId && m.TableId == tableId && m.From == from),
            update,
            new FindOneAndUpdateOptions<TableChatDoc, TableChatDoc> { ReturnDocument = ReturnDocument.After });
    }

    /// <summary>Old messages whose content changed since the given moment.</summary>
    public Task<List<TableChatDoc>> ChangedAsync(int restaurantId, int tableId, DateTime since, int maxId) =>
        _messages.Find(m => m.RestaurantId == restaurantId && m.TableId == tableId &&
                            m.UpdatedAt > since && m.Id <= maxId && !m.Archived)
            .SortBy(m => m.Id).Limit(100).ToListAsync();

    // ---------- Read markers: who has seen how far, and when ----------

    /// <summary>"I have read up to message N" — one marker per side per table.</summary>
    public Task MarkReadAsync(int restaurantId, int tableId, string side, int lastId) =>
        _counters.Database.GetCollection<BsonDocument>("tableChatReads").UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", $"{restaurantId}:{tableId}:{side}"),
            Builders<BsonDocument>.Update
                .Max("lastId", lastId)
                .Set("at", DateTime.Now),
            new UpdateOptions { IsUpsert = true });

    /// <summary>How far (and when) the given side has read this table's chat.</summary>
    public async Task<(int LastId, DateTime? At)> ReadOfAsync(int restaurantId, int tableId, string side)
    {
        var doc = await _counters.Database.GetCollection<BsonDocument>("tableChatReads")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", $"{restaurantId}:{tableId}:{side}"))
            .FirstOrDefaultAsync();
        if (doc is null) return (0, null);
        return (doc.GetValue("lastId", 0).ToInt32(),
                doc.TryGetValue("at", out var at) ? at.ToUniversalTime().ToLocalTime() : null);
    }

    /// <summary>How many guest messages arrived after the store last looked.</summary>
    public async Task<(int Count, int MaxId)> UnreadAsync(int restaurantId, int afterId)
    {
        var fresh = await _messages.Find(m => m.RestaurantId == restaurantId && m.Id > afterId && m.From == "guest" && !m.Archived)
            .SortByDescending(m => m.Id).Limit(200).ToListAsync();
        return (fresh.Count, fresh.Count > 0 ? fresh[0].Id : afterId);
    }

    /// <summary>How many guest messages in ONE table's thread the store hasn't opened yet.</summary>
    public async Task<int> UnreadOfTableAsync(int restaurantId, int tableId)
    {
        var (lastId, _) = await ReadOfAsync(restaurantId, tableId, "store");
        return (int)await _messages.CountDocumentsAsync(m =>
            m.RestaurantId == restaurantId && m.TableId == tableId &&
            m.From == "guest" && m.Id > lastId && !m.Archived && !m.Deleted);
    }

    /// <summary>The store's inbox: one row per table, newest word first.</summary>
    public async Task<List<TableChatDoc>> LatestPerTableAsync(int restaurantId)
    {
        var recent = await _messages.Find(m => m.RestaurantId == restaurantId && !m.Archived)
            .SortByDescending(m => m.Id).Limit(500).ToListAsync();
        return recent.GroupBy(m => m.TableId).Select(g => g.First())
            .OrderByDescending(m => m.Id).ToList();
    }
}
