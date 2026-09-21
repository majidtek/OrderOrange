using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>One line of a conversation with the assistant, as typed and as answered.</summary>
public sealed class BotChatDoc
{
    [BsonId] public int Id { get; set; }
    /// <summary>The visitor's address, from CF-Connecting-IP behind Cloudflare.</summary>
    public string Ip { get; set; } = "";
    /// <summary>Groups the turns of one sitting, so a report can show a conversation, not a heap.</summary>
    public string Session { get; set; } = "";
    /// <summary>Which assistant: "customer", "partner" or "pos".</summary>
    public string Bot { get; set; } = "customer";
    /// <summary>What the person typed.</summary>
    public string Text { get; set; } = "";
    /// <summary>What the assistant answered, trimmed to something a report can show.</summary>
    public string Reply { get; set; } = "";
    /// <summary>The page they were on when they typed it.</summary>
    public string? Page { get; set; }
    public string? Lang { get; set; }
    public int? StoreId { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Country { get; set; }
    public string? Agent { get; set; }
    public DateTime At { get; set; }
}

/// <summary>
/// Everything said to the assistants, kept so the team can read what people actually ask.
/// Mongo, like the rest of the write-heavy trails: one small append-only document per turn
/// that nothing joins against, and a new SQL table would have to be made by hand.
/// </summary>
public sealed class BotChatStore
{
    private readonly IMongoCollection<BotChatDoc> _turns;
    private readonly IMongoCollection<BsonDocument> _counters;

    public BotChatStore(IMongoDatabase database)
    {
        _turns = database.GetCollection<BotChatDoc>("botChats");
        _counters = database.GetCollection<BsonDocument>("counters");
        // _id is indexed by Mongo itself and may not be re-declared; these two are the
        // report's own access paths: everything from one address, and one conversation.
        _turns.Indexes.CreateOne(new CreateIndexModel<BotChatDoc>(
            Builders<BotChatDoc>.IndexKeys.Ascending(t => t.Ip).Descending(t => t.At)));
        _turns.Indexes.CreateOne(new CreateIndexModel<BotChatDoc>(
            Builders<BotChatDoc>.IndexKeys.Ascending(t => t.Session).Ascending(t => t.Id)));
    }

    public async Task<BotChatDoc> AddAsync(BotChatDoc doc)
    {
        var seq = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", "botChats"),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument>
            { IsUpsert = true, ReturnDocument = ReturnDocument.After });
        doc.Id = seq["seq"].ToInt32();
        doc.At = DateTime.UtcNow;
        await _turns.InsertOneAsync(doc);
        return doc;
    }

    /// <summary>Every turn from one address, newest first.</summary>
    public Task<List<BotChatDoc>> ByIpAsync(string ip, int limit = 500) =>
        _turns.Find(t => t.Ip == ip).SortByDescending(t => t.Id).Limit(limit).ToListAsync();

    public Task<List<BotChatDoc>> RecentAsync(int skip, int take, string? search) =>
        _turns.Find(Where(search)).SortByDescending(t => t.Id).Skip(skip).Limit(take).ToListAsync();

    public Task<long> CountAsync(string? search) => _turns.CountDocumentsAsync(Where(search));

    private static FilterDefinition<BotChatDoc> Where(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return FilterDefinition<BotChatDoc>.Empty;
        var rx = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
        return Builders<BotChatDoc>.Filter.Or(
            Builders<BotChatDoc>.Filter.Regex(t => t.Text, rx),
            Builders<BotChatDoc>.Filter.Regex(t => t.Reply, rx),
            Builders<BotChatDoc>.Filter.Regex(t => t.Ip, rx));
    }

    /// <summary>
    /// The report's front page: one row per address, with how much was said, when, and the
    /// last thing typed. Grouped in Mongo so a busy month does not travel over the wire.
    /// </summary>
    public async Task<List<BotChatIpRow>> ByIpSummaryAsync(int skip, int take, string? search)
    {
        var match = Where(search);
        var pipeline = _turns.Aggregate().Match(match)
            .Group(new BsonDocument
            {
                { "_id", "$Ip" },
                { "turns", new BsonDocument("$sum", 1) },
                { "first", new BsonDocument("$min", "$At") },
                { "last", new BsonDocument("$max", "$At") },
                { "sessions", new BsonDocument("$addToSet", "$Session") },
                { "lastText", new BsonDocument("$last", "$Text") },
                { "country", new BsonDocument("$last", "$Country") },
            })
            .Sort(new BsonDocument("last", -1))
            .Skip(skip).Limit(take);

        var docs = await pipeline.ToListAsync();
        return docs.Select(d => new BotChatIpRow(
            d["_id"].IsBsonNull ? "" : d["_id"].AsString,
            d["turns"].ToInt32(),
            d.Contains("sessions") ? d["sessions"].AsBsonArray.Count : 0,
            d["first"].ToUniversalTime(),
            d["last"].ToUniversalTime(),
            d.Contains("lastText") && !d["lastText"].IsBsonNull ? d["lastText"].AsString : "",
            d.Contains("country") && !d["country"].IsBsonNull ? d["country"].AsString : null)).ToList();
    }

    /// <summary>How many distinct addresses the report has to page through.</summary>
    public async Task<int> IpCountAsync(string? search)
    {
        var docs = await _turns.Aggregate().Match(Where(search))
            .Group(new BsonDocument { { "_id", "$Ip" } }).ToListAsync();
        return docs.Count;
    }
}

/// <summary>One address's line on the report's front page.</summary>
public record BotChatIpRow(string Ip, int Turns, int Sessions, DateTime First, DateTime Last,
    string LastText, string? Country);
