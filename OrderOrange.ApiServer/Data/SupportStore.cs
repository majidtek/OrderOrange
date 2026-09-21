using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>A file on a message: what to show. The bytes live in <see cref="TicketFileDoc"/>.</summary>
public sealed class TicketAttachmentRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Mime { get; set; } = "";
    public int Size { get; set; }
    public bool IsImage { get; set; }
}

/// <summary>One line of the conversation. Kind: message | status.</summary>
public sealed class TicketMessageDoc
{
    public int Id { get; set; }
    /// <summary>partner | support | system</summary>
    public string From { get; set; } = "";
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = "";
    public string Text { get; set; } = "";
    public List<TicketAttachmentRef> Attachments { get; set; } = [];
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime At { get; set; }
    public string Kind { get; set; } = "message";
    public string? StatusTo { get; set; }
}

public sealed class TicketDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public string Number { get; set; } = "";
    public int RestaurantId { get; set; }
    public string RestaurantName { get; set; } = "";
    public int OpenedByUserId { get; set; }
    public string OpenedByName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Category { get; set; } = "other";
    public string Priority { get; set; } = "normal";
    public string Status { get; set; } = "open";
    public string? AssignedTo { get; set; }
    // Everything else in the platform speaks server-local time; Mongo would hand back UTC.
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime CreatedAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime UpdatedAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime LastMessageAt { get; set; }
    /// <summary>partner | support — who spoke last.</summary>
    public string LastFrom { get; set; } = "partner";
    public string LastPreview { get; set; } = "";
    /// <summary>News the partner has not opened yet (support wrote or changed status).</summary>
    public bool PartnerUnread { get; set; }
    /// <summary>News support has not opened yet (partner wrote).</summary>
    public bool SupportUnread { get; set; }
    public int? Rating { get; set; }
    public string? RatingNote { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime? ResolvedAt { get; set; }
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime? ClosedAt { get; set; }
    public List<TicketMessageDoc> Messages { get; set; } = [];
}

/// <summary>Attachment bytes, one document each, so a ticket stays small to list and read.</summary>
public sealed class TicketFileDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public ObjectId TicketId { get; set; }
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string Mime { get; set; } = "";
    public int Size { get; set; }
    public string Data { get; set; } = "";
    [BsonDateTimeOptions(Kind = DateTimeKind.Local)] public DateTime At { get; set; }
}

/// <summary>
/// Support tickets live in MongoDB beside surveys and chats: a conversation that only
/// grows, with files hanging off it, and nothing in SQL that needs to join to it.
/// </summary>
public sealed class SupportStore(IMongoDatabase database)
{
    private readonly IMongoCollection<TicketDoc> _tickets =
        database.GetCollection<TicketDoc>(CollectionNames.SupportTickets);
    private readonly IMongoCollection<TicketFileDoc> _files =
        database.GetCollection<TicketFileDoc>(CollectionNames.SupportFiles);
    private readonly IMongoCollection<BsonDocument> _counters =
        database.GetCollection<BsonDocument>("counters");

    public async Task EnsureIndexesAsync()
    {
        await _tickets.Indexes.CreateOneAsync(new CreateIndexModel<TicketDoc>(
            Builders<TicketDoc>.IndexKeys.Ascending(t => t.RestaurantId).Descending(t => t.LastMessageAt)));
        await _tickets.Indexes.CreateOneAsync(new CreateIndexModel<TicketDoc>(
            Builders<TicketDoc>.IndexKeys.Ascending(t => t.Status).Descending(t => t.LastMessageAt)));
        await _files.Indexes.CreateOneAsync(new CreateIndexModel<TicketFileDoc>(
            Builders<TicketFileDoc>.IndexKeys.Ascending(f => f.TicketId)));
    }

    /// <summary>T-00001, T-00002 … one sequence for the whole platform, so support can quote it.</summary>
    public async Task<string> NextNumberAsync()
    {
        var doc = await _counters.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", "supportTickets"),
            Builders<BsonDocument>.Update.Inc("Seq", 1),
            new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });
        return $"T-{doc["Seq"].ToInt32():00000}";
    }

    public Task InsertAsync(TicketDoc ticket) => _tickets.InsertOneAsync(ticket);

    public Task ReplaceAsync(TicketDoc ticket) => _tickets.ReplaceOneAsync(t => t.Id == ticket.Id, ticket);

    /// <summary>restaurantId null = every store (the support desk).</summary>
    public Task<TicketDoc?> GetAsync(ObjectId id, int? restaurantId) =>
        (restaurantId is int rid
            ? _tickets.Find(t => t.Id == id && t.RestaurantId == rid)
            : _tickets.Find(t => t.Id == id)).FirstOrDefaultAsync()!;

    public Task MarkReadAsync(ObjectId id, bool partnerSide) =>
        _tickets.UpdateOneAsync(t => t.Id == id,
            partnerSide
                ? Builders<TicketDoc>.Update.Set(t => t.PartnerUnread, false)
                : Builders<TicketDoc>.Update.Set(t => t.SupportUnread, false));

    private static FilterDefinition<TicketDoc> StatusFilter(string status) => status switch
    {
        "open" => Builders<TicketDoc>.Filter.In(t => t.Status, ["open", "in_progress", "waiting"]),
        "in_progress" or "waiting" or "resolved" or "closed" => Builders<TicketDoc>.Filter.Eq(t => t.Status, status),
        "unread_support" => Builders<TicketDoc>.Filter.Eq(t => t.SupportUnread, true),
        "unread_partner" => Builders<TicketDoc>.Filter.Eq(t => t.PartnerUnread, true),
        _ => Builders<TicketDoc>.Filter.Empty,
    };

    public async Task<(List<TicketDoc> Items, long Total)> ListAsync(int? restaurantId, string status, string? search, int skip, int take)
    {
        var filter = StatusFilter(status);
        if (restaurantId is int rid) filter &= Builders<TicketDoc>.Filter.Eq(t => t.RestaurantId, rid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var rx = new BsonRegularExpression(System.Text.RegularExpressions.Regex.Escape(term), "i");
            filter &= Builders<TicketDoc>.Filter.Or(
                Builders<TicketDoc>.Filter.Regex(t => t.Number, rx),
                Builders<TicketDoc>.Filter.Regex(t => t.Subject, rx),
                Builders<TicketDoc>.Filter.Regex(t => t.RestaurantName, rx));
        }
        var total = await _tickets.CountDocumentsAsync(filter);
        var items = await _tickets.Find(filter)
            .Project<TicketDoc>(Builders<TicketDoc>.Projection.Exclude(t => t.Messages))
            .SortByDescending(t => t.LastMessageAt).Skip(skip).Limit(take).ToListAsync();
        return (items, total);
    }

    /// <summary>Message counts for a listing — the list projection leaves the thread out.</summary>
    public async Task<Dictionary<ObjectId, int>> MessageCountsAsync(IEnumerable<ObjectId> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0) return [];
        var rows = await _tickets.Aggregate()
            .Match(t => list.Contains(t.Id))
            .Project(t => new { t.Id, N = t.Messages.Count })
            .ToListAsync();
        return rows.ToDictionary(x => x.Id, x => x.N);
    }

    public async Task<(int Open, int InProgress, int Waiting, int Resolved, int Closed, int Unread)> SummaryAsync(int? restaurantId, bool partnerSide)
    {
        var scope = restaurantId is int rid
            ? Builders<TicketDoc>.Filter.Eq(t => t.RestaurantId, rid)
            : Builders<TicketDoc>.Filter.Empty;
        var rows = await _tickets.Aggregate().Match(scope)
            .Group(t => t.Status, g => new { Status = g.Key, N = g.Count() }).ToListAsync();
        int Of(string s) => rows.FirstOrDefault(r => r.Status == s)?.N ?? 0;
        var unread = (int)await _tickets.CountDocumentsAsync(scope &
            (partnerSide ? Builders<TicketDoc>.Filter.Eq(t => t.PartnerUnread, true)
                         : Builders<TicketDoc>.Filter.Eq(t => t.SupportUnread, true)));
        return (Of("open"), Of("in_progress"), Of("waiting"), Of("resolved"), Of("closed"), unread);
    }

    /// <summary>Tickets with news for the partner — the bell's feed.</summary>
    public Task<List<TicketDoc>> UnreadForPartnerAsync(int restaurantId, int limit) =>
        _tickets.Find(t => t.RestaurantId == restaurantId && t.PartnerUnread)
            .Project<TicketDoc>(Builders<TicketDoc>.Projection.Exclude(t => t.Messages))
            .SortByDescending(t => t.LastMessageAt).Limit(limit).ToListAsync();

    // ---------- Files ----------

    public Task AddFileAsync(TicketFileDoc file) => _files.InsertOneAsync(file);

    public Task<TicketFileDoc?> GetFileAsync(ObjectId ticketId, ObjectId fileId) =>
        _files.Find(f => f.Id == fileId && f.TicketId == ticketId).FirstOrDefaultAsync()!;
}
