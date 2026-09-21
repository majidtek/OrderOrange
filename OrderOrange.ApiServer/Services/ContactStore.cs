using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>A message from the public "Contact us" form (the partner guide, for one).</summary>
public sealed class ContactMessageDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
    public string Ip { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    public ContactMessageDto ToDto() => new(Id.ToString(), Email, Phone, Title, Text, Source, Ip, L(CreatedAt), ReadAt is { } r ? L(r) : null);

    // Mongo hands dates back as UTC; the desk shows store-local time.
    private static DateTime L(DateTime d) => d.Kind == DateTimeKind.Utc ? d.ToLocalTime() : d;
}

/// <summary>
/// The contact inbox in Mongo. There is no login on the form and no captcha yet, so the
/// store also answers the two questions the throttle asks: how many today in total, and
/// how many from this address.
/// </summary>
public sealed class ContactStore
{
    private readonly IMongoCollection<ContactMessageDoc> _messages;

    public ContactStore(IMongoDatabase database)
    {
        _messages = database.GetCollection<ContactMessageDoc>("contactMessages");
        _messages.Indexes.CreateOne(new CreateIndexModel<ContactMessageDoc>(
            Builders<ContactMessageDoc>.IndexKeys.Descending(m => m.CreatedAt)));
    }

    public Task<long> CountSinceAsync(DateTime since) =>
        _messages.CountDocumentsAsync(m => m.CreatedAt >= since);

    public Task<long> CountSinceFromAsync(DateTime since, string ip) =>
        _messages.CountDocumentsAsync(m => m.CreatedAt >= since && m.Ip == ip);

    public Task AddAsync(ContactMessageDoc doc) => _messages.InsertOneAsync(doc);

    public async Task<ContactPageDto> PageAsync(int skip, int take, int dailyLimit, DateTime todayStart)
    {
        var total = await _messages.CountDocumentsAsync(FilterDefinition<ContactMessageDoc>.Empty);
        var unread = await _messages.CountDocumentsAsync(m => m.ReadAt == null);
        var today = await CountSinceAsync(todayStart);
        var rows = await _messages.Find(FilterDefinition<ContactMessageDoc>.Empty)
            .SortByDescending(m => m.CreatedAt).Skip(skip).Limit(take).ToListAsync();
        return new ContactPageDto((int)total, (int)unread, (int)today, dailyLimit, rows.Select(r => r.ToDto()).ToList());
    }

    public async Task<ContactSummaryDto> SummaryAsync(int dailyLimit, DateTime todayStart)
    {
        var unread = await _messages.CountDocumentsAsync(m => m.ReadAt == null);
        var today = await CountSinceAsync(todayStart);
        var latest = await _messages.Find(m => m.ReadAt == null).SortByDescending(m => m.CreatedAt).Limit(1).FirstOrDefaultAsync();
        return new ContactSummaryDto((int)unread, (int)today, dailyLimit, latest is null ? null : (latest.CreatedAt.Kind == DateTimeKind.Utc ? latest.CreatedAt.ToLocalTime() : latest.CreatedAt), latest?.Title);
    }

    public async Task<bool> MarkReadAsync(string id, bool read)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;
        var r = await _messages.UpdateOneAsync(m => m.Id == oid,
            Builders<ContactMessageDoc>.Update.Set(m => m.ReadAt, read ? DateTime.Now : null));
        return r.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;
        var r = await _messages.DeleteOneAsync(m => m.Id == oid);
        return r.DeletedCount > 0;
    }
}
