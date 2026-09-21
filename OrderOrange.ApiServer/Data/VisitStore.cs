using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace OrderOrange.ApiServer.Data;

/// <summary>One page view. Append-only, and by far the highest-volume thing written.</summary>
public sealed class VisitDoc
{
    [BsonId] public ObjectId Id { get; set; }

    /// <summary>0 for a guest — most visitors never sign in, and the address is their identity.</summary>
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public UserRole Role { get; set; }

    /// <summary>Customer, Partner, Delivery or Admin.</summary>
    public string App { get; set; } = "";
    public string Page { get; set; } = "";
    public string Ip { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>
/// Page views, in MongoDB rather than SQL.
///
/// This is the one table that grows without limit: every anonymous page view writes a
/// row, and none of it is transactional — nothing joins to it, nothing needs it to be
/// consistent with an order. Keeping it beside the orders was costing the SQL database
/// its headroom for data that actually matters, so it lives here, next to the catalog
/// and the chats.
/// </summary>
public sealed class VisitStore(IMongoDatabase database)
{
    private readonly IMongoCollection<VisitDoc> _visits =
        database.GetCollection<VisitDoc>(CollectionNames.Visits);

    public Task AddAsync(VisitDoc visit) => _visits.InsertOneAsync(visit);

    /// <summary>Everything since a moment ago — small, because the moment is minutes, not days.</summary>
    public Task<List<VisitDoc>> SinceAsync(DateTime since) =>
        _visits.Find(v => v.At >= since).SortByDescending(v => v.At).Limit(3000).ToListAsync();

    /// <summary>Bulk insert — used once, to carry the old SQL rows across.</summary>
    public async Task<int> AddManyAsync(IReadOnlyList<VisitDoc> visits)
    {
        if (visits.Count == 0) return 0;
        await _visits.InsertManyAsync(visits, new InsertManyOptions { IsOrdered = false });
        return visits.Count;
    }

    public Task<long> CountAsync() => _visits.CountDocumentsAsync(FilterDefinition<VisitDoc>.Empty);

    private static FilterDefinition<VisitDoc> Window(DateTime? from, DateTime? to, string? app)
    {
        var f = Builders<VisitDoc>.Filter;
        var filter = f.Empty;
        if (from is not null) filter &= f.Gte(v => v.At, from.Value);
        if (to is not null) filter &= f.Lt(v => v.At, to.Value);
        if (!string.IsNullOrWhiteSpace(app) && app != "All") filter &= f.Eq(v => v.App, app);
        return filter;
    }

    // ---------- The admin activity feed ----------

    public async Task<List<ActivityDto>> RecentAsync(int skip, int take, DateTime? from, DateTime? to,
        UserRole? role, string? app, string? search)
    {
        var f = Builders<VisitDoc>.Filter;
        var filter = Window(from?.Date, to?.Date.AddDays(1), app);
        if (role is not null) filter &= f.Eq(v => v.Role, role.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Escaped: a visitor's name or a page could contain regex punctuation.
            var s = System.Text.RegularExpressions.Regex.Escape(search.Trim());
            filter &= f.Or(f.Regex(v => v.UserName, new BsonRegularExpression(s, "i")),
                           f.Regex(v => v.Page, new BsonRegularExpression(s, "i")));
        }

        var rows = await _visits.Find(filter)
            .SortByDescending(v => v.At)
            .Skip(Math.Max(0, skip)).Limit(Math.Clamp(take, 1, 100))
            .ToListAsync();

        return rows.Select(v => new ActivityDto(v.UserId, v.UserName, v.Role, v.App, v.Page, v.At, v.Ip)).ToList();
    }

    // ---------- The visitor board ----------

    public async Task<VisitorsDto> VisitorsAsync(DateTime since, string app, string? search, int skip, int take)
    {
        var f = Builders<VisitDoc>.Filter;
        var filter = Window(since, null, app) & f.Ne(v => v.Ip, "");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = System.Text.RegularExpressions.Regex.Escape(search.Trim());
            var rx = new BsonRegularExpression(s, "i");
            filter &= f.Or(f.Regex(v => v.Ip, rx), f.Regex(v => v.Page, rx), f.Regex(v => v.UserName, rx));
        }

        // Rolled up by the server. The log runs to millions of rows and must never be
        // pulled into memory to be counted.
        var grouped = await _visits.Aggregate().Match(filter)
            .Group(v => v.Ip, g => new
            {
                Ip = g.Key,
                Visits = g.Count(),
                Pages = g.Select(x => x.Page).Distinct().Count(),
                FirstAt = g.Min(x => x.At),
                LastAt = g.Max(x => x.At),
                SignedIn = g.Max(x => x.UserId),
            })
            .ToListAsync();

        var page = grouped.OrderByDescending(g => g.LastAt)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 200)).ToList();

        // The last page and the names are per-address details, fetched only for the rows
        // actually on screen rather than for every address in the window.
        var ips = page.Select(p => p.Ip).ToList();
        var detail = ips.Count == 0
            ? []
            : await _visits.Find(filter & f.In(v => v.Ip, ips)).ToListAsync();

        var rows = page.Select(p =>
        {
            var mine = detail.Where(d => d.Ip == p.Ip).ToList();
            var last = mine.OrderByDescending(d => d.At).FirstOrDefault();
            var who = string.Join(", ", mine.Where(d => d.UserName.Length > 0)
                .Select(d => d.UserName).Distinct().Take(3));
            return new VisitorDto(p.Ip, p.Visits, p.Pages, p.FirstAt, p.LastAt,
                last?.Page ?? "", last?.App ?? "", who);
        }).ToList();

        return new VisitorsDto(grouped.Count, grouped.Sum(g => g.Visits),
            grouped.Count(g => g.SignedIn == 0), rows);
    }

    public async Task<List<VisitorPageDto>> VisitorPagesAsync(string ip, DateTime since, string app)
    {
        var filter = Window(since, null, app) & Builders<VisitDoc>.Filter.Eq(v => v.Ip, ip);
        var rows = await _visits.Aggregate().Match(filter)
            .Group(v => new { v.App, v.Page }, g => new
            {
                g.Key.App,
                g.Key.Page,
                Views = g.Count(),
                FirstAt = g.Min(x => x.At),
                LastAt = g.Max(x => x.At),
            })
            .ToListAsync();

        return rows.OrderByDescending(r => r.LastAt).Take(200)
            .Select(r => new VisitorPageDto(r.App, r.Page, r.Views, r.FirstAt, r.LastAt)).ToList();
    }

    // ---------- The insights board ----------

    public Task<long> CountSinceAsync(DateTime since, string app) =>
        _visits.CountDocumentsAsync(Window(since, null, app));

    public async Task<int> DistinctVisitorsAsync(DateTime since, string app) =>
        (await _visits.Distinct(v => v.UserId, Window(since, null, app)).ToListAsync()).Count;

    public async Task<List<PageHitDto>> TopPagesAsync(DateTime since, string app, int take)
    {
        var rows = await _visits.Aggregate().Match(Window(since, null, app))
            .Group(v => new { v.App, v.Page }, g => new
            {
                g.Key.App,
                g.Key.Page,
                Views = g.Count(),
                Visitors = g.Select(x => x.UserId).Distinct().Count(),
                LastAt = g.Max(x => x.At),
            })
            .ToListAsync();

        return rows.OrderByDescending(r => r.Views).Take(take)
            .Select(r => new PageHitDto(r.App, r.Page, r.Views, r.Visitors, r.LastAt)).ToList();
    }

    public async Task<List<ActivityDto>> RecentVisitsAsync(DateTime since, string app, int take)
    {
        var rows = await _visits.Find(Window(since, null, app))
            .SortByDescending(v => v.At).Limit(take).ToListAsync();
        return rows.Select(v => new ActivityDto(v.UserId, v.UserName, v.Role, v.App, v.Page, v.At, v.Ip)).ToList();
    }

    // ---------- Presence ----------

    public async Task<Dictionary<int, int>> VisitsTodayByUserAsync()
    {
        var rows = await _visits.Aggregate().Match(Window(DateTime.Today, null, null))
            .Group(v => v.UserId, g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(r => r.UserId, r => r.Count);
    }

    /// <summary>The most recent page each signed-in person opened, in any app.</summary>
    public async Task<Dictionary<int, VisitDoc>> LastVisitByUserAsync()
    {
        var rows = await _visits.Aggregate()
            .Match(Builders<VisitDoc>.Filter.Gt(v => v.UserId, 0))
            .SortByDescending(v => v.At)
            .Group(v => v.UserId, g => new { UserId = g.Key, Last = g.First() })
            .ToListAsync();
        return rows.ToDictionary(r => r.UserId, r => r.Last);
    }

    // ---------- The partner's own team board ----------

    public async Task<Dictionary<int, int>> PartnerVisitsTodayAsync(IReadOnlyCollection<int> userIds)
    {
        if (userIds.Count == 0) return [];
        var f = Builders<VisitDoc>.Filter;
        var filter = Window(DateTime.Today, null, "Partner") & f.In(v => v.UserId, userIds);
        var rows = await _visits.Aggregate().Match(filter)
            .Group(v => v.UserId, g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(r => r.UserId, r => r.Count);
    }

    public async Task<List<TeamVisitDto>> PartnerVisitsAsync(int userId, DateTime? from, DateTime? to, int take)
    {
        var filter = Window(from?.Date, to?.Date.AddDays(1), "Partner")
                     & Builders<VisitDoc>.Filter.Eq(v => v.UserId, userId);
        var rows = await _visits.Find(filter)
            .SortByDescending(v => v.At).Limit(Math.Clamp(take, 1, 2000)).ToListAsync();
        return rows.Select(v => new TeamVisitDto(v.Page, v.At, v.Ip)).ToList();
    }
}
