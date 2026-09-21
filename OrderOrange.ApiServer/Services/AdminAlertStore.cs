using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// One line on the administrator's bell. Read state is a list of the administrators who have
/// opened it rather than a single flag, so one admin clearing the bell does not hide the news
/// from another.
/// </summary>
public sealed class AdminAlertDoc
{
    [BsonId] public int Id { get; set; }
    /// <summary>"user" or "business".</summary>
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Url { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<int> ReadBy { get; set; } = [];
}

/// <summary>
/// Where "somebody joined" lives. Mongo rather than SQL: one small append-only document per
/// event that nothing joins against, and EnsureCreated would never add a new SQL table to the
/// live database anyway. Integer ids from the shared counters collection so the bell can poll
/// with afterId instead of re-reading the whole list.
/// </summary>
public sealed class AdminAlertStore
{
    private readonly IMongoCollection<AdminAlertDoc> _alerts;
    private readonly IMongoCollection<BsonDocument> _counters;

    public AdminAlertStore(IMongoDatabase database)
    {
        _alerts = database.GetCollection<AdminAlertDoc>("adminAlerts");
        _counters = database.GetCollection<BsonDocument>("counters");
        // No index is declared here on purpose: the newest-first sort runs on _id, and Mongo
        // both indexes _id for us and refuses any other index on it ("the field 'key' for an
        // _id index must be {_id: 1}"), which threw on construction when this said Descending.
    }

    public async Task<AdminAlertDoc> AddAsync(string kind, string title, string body, string? url)
    {
        var seq = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", "adminAlerts"),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument>
            { IsUpsert = true, ReturnDocument = ReturnDocument.After });

        var doc = new AdminAlertDoc
        {
            Id = seq["seq"].ToInt32(),
            Kind = kind,
            Title = title,
            Body = body,
            Url = url,
            CreatedAt = DateTime.UtcNow,
        };
        await _alerts.InsertOneAsync(doc);
        return doc;
    }

    public Task<List<AdminAlertDoc>> RecentAsync(int limit = 50) =>
        _alerts.Find(FilterDefinition<AdminAlertDoc>.Empty)
               .SortByDescending(a => a.Id).Limit(limit).ToListAsync();

    public Task<long> UnreadCountAsync(int adminUserId) =>
        _alerts.CountDocumentsAsync(Builders<AdminAlertDoc>.Filter.Not(
            Builders<AdminAlertDoc>.Filter.AnyEq(a => a.ReadBy, adminUserId)));

    /// <summary>Marks everything this administrator can currently see as opened.</summary>
    public Task MarkAllReadAsync(int adminUserId) =>
        _alerts.UpdateManyAsync(
            Builders<AdminAlertDoc>.Filter.Not(
                Builders<AdminAlertDoc>.Filter.AnyEq(a => a.ReadBy, adminUserId)),
            Builders<AdminAlertDoc>.Update.AddToSet(a => a.ReadBy, adminUserId));
}
