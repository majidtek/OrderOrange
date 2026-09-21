using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>The shop's programme. One document per store, keyed by the store id.</summary>
public sealed class LoyaltyProgramDoc
{
    [BsonId] public int StoreId { get; set; }
    public bool Enabled { get; set; }
    public decimal PointsPerUnit { get; set; } = 1m;
    public int RewardPoints { get; set; } = 100;
    public decimal RewardValue { get; set; } = 5m;
    public int WelcomePoints { get; set; } = 10;
    public int SilverAt { get; set; } = 500;
    public int GoldAt { get; set; } = 1500;
    public DateTime UpdatedAt { get; set; }

    public LoyaltyProgramDto ToDto() => new(Enabled, PointsPerUnit, RewardPoints, RewardValue, WelcomePoints, SilverAt, GoldAt);
}

/// <summary>A guest's card. "&lt;store&gt;:&lt;user&gt;" — one per guest per shop.</summary>
public sealed class LoyaltyMemberDoc
{
    [BsonId] public string Id { get; set; } = "";
    public int StoreId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public int Balance { get; set; }
    public int Lifetime { get; set; }
    public int Redeemed { get; set; }
    public int Visits { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LastAt { get; set; }
}

/// <summary>Every change to a card, so a balance can always be explained.</summary>
public sealed class LoyaltyEventDoc
{
    [BsonId] public int Id { get; set; }
    public int StoreId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = LoyaltyKinds.Earn;
    public int Points { get; set; }
    public decimal? Amount { get; set; }
    /// <summary>Set when an order earned the points — and checked so no order pays twice.</summary>
    public int? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public string? Note { get; set; }
    public string By { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>
/// Where the programme, the cards and their history live. Mongo for the usual reason — the
/// SQL schema is EnsureCreated and can never grow on a live database — and because a card
/// is a running total the shop reads far more often than it writes.
/// </summary>
public sealed class LoyaltyStore
{
    private readonly IMongoCollection<LoyaltyProgramDoc> _programs;
    private readonly IMongoCollection<LoyaltyMemberDoc> _members;
    private readonly IMongoCollection<LoyaltyEventDoc> _events;
    private readonly IMongoCollection<BsonDocument> _counters;

    public LoyaltyStore(IMongoDatabase database)
    {
        _programs = database.GetCollection<LoyaltyProgramDoc>("loyaltyPrograms");
        _members = database.GetCollection<LoyaltyMemberDoc>("loyaltyMembers");
        _events = database.GetCollection<LoyaltyEventDoc>("loyaltyEvents");
        _counters = database.GetCollection<BsonDocument>("counters");

        _members.Indexes.CreateOne(new CreateIndexModel<LoyaltyMemberDoc>(
            Builders<LoyaltyMemberDoc>.IndexKeys.Ascending(m => m.StoreId).Descending(m => m.Lifetime)));
        _events.Indexes.CreateOne(new CreateIndexModel<LoyaltyEventDoc>(
            Builders<LoyaltyEventDoc>.IndexKeys.Ascending(e => e.StoreId).Descending(e => e.At)));
        _events.Indexes.CreateOne(new CreateIndexModel<LoyaltyEventDoc>(
            Builders<LoyaltyEventDoc>.IndexKeys.Ascending(e => e.StoreId).Ascending(e => e.OrderId)));
    }

    private async Task<int> NextAsync(string name)
    {
        var seq = await _counters.FindOneAndUpdateAsync<BsonDocument>(
            Builders<BsonDocument>.Filter.Eq("_id", name),
            Builders<BsonDocument>.Update.Inc("seq", 1),
            new FindOneAndUpdateOptions<BsonDocument, BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After });
        return seq["seq"].ToInt32();
    }

    private static string Key(int storeId, int userId) => $"{storeId}:{userId}";

    // ───────────────────────────── programme ─────────────────────────────

    public async Task<LoyaltyProgramDoc> ProgramAsync(int storeId) =>
        await _programs.Find(p => p.StoreId == storeId).FirstOrDefaultAsync() ?? new LoyaltyProgramDoc { StoreId = storeId };

    public Task SaveProgramAsync(LoyaltyProgramDoc doc)
    {
        doc.UpdatedAt = DateTime.Now;
        return _programs.ReplaceOneAsync(p => p.StoreId == doc.StoreId, doc, new ReplaceOptions { IsUpsert = true });
    }

    // ───────────────────────────── cards ─────────────────────────────

    public Task<LoyaltyMemberDoc?> MemberAsync(int storeId, int userId) =>
        _members.Find(m => m.Id == Key(storeId, userId)).FirstOrDefaultAsync()!;

    public Task<List<LoyaltyMemberDoc>> MembersAsync(int storeId) =>
        _members.Find(m => m.StoreId == storeId).ToListAsync();

    public Task<List<LoyaltyMemberDoc>> TopAsync(int storeId, int take = 8) =>
        _members.Find(m => m.StoreId == storeId).SortByDescending(m => m.Lifetime).Limit(take).ToListAsync();

    public Task<long> CountAsync(int storeId) => _members.CountDocumentsAsync(m => m.StoreId == storeId);

    public Task SaveMemberAsync(LoyaltyMemberDoc doc) =>
        _members.ReplaceOneAsync(m => m.Id == doc.Id, doc, new ReplaceOptions { IsUpsert = true });

    // ───────────────────────────── history ─────────────────────────────

    public async Task<LoyaltyEventDoc> AddEventAsync(LoyaltyEventDoc doc)
    {
        doc.Id = await NextAsync("loyaltyEvents");
        doc.At = DateTime.Now;
        await _events.InsertOneAsync(doc);
        return doc;
    }

    public Task<bool> OrderRewardedAsync(int storeId, int orderId) =>
        _events.Find(e => e.StoreId == storeId && e.OrderId == orderId).AnyAsync();

    public Task<List<LoyaltyEventDoc>> EventsAsync(int storeId, int take = 40) =>
        _events.Find(e => e.StoreId == storeId).SortByDescending(e => e.Id).Limit(take).ToListAsync();

    public Task<List<LoyaltyEventDoc>> MemberEventsAsync(int storeId, int userId, int take = 60) =>
        _events.Find(e => e.StoreId == storeId && e.UserId == userId).SortByDescending(e => e.Id).Limit(take).ToListAsync();

    public Task<List<LoyaltyEventDoc>> EventsSinceAsync(int storeId, DateTime since) =>
        _events.Find(e => e.StoreId == storeId && e.At >= since).ToListAsync();

    // ───────────────────────────── the moves ─────────────────────────────

    /// <summary>
    /// Points land on a card. A guest who has never been on the programme gets a card and
    /// the welcome gift in the same breath, so the first receipt already shows something.
    /// Returns the card as it is now.
    /// </summary>
    public async Task<LoyaltyMemberDoc> CreditAsync(LoyaltyProgramDoc program, int userId, string name, string phone,
        int points, string kind, string by, decimal? amount = null, int? orderId = null, string? orderNumber = null, string? note = null)
    {
        var member = await MemberAsync(program.StoreId, userId);
        var isNew = member is null;
        member ??= new LoyaltyMemberDoc
        {
            Id = Key(program.StoreId, userId), StoreId = program.StoreId, UserId = userId, JoinedAt = DateTime.Now,
        };
        member.Name = string.IsNullOrWhiteSpace(name) ? member.Name : name;
        member.Phone = string.IsNullOrWhiteSpace(phone) ? member.Phone : phone;

        if (isNew && program.WelcomePoints > 0)
        {
            member.Balance += program.WelcomePoints;
            member.Lifetime += program.WelcomePoints;
            await AddEventAsync(new LoyaltyEventDoc
            {
                StoreId = program.StoreId, UserId = userId, Name = member.Name, Kind = LoyaltyKinds.Welcome,
                Points = program.WelcomePoints, By = by,
            });
        }

        if (points != 0)
        {
            member.Balance = Math.Max(0, member.Balance + points);
            if (points > 0) member.Lifetime += points;
            if (kind == LoyaltyKinds.Earn) member.Visits++;
            await AddEventAsync(new LoyaltyEventDoc
            {
                StoreId = program.StoreId, UserId = userId, Name = member.Name, Kind = kind, Points = points,
                Amount = amount, OrderId = orderId, OrderNumber = orderNumber, Note = note, By = by,
            });
        }
        member.LastAt = DateTime.Now;
        await SaveMemberAsync(member);
        return member;
    }

    /// <summary>Points leave the card for a reward. The caller has checked the balance.</summary>
    public async Task<LoyaltyMemberDoc> RedeemAsync(LoyaltyProgramDoc program, LoyaltyMemberDoc member, int points, string by, string? note)
    {
        member.Balance -= points;
        member.Redeemed += points;
        member.LastAt = DateTime.Now;
        await AddEventAsync(new LoyaltyEventDoc
        {
            StoreId = program.StoreId, UserId = member.UserId, Name = member.Name, Kind = LoyaltyKinds.Redeem,
            Points = -points, Amount = Math.Round(points * program.RewardValue / Math.Max(1, program.RewardPoints), 3),
            Note = note, By = by,
        });
        await SaveMemberAsync(member);
        return member;
    }

    // ───────────────────────────── the automatic part ─────────────────────────────

    /// <summary>
    /// An order was paid or delivered: its guest earns points by the shop's rate. Safe to
    /// call from every place an order completes — an order that already paid out is skipped,
    /// as is the nameless walk-in, who has no card to put anything on.
    /// </summary>
    public async Task TryAwardAsync(AppDbContext db, Order order, string by)
    {
        try
        {
            if (!(order.IsPaid || order.Status == OrderStatus.Delivered)) return;
            if (order.Status is OrderStatus.Cancelled or OrderStatus.Rejected) return;
            var program = await ProgramAsync(order.RestaurantId);
            if (!program.Enabled || program.PointsPerUnit <= 0) return;
            var points = (int)Math.Floor(order.Total * program.PointsPerUnit);
            if (points <= 0) return;
            if (await OrderRewardedAsync(order.RestaurantId, order.Id)) return;

            var card = await db.StoreCustomers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.RestaurantId == order.RestaurantId && c.UserId == order.CustomerId);
            var phone = card?.Phone ?? "";
            var name = card?.Name ?? "";
            if (card is null)
            {
                var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == order.CustomerId);
                if (user is null) return;
                phone = user.Phone; name = user.FullName;
            }
            if (phone == WalkInBook.Phone) return;

            await CreditAsync(program, order.CustomerId, name, phone, points, LoyaltyKinds.Earn, by,
                amount: order.Total, orderId: order.Id, orderNumber: order.Number);
        }
        catch
        {
            // A reward that fails must never fail the sale it rides on.
        }
    }
}
