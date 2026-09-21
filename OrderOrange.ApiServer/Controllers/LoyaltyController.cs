using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The loyalty programme: its rules, the guests' cards, and the moves on them. Points are
/// earned automatically when an order is paid or delivered; this is where the shop sets
/// the rate, looks a guest up, rings up a visit by hand and gives out rewards.
/// </summary>
[ApiController]
[Route("api/loyalty")]
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Customers)]
public class LoyaltyController(AppDbContext db, LoyaltyStore store) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<LoyaltyBoardDto>> Board()
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();

        var program = await store.ProgramAsync(storeId);
        var members = await store.MembersAsync(storeId);
        var since = DateTime.Now.Date.AddDays(-29);
        var recentEvents = await store.EventsSinceAsync(storeId, since);

        var outstanding = members.Sum(m => m.Balance);
        var redeemed = members.Sum(m => m.Redeemed);
        var perPoint = program.RewardPoints > 0 ? program.RewardValue / program.RewardPoints : 0m;

        var days = Enumerable.Range(0, 30).Select(i => since.AddDays(i)).Select(d => new LoyaltyDayDto(d,
            recentEvents.Where(e => e.At.Date == d && e.Points > 0).Sum(e => e.Points),
            recentEvents.Where(e => e.At.Date == d && e.Kind == LoyaltyKinds.Redeem).Sum(e => -e.Points))).ToList();

        // Who to nudge: a few points from a reward, or gone quiet for a month.
        var need = Math.Max(1, program.RewardPoints);
        var almost = members
            .Where(m => m.Visits > 0 && m.Balance < need && need - m.Balance <= Math.Max(10, need / 2))
            .OrderBy(m => need - m.Balance).Take(6).Select(m => ToDto(m, program)).ToList();
        var sleeping = members
            .Where(m => m.Lifetime > 0 && m.LastAt is { } last && last < since)
            .OrderByDescending(m => m.Lifetime).Take(6).Select(m => ToDto(m, program)).ToList();
        var tiers = members.Select(m => LoyaltyTiers.For(m.Lifetime, program.SilverAt, program.GoldAt)).ToList();
        var storeName = await db.Restaurants.Where(r => r.Id == storeId).Select(r => r.Name).FirstOrDefaultAsync() ?? "";

        return Ok(new LoyaltyBoardDto(
            program.ToDto(),
            members.Count,
            members.Count(m => m.LastAt >= since),
            outstanding,
            Math.Round(outstanding * perPoint, 3),
            redeemed,
            Math.Round(redeemed * perPoint, 3),
            recentEvents.Where(e => e.Points > 0).Sum(e => e.Points),
            members.OrderByDescending(m => m.Lifetime).Take(8).Select(m => ToDto(m, program)).ToList(),
            (await store.EventsAsync(storeId, 30)).Select(ToDto).ToList(),
            days,
            almost, sleeping,
            tiers.Count(t => t == LoyaltyTiers.Bronze), tiers.Count(t => t == LoyaltyTiers.Silver), tiers.Count(t => t == LoyaltyTiers.Gold),
            storeName));
    }

    [HttpPut("program")]
    public async Task<IActionResult> SaveProgram(SaveLoyaltyProgramRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (req.PointsPerUnit is < 0 or > 1000) return BadRequest(new { message = "Points per unit must be between 0 and 1000." });
        if (req.RewardPoints is < 1 or > 100000) return BadRequest(new { message = "A reward needs at least one point." });
        if (req.RewardValue < 0) return BadRequest(new { message = "The reward cannot be worth less than nothing." });
        if (req.SilverAt < 0 || req.GoldAt < 0 || (req.GoldAt > 0 && req.SilverAt > 0 && req.GoldAt <= req.SilverAt))
            return BadRequest(new { message = "Gold must sit above Silver." });

        var doc = await store.ProgramAsync(storeId);
        doc.Enabled = req.Enabled;
        doc.PointsPerUnit = req.PointsPerUnit;
        doc.RewardPoints = req.RewardPoints;
        doc.RewardValue = req.RewardValue;
        doc.WelcomePoints = Math.Clamp(req.WelcomePoints, 0, 100000);
        doc.SilverAt = req.SilverAt;
        doc.GoldAt = req.GoldAt;
        await store.SaveProgramAsync(doc);
        return NoContent();
    }

    /// <summary>
    /// Find a guest by name or phone. Comes from the shop's customer book, so a guest who has
    /// never earned a point still shows up — with an empty card — and can be rung up.
    /// </summary>
    [HttpGet("lookup")]
    public async Task<ActionResult<List<LoyaltyMemberDto>>> Lookup([FromQuery] string q = "")
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        q = (q ?? "").Trim();
        var program = await store.ProgramAsync(storeId);

        var query = db.StoreCustomers.AsNoTracking()
            .Where(c => c.RestaurantId == storeId && c.IsActive && c.Phone != WalkInBook.Phone);
        if (q.Length > 0) query = query.Where(c => c.Name.Contains(q) || c.Phone.Contains(q));
        var cards = await query.OrderByDescending(c => c.LastOrderAt).Take(12).ToListAsync();

        var result = new List<LoyaltyMemberDto>();
        foreach (var c in cards)
        {
            var m = await store.MemberAsync(storeId, c.UserId);
            result.Add(m is null
                ? new LoyaltyMemberDto(c.UserId, c.Name, c.Phone, 0, 0, 0, 0, LoyaltyTiers.Bronze, null, c.LastOrderAt, program.RewardPoints, 0)
                : ToDto(m, program));
        }
        return Ok(result);
    }

    [HttpGet("members/{userId:int}")]
    public async Task<ActionResult<List<LoyaltyEventDto>>> History(int userId)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        return Ok((await store.MemberEventsAsync(storeId, userId)).Select(ToDto).ToList());
    }

    /// <summary>A visit rung up by hand — a cash sale, an old receipt, a phone order.</summary>
    [HttpPost("earn")]
    public async Task<ActionResult<LoyaltyMemberDto>> Earn(LoyaltyEarnRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (req.Amount <= 0) return BadRequest(new { message = "Enter what the guest spent." });
        var program = await store.ProgramAsync(storeId);
        if (!program.Enabled) return BadRequest(new { message = "The programme is switched off." });
        var guest = await GuestAsync(storeId, req.UserId);
        if (guest is null) return NotFound();

        var points = (int)Math.Floor(req.Amount * program.PointsPerUnit);
        if (points <= 0) return BadRequest(new { message = "That amount earns no points at the current rate." });
        var member = await store.CreditAsync(program, req.UserId, guest.Value.Name, guest.Value.Phone, points,
            LoyaltyKinds.Earn, CurrentUserName, amount: req.Amount, note: Trim(req.Note));
        return Ok(ToDto(member, program));
    }

    [HttpPost("redeem")]
    public async Task<ActionResult<LoyaltyMemberDto>> Redeem(LoyaltyRedeemRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        var program = await store.ProgramAsync(storeId);
        var member = await store.MemberAsync(storeId, req.UserId);
        if (member is null) return NotFound();
        var points = req.Points > 0 ? req.Points : program.RewardPoints;
        if (points > member.Balance) return BadRequest(new { message = $"Only {member.Balance} points on the card." });
        member = await store.RedeemAsync(program, member, points, CurrentUserName, Trim(req.Note));
        return Ok(ToDto(member, program));
    }

    [HttpPost("adjust")]
    public async Task<ActionResult<LoyaltyMemberDto>> Adjust(LoyaltyAdjustRequest req)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        if (req.Points == 0) return BadRequest(new { message = "Nothing to change." });
        if (string.IsNullOrWhiteSpace(req.Note)) return BadRequest(new { message = "Say why." });
        var program = await store.ProgramAsync(storeId);
        var guest = await GuestAsync(storeId, req.UserId);
        if (guest is null) return NotFound();
        var member = await store.CreditAsync(program, req.UserId, guest.Value.Name, guest.Value.Phone, req.Points,
            LoyaltyKinds.Adjust, CurrentUserName, note: Trim(req.Note));
        return Ok(ToDto(member, program));
    }

    /// <summary>
    /// Right now: who is on the shop's pages (last ten minutes, one row per person) and
    /// which orders are still unpaid — each with the guest's points, so the till can say
    /// "you have a reward" before the bill is settled.
    /// </summary>
    [HttpGet("live")]
    public async Task<ActionResult<LoyaltyLiveDto>> Live()
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        var program = await store.ProgramAsync(storeId);
        var need = Math.Max(1, program.RewardPoints);
        var shop = await db.Restaurants.AsNoTracking().Where(r => r.Id == storeId).Select(r => new { r.Slug }).FirstOrDefaultAsync();
        var slug = (shop?.Slug ?? "").Trim().ToLowerInvariant();

        // ---- open invoices: unpaid, not dead
        var open = await db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == storeId && !o.IsPaid && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected)
            .OrderByDescending(o => o.PlacedAt).Take(30)
            .Select(o => new { o.Id, o.Number, o.CustomerId, o.Total, o.PlacedAt, o.Status, o.OrderType, o.TableName,
                               Customer = o.Customer.FullName, Phone = o.Customer.Phone })
            .ToListAsync();
        var openIds = open.Select(o => o.Id).ToHashSet();
        var cards = new Dictionary<int, LoyaltyMemberDoc>();
        async Task<LoyaltyMemberDoc?> CardAsync(int userId)
        {
            if (cards.TryGetValue(userId, out var c)) return c;
            var m = await store.MemberAsync(storeId, userId);
            if (m is not null) cards[userId] = m;
            return m;
        }
        var openDtos = new List<OpenInvoiceDto>();
        foreach (var o in open)
        {
            var walkIn = o.Phone == WalkInBook.Phone;
            var m = walkIn ? null : await CardAsync(o.CustomerId);
            openDtos.Add(new OpenInvoiceDto(o.Id, o.Number, walkIn ? "" : o.Customer, walkIn ? "" : o.Phone, o.Total, o.PlacedAt,
                o.Status.ToString(), o.OrderType.ToString(), o.TableName, walkIn ? null : o.CustomerId,
                m?.Balance ?? 0, (m?.Balance ?? 0) / need));
        }

        // ---- online now: the customer site's pages that belong to this shop
        var since = DateTime.Now.AddMinutes(-10);
        // Page views land in ActivityLogs (one row per address, app and page every half minute).
        var recent = await db.ActivityLogs.AsNoTracking()
            .Where(a => a.At >= since && a.App != "partner" && a.App != "admin")
            .OrderByDescending(a => a.At).Take(3000)
            .Select(a => new { a.UserId, a.UserName, a.Page, a.Ip, a.At }).ToListAsync();
        // Anyone following one of today's orders counts too, not only the unpaid ones.
        var trackable = openIds.ToHashSet();
        foreach (var id in await db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == storeId && o.PlacedAt >= DateTime.Now.AddHours(-3))
            .Select(o => o.Id).ToListAsync()) trackable.Add(id);

        bool Mine(string page)
        {
            var path = (page ?? "").Split('?')[0].TrimEnd('/').ToLowerInvariant();
            if (path.Length == 0) return false;
            if (slug.Length > 0 && (path == "/" + slug || path.StartsWith("/" + slug + "/"))) return true;
            if (path == $"/restaurant/{storeId}" || path == $"/store/{storeId}" || path == $"/reserve-table/{storeId}" || path == $"/survey/{storeId}") return true;
            if (path.StartsWith("/track/") && int.TryParse(path[7..], out var oid) && trackable.Contains(oid)) return true;
            return false;
        }
        string Doing(string page)
        {
            var path = (page ?? "").ToLowerInvariant();
            return path.StartsWith("/track/") ? "track" : path.StartsWith("/reserve-table/") ? "reserve" : path.StartsWith("/survey/") ? "survey" : "menu";
        }
        var online = new List<LiveGuestDto>();
        foreach (var g in recent.Where(v => Mine(v.Page)).GroupBy(v => v.UserId > 0 ? "u" + v.UserId : "ip" + v.Ip))
        {
            var last = g.OrderByDescending(v => v.At).First();
            var signed = last.UserId > 0 && !string.IsNullOrWhiteSpace(last.UserName);
            var m = signed ? await CardAsync(last.UserId) : null;
            var ipBits = (last.Ip ?? "").Split('.');
            var who = signed ? last.UserName : (ipBits.Length == 4 ? $"{ipBits[0]}.{ipBits[1]}.{ipBits[2]}.*" : "guest");
            online.Add(new LiveGuestDto(who, Doing(last.Page), last.At, g.Count(), signed ? last.UserId : null,
                m?.Balance ?? 0, m is null ? LoyaltyTiers.Bronze : LoyaltyTiers.For(m.Lifetime, program.SilverAt, program.GoldAt), signed));
        }

        // ---- and the people actually in the room: every table with an open tab
        // A tab left open since last week is a forgotten row, not a guest in the room.
        var freshFrom = DateTime.Now.AddHours(-12);
        var tabs = await db.StoreTabs.AsNoTracking().Where(t => t.RestaurantId == storeId)
            .Select(t => new { t.TableId, t.StoreCustomerId, t.GuestName, t.OpenedAt,
                               Lines = t.Lines.Count, Last = t.Lines.Max(l => (DateTime?)l.AddedAt) })
            .ToListAsync();
        if (tabs.Count > 0)
        {
            var tableNames = await db.StoreTables.AsNoTracking().Where(t => t.RestaurantId == storeId)
                .ToDictionaryAsync(t => t.Id, t => t.Name);
            var custIds = tabs.Where(t => t.StoreCustomerId != null).Select(t => t.StoreCustomerId!.Value).Distinct().ToList();
            var custs = await db.StoreCustomers.AsNoTracking().Where(c => custIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => new { c.UserId, c.Name, c.Phone });
            foreach (var t in tabs.Where(t => (t.Last ?? t.OpenedAt) >= freshFrom))
            {
                var cust = t.StoreCustomerId is { } cid ? custs.GetValueOrDefault(cid) : null;
                var known = cust is not null && cust.Phone != WalkInBook.Phone;
                var m = known ? await CardAsync(cust!.UserId) : null;
                online.Add(new LiveGuestDto(
                    known ? cust!.Name : (string.IsNullOrWhiteSpace(t.GuestName) ? "" : t.GuestName!),
                    "table", t.Last ?? t.OpenedAt, t.Lines, known ? cust!.UserId : null,
                    m?.Balance ?? 0, m is null ? LoyaltyTiers.Bronze : LoyaltyTiers.For(m.Lifetime, program.SilverAt, program.GoldAt),
                    known, tableNames.GetValueOrDefault(t.TableId)));
            }
        }

        return Ok(new LoyaltyLiveDto(online.OrderByDescending(o => o.LastAt).Take(30).ToList(), openDtos,
            DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)));
    }

    private async Task<(string Name, string Phone)?> GuestAsync(int storeId, int userId)
    {
        var c = await db.StoreCustomers.AsNoTracking().FirstOrDefaultAsync(x => x.RestaurantId == storeId && x.UserId == userId);
        if (c is not null) return (c.Name, c.Phone);
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        return u is null ? null : (u.FullName, u.Phone);
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, 200)];

    private static LoyaltyMemberDto ToDto(LoyaltyMemberDoc m, LoyaltyProgramDoc p)
    {
        var need = Math.Max(1, p.RewardPoints);
        return new LoyaltyMemberDto(m.UserId, m.Name, m.Phone, m.Balance, m.Lifetime, m.Redeemed, m.Visits,
            LoyaltyTiers.For(m.Lifetime, p.SilverAt, p.GoldAt), m.JoinedAt, m.LastAt,
            m.Balance >= need ? 0 : need - m.Balance, m.Balance / need);
    }

    private static LoyaltyEventDto ToDto(LoyaltyEventDoc e) =>
        new(e.Id, e.UserId, e.Name, e.Kind, e.Points, e.Amount, e.OrderNumber, e.Note, e.By, e.At);
}
