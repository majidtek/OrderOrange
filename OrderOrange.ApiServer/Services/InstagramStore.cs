using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Services;

/// <summary>One store's Instagram connection. The _id IS the restaurant id: one account per store.</summary>
public sealed class StoreSocialDoc
{
    [BsonId] public int Id { get; set; }
    public string IgUserId { get; set; } = "";
    public string IgUsername { get; set; } = "";
    /// <summary>The owner's long-lived token. Never leaves the server.</summary>
    public string IgToken { get; set; } = "";
    public DateTime? IgExpiresAt { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? RefreshedAt { get; set; }
    public string? LastPostUrl { get; set; }
    public DateTime? LastPostAt { get; set; }
    public int PostCount { get; set; }
}

/// <summary>
/// Where the shops' Instagram connections live. Mongo rather than SQL because this is one
/// small document per store that nothing joins against — and because a new SQL table would
/// have to be created by hand (EnsureCreated never adds one to a live database).
/// </summary>
public sealed class InstagramStore
{
    private readonly IMongoCollection<StoreSocialDoc> _docs;

    public InstagramStore(IMongoDatabase database) =>
        _docs = database.GetCollection<StoreSocialDoc>("storeSocial");

    public Task<StoreSocialDoc?> GetAsync(int storeId) =>
        _docs.Find(d => d.Id == storeId).FirstOrDefaultAsync()!;

    public Task SaveAsync(StoreSocialDoc doc) =>
        _docs.ReplaceOneAsync(d => d.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true });

    public Task DeleteAsync(int storeId) => _docs.DeleteOneAsync(d => d.Id == storeId);

    /// <summary>After a successful publish: remember where it went, for the page to show.</summary>
    public Task RecordPostAsync(int storeId, string permalink) =>
        _docs.UpdateOneAsync(d => d.Id == storeId,
            Builders<StoreSocialDoc>.Update
                .Set(d => d.LastPostUrl, permalink)
                .Set(d => d.LastPostAt, DateTime.UtcNow)
                .Inc(d => d.PostCount, 1));

    /// <summary>A refreshed token replaces the old one in place.</summary>
    public Task UpdateTokenAsync(int storeId, string token, DateTime expiresAt) =>
        _docs.UpdateOneAsync(d => d.Id == storeId,
            Builders<StoreSocialDoc>.Update
                .Set(d => d.IgToken, token)
                .Set(d => d.IgExpiresAt, expiresAt)
                .Set(d => d.RefreshedAt, DateTime.UtcNow));
}
