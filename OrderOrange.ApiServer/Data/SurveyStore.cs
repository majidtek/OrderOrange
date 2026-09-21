using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Data;

/// <summary>One question and the answers a guest may tick.</summary>
public sealed class SurveyQuestionDoc
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public bool Multi { get; set; }
}

/// <summary>A store's questionnaire.</summary>
public sealed class SurveyDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public int RestaurantId { get; set; }
    public string Title { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SurveyQuestionDoc> Questions { get; set; } = [];
}

public sealed class SurveyAnswerDoc
{
    public string QuestionId { get; set; } = "";
    public List<int> Selected { get; set; } = [];
}

/// <summary>One guest's ticks. Anonymous by design — no name, no phone, no account.</summary>
public sealed class SurveyResponseDoc
{
    [BsonId] public ObjectId Id { get; set; }
    public ObjectId SurveyId { get; set; }
    public int RestaurantId { get; set; }
    public DateTime At { get; set; }
    public List<SurveyAnswerDoc> Answers { get; set; } = [];
    public string? Comment { get; set; }
    /// <summary>Where the guest came from — a table code, or empty for the store page.</summary>
    public string? Source { get; set; }
}

/// <summary>
/// Surveys live in MongoDB beside the catalog and the chats: free-form questions with
/// free-form option lists, and an answer stream that only ever grows — nothing in SQL
/// joins to any of it.
/// </summary>
public sealed class SurveyStore(IMongoDatabase database)
{
    private readonly IMongoCollection<SurveyDoc> _surveys =
        database.GetCollection<SurveyDoc>(CollectionNames.Surveys);
    private readonly IMongoCollection<SurveyResponseDoc> _responses =
        database.GetCollection<SurveyResponseDoc>(CollectionNames.SurveyResponses);

    public async Task EnsureIndexesAsync()
    {
        await _surveys.Indexes.CreateOneAsync(new CreateIndexModel<SurveyDoc>(
            Builders<SurveyDoc>.IndexKeys.Ascending(s => s.RestaurantId).Descending(s => s.CreatedAt)));
        await _responses.Indexes.CreateOneAsync(new CreateIndexModel<SurveyResponseDoc>(
            Builders<SurveyResponseDoc>.IndexKeys.Ascending(r => r.SurveyId).Descending(r => r.At)));
    }

    // ---------- The store's side ----------

    public Task<List<SurveyDoc>> ListAsync(int restaurantId) =>
        _surveys.Find(s => s.RestaurantId == restaurantId).SortByDescending(s => s.CreatedAt).ToListAsync();

    public Task<SurveyDoc?> GetAsync(int restaurantId, ObjectId id) =>
        _surveys.Find(s => s.Id == id && s.RestaurantId == restaurantId).FirstOrDefaultAsync()!;

    public Task<long> CountAsync(int restaurantId) =>
        _surveys.CountDocumentsAsync(s => s.RestaurantId == restaurantId);

    public Task InsertAsync(SurveyDoc survey) => _surveys.InsertOneAsync(survey);

    public Task ReplaceAsync(SurveyDoc survey) =>
        _surveys.ReplaceOneAsync(s => s.Id == survey.Id && s.RestaurantId == survey.RestaurantId, survey);

    /// <summary>The survey AND every answer ever given to it — nothing orphaned.</summary>
    public async Task DeleteAsync(int restaurantId, ObjectId id)
    {
        await _responses.DeleteManyAsync(r => r.SurveyId == id && r.RestaurantId == restaurantId);
        await _surveys.DeleteOneAsync(s => s.Id == id && s.RestaurantId == restaurantId);
    }

    /// <summary>Response counts for a batch of surveys, one round trip.</summary>
    public async Task<Dictionary<ObjectId, int>> ResponseCountsAsync(IEnumerable<ObjectId> ids)
    {
        var list = ids.ToList();
        if (list.Count == 0) return [];
        var rows = await _responses.Aggregate()
            .Match(r => list.Contains(r.SurveyId))
            .Group(r => r.SurveyId, g => new { Id = g.Key, N = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(x => x.Id, x => x.N);
    }

    public Task<List<SurveyResponseDoc>> ResponsesAsync(ObjectId surveyId) =>
        _responses.Find(r => r.SurveyId == surveyId).SortByDescending(r => r.At).ToListAsync();

    // ---------- The guest's side ----------

    /// <summary>The newest ACTIVE survey of a store — the one its guests are asked.</summary>
    public Task<SurveyDoc?> ActiveAsync(int restaurantId) =>
        _surveys.Find(s => s.RestaurantId == restaurantId && s.IsActive)
            .SortByDescending(s => s.CreatedAt).FirstOrDefaultAsync()!;

    public Task AddResponseAsync(SurveyResponseDoc response) => _responses.InsertOneAsync(response);
}
