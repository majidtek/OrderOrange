using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// A store's own discount codes. The partner side sits behind <see cref="Perm.Discounts"/>;
/// guests read the public offers on the store page and check a code from the cart.
/// The order itself is priced in <see cref="OrdersController.Place"/> with the same engine.
/// </summary>
public class PromotionsController(AppDbContext db, PromotionStore store, PromotionEngine engine) : ApiControllerBase
{
    // ---------- The store's side ----------

    [HttpGet]
    [RequirePerm(Perm.Discounts)]
    public async Task<ActionResult<PromotionPageDto>> List()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var docs = await store.ListAsync(CurrentRestaurantId);
        var stats = await StatsAsync(docs.Select(d => d.Code).ToList());
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var items = docs.Select(d => ToDto(d, stats.GetValueOrDefault(d.Code))).ToList();
        var summary = new PromotionSummaryDto(
            items.Count(i => i.IsActive),
            items.Sum(i => i.UsesThisMonth),
            stats.Values.Sum(s => s.SavedMonth));
        return Ok(new PromotionPageDto(items, summary));
    }

    [HttpPost]
    [RequirePerm(Perm.Discounts)]
    public async Task<ActionResult<PromotionDto>> Create(SavePromotionRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (Validate(req) is { } bad) return BadRequest(new { code = bad });
        if (await store.CountAsync(CurrentRestaurantId) >= 50) return BadRequest(new { code = "dc.e.tooMany" });
        var code = Normalize(req.Code);
        if (await store.CodeTakenAsync(CurrentRestaurantId, code, null)) return BadRequest(new { code = "dc.e.codeTaken" });

        var doc = new PromotionDoc { Id = ObjectId.GenerateNewId(), RestaurantId = CurrentRestaurantId, CreatedAt = DateTime.Now };
        Apply(doc, req, code);
        await store.InsertAsync(doc);
        return Ok(ToDto(doc, default));
    }

    [HttpPut("{id}")]
    [RequirePerm(Perm.Discounts)]
    public async Task<ActionResult<PromotionDto>> Update(string id, SavePromotionRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await store.GetAsync(CurrentRestaurantId, oid);
        if (doc is null) return NotFound();
        if (Validate(req) is { } bad) return BadRequest(new { code = bad });
        var code = Normalize(req.Code);
        if (await store.CodeTakenAsync(CurrentRestaurantId, code, oid)) return BadRequest(new { code = "dc.e.codeTaken" });
        Apply(doc, req, code);
        await store.ReplaceAsync(doc);
        var stats = await StatsAsync([doc.Code]);
        return Ok(ToDto(doc, stats.GetValueOrDefault(doc.Code)));
    }

    /// <summary>Flip a code on or off without opening the editor.</summary>
    [HttpPost("{id}/toggle")]
    [RequirePerm(Perm.Discounts)]
    public async Task<ActionResult<PromotionDto>> Toggle(string id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        var doc = await store.GetAsync(CurrentRestaurantId, oid);
        if (doc is null) return NotFound();
        doc.IsActive = !doc.IsActive; doc.UpdatedAt = DateTime.Now;
        await store.ReplaceAsync(doc);
        var stats = await StatsAsync([doc.Code]);
        return Ok(ToDto(doc, stats.GetValueOrDefault(doc.Code)));
    }

    [HttpDelete("{id}")]
    [RequirePerm(Perm.Discounts)]
    public async Task<IActionResult> Delete(string id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (!ObjectId.TryParse(id, out var oid)) return NotFound();
        await store.DeleteAsync(CurrentRestaurantId, oid);
        return NoContent();
    }

    // ---------- The guest's side ----------

    /// <summary>The offers a store shows on its page — live and public only, no limits exposed.</summary>
    [HttpGet("public/{storeId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<PublicPromotionDto>>> Public(int storeId)
    {
        var docs = await store.PublicAsync(storeId);
        return Ok(docs.Select(d => new PublicPromotionDto(d.Code, d.Title, d.Description, d.Type, d.Value, d.MaxDiscount,
            d.MinOrder, d.Rule, d.RuleValue, d.RuleAmount, d.EndsAt)).ToList());
    }

    /// <summary>
    /// Checked live from the cart. A guest who has not signed in yet is judged with no
    /// history; the order itself re-checks with the real account.
    /// </summary>
    [HttpPost("check")]
    [AllowAnonymous]
    public async Task<ActionResult<PromotionCheckDto>> Check(CheckPromotionRequest req)
    {
        var code = Normalize(req.Code ?? "");
        if (code.Length == 0 || req.RestaurantId <= 0) return Ok(new PromotionCheckDto(false, false, "unknown", [], 0m, null, "Unknown code."));
        var doc = await store.FindAsync(req.RestaurantId, code);
        if (doc is null) return Ok(new PromotionCheckDto(false, false, "unknown", [], 0m, null, "Unknown code."));
        var verdict = await engine.EvaluateAsync(doc, CurrentUserId, req.Subtotal, req.ItemCount, req.OrderType, DateTime.Now);
        return Ok(new PromotionCheckDto(verdict.Ok, true, verdict.Code, verdict.Args, verdict.Discount, doc.Title, verdict.Message));
    }

    // ---------- Plumbing ----------

    private static string Normalize(string code) =>
        new string(code.Trim().ToUpperInvariant().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

    private static string? Validate(SavePromotionRequest req)
    {
        if (Normalize(req.Code ?? "").Length is < 3 or > 24) return "dc.e.code";
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length > 80) return "dc.e.title";
        if (!PromoCatalog.Types.Contains(req.Type)) return "dc.e.type";
        if (req.Type == "percent" && (req.Value <= 0 || req.Value > 100)) return "dc.e.percent";
        if (req.Type == "amount" && req.Value <= 0) return "dc.e.amount";
        if (req.MaxDiscount < 0 || req.MinOrder < 0 || req.RuleAmount < 0 || req.RuleValue < 0) return "dc.e.negative";
        if (!PromoCatalog.Rules.Contains(req.Rule)) return "dc.e.rule";
        if (req.Rule is "orders_month" or "orders_total" or "min_items" && req.RuleValue < 1) return "dc.e.ruleValue";
        if (req.Rule == "spent_total" && req.RuleAmount <= 0) return "dc.e.ruleValue";
        if (!PromoCatalog.OrderTypes.Contains(req.OrderTypes)) return "dc.e.type";
        if (req.StartsAt is DateTime s && req.EndsAt is DateTime e && e < s) return "dc.e.dates";
        if ((req.StartHour is < 0 or > 23) || (req.EndHour is < 0 or > 24)) return "dc.e.hoursBad";
        if (req.MaxUses < 0 || req.MaxUsesPerCustomer < 0) return "dc.e.negative";
        return null;
    }

    private static void Apply(PromotionDoc doc, SavePromotionRequest req, string code)
    {
        doc.Code = code;
        doc.Title = req.Title.Trim();
        doc.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim()[..Math.Min(300, req.Description.Trim().Length)];
        doc.Type = req.Type; doc.Value = req.Value; doc.MaxDiscount = req.Type == "percent" ? req.MaxDiscount : 0m; doc.MinOrder = req.MinOrder;
        doc.Rule = req.Rule; doc.RuleValue = req.RuleValue; doc.RuleAmount = req.RuleAmount;
        doc.OrderTypes = req.OrderTypes;
        doc.Days = (req.Days ?? []).Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d).ToList();
        doc.StartHour = req.StartHour; doc.EndHour = req.EndHour;
        doc.StartsAt = req.StartsAt?.Date; doc.EndsAt = req.EndsAt?.Date.AddDays(1).AddSeconds(-1);
        doc.MaxUses = req.MaxUses; doc.MaxUsesPerCustomer = req.MaxUsesPerCustomer;
        doc.IsActive = req.IsActive; doc.IsPublic = req.IsPublic;
        doc.UpdatedAt = DateTime.Now;
    }

    private sealed record Stat(int Uses, decimal Saved, int UsesMonth, decimal SavedMonth);

    /// <summary>Redemptions per code from the order book — the only counter that cannot drift.</summary>
    private async Task<Dictionary<string, Stat>> StatsAsync(List<string> codes)
    {
        if (codes.Count == 0) return [];
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var rid = CurrentRestaurantId;
        var rows = await db.Orders
            .Where(o => o.RestaurantId == rid && o.CouponCode != null && codes.Contains(o.CouponCode)
                        && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected)
            .GroupBy(o => o.CouponCode!)
            .Select(g => new
            {
                Code = g.Key,
                Uses = g.Count(),
                Saved = g.Sum(o => o.Discount),
                UsesMonth = g.Count(o => o.PlacedAt >= monthStart),
                SavedMonth = g.Where(o => o.PlacedAt >= monthStart).Sum(o => (decimal?)o.Discount) ?? 0m,
            })
            .ToListAsync();
        return rows.ToDictionary(r => r.Code, r => new Stat(r.Uses, r.Saved, r.UsesMonth, r.SavedMonth));
    }

    private static PromotionDto ToDto(PromotionDoc d, Stat? s) => new(
        d.Id.ToString(), d.RestaurantId, d.Code, d.Title, d.Description,
        d.Type, d.Value, d.MaxDiscount, d.MinOrder,
        d.Rule, d.RuleValue, d.RuleAmount,
        d.OrderTypes, d.Days, d.StartHour, d.EndHour,
        d.StartsAt, d.EndsAt, d.MaxUses, d.MaxUsesPerCustomer,
        d.IsActive, d.IsPublic, d.CreatedAt,
        s?.Uses ?? 0, s?.Saved ?? 0m, s?.UsesMonth ?? 0);
}
