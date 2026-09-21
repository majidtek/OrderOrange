using System.Text.Json;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Invoice management for the partner: cancel one, or replace one with a corrected copy.
///
/// An invoice is never edited in place. "Edit" cancels the original and writes a NEW
/// order carrying the corrected lines — the original keeps its number and its history,
/// the replacement gets fresh ones, and an <see cref="InvoiceAudit"/> row freezes the
/// whole act: who wrote the original, who corrected it, when, why, and both invoices in
/// full. The reports read only that table, so the story cannot be rewritten afterwards.
///
/// Note for the shop: a replaced invoice's printed QR verifies the CANCELLED order.
/// That is correct — the paper in the customer's hand is the one that was taken back.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
public class InvoicesController(AppDbContext db) : ApiControllerBase
{
    private const int EditableDays = 30;

    // ---------- The list the page manages ----------

    [HttpGet]
    public async Task<List<InvoiceRowDto>> List(int days = 7, string? search = null, int skip = 0, int take = 50)
    {
        days = Math.Clamp(days, 1, EditableDays);
        take = Math.Clamp(take, 1, 100);
        var floor = DateTime.Now.AddDays(-days);

        var query = db.Orders
            .Where(o => o.RestaurantId == CurrentRestaurantId && o.PlacedAt >= floor);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.Number.Contains(search));

        var rows = await query
            .OrderByDescending(o => o.PlacedAt)
            .Skip(Math.Max(0, skip)).Take(take)
            .Select(o => new
            {
                o.Id, o.Number, o.PlacedAt, o.Total, o.Status, o.TableName, o.IsPaid,
                o.ReplacesOrderId,
                CustomerName = o.Customer.FullName,
                ItemCount = o.Items.Count,
            })
            .ToListAsync();

        // Which of these were themselves replaced? One query over the audit trail.
        var ids = rows.Select(r => r.Id).ToList();
        var replacedBy = await db.InvoiceAudits
            .Where(a => a.RestaurantId == CurrentRestaurantId
                        && a.ReplacementOrderId != null && ids.Contains(a.OrderId))
            .ToDictionaryAsync(a => a.OrderId, a => a.ReplacementNumber);

        return rows.Select(r => new InvoiceRowDto(
            r.Id, r.Number, r.PlacedAt, r.Total, r.Status.ToString(), r.TableName,
            r.CustomerName, r.ItemCount, r.IsPaid,
            r.ReplacesOrderId, replacedBy.GetValueOrDefault(r.Id))).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InvoiceDetailDto>> Detail(int id)
    {
        var o = await db.Orders.Include(x => x.Items).Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == CurrentRestaurantId);
        if (o is null) return NotFound();
        return new InvoiceDetailDto(o.Id, o.Number, o.PlacedAt, o.Status.ToString(),
            o.Subtotal, o.TaxPercent, o.TaxAmount, o.Total, o.TableName,
            o.Customer.FullName, o.IsPaid,
            o.Items.Select(i => new InvoiceLineDto(i.MenuItemId, i.Name, i.UnitPrice, i.Quantity, i.Notes)).ToList());
    }

    // ---------- Cancel ----------

    [HttpPost("{id:int}/cancel")]
    [RequirePerm(Perm.InvoiceCancel)]
    public async Task<IActionResult> Cancel(int id, CancelInvoiceRequest req)
    {
        var o = await db.Orders.Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == CurrentRestaurantId);
        if (o is null) return NotFound();
        if (o.Status == OrderStatus.Cancelled)
            return BadRequest(new { message = "This invoice is already cancelled." });

        var before = Snapshot(o, await AuthorOf(o));

        o.Status = OrderStatus.Cancelled;
        db.Set<OrderEvent>().Add(new OrderEvent
        {
            OrderId = o.Id, Status = OrderStatus.Cancelled, At = DateTime.Now, By = CurrentUserName,
        });
        db.InvoiceAudits.Add(new InvoiceAudit
        {
            RestaurantId = CurrentRestaurantId,
            OrderId = o.Id, OrderNumber = o.Number,
            Action = "cancel",
            ActorUserId = CurrentUserId, ActorName = CurrentUserName,
            Reason = Trim(req.Reason),
            BeforeJson = JsonSerializer.Serialize(before),
            At = DateTime.Now,
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Edit = cancel + rewrite ----------

    [HttpPost("{id:int}/replace")]
    [RequirePerm(Perm.InvoiceEdit)]
    public async Task<ActionResult<InvoiceRowDto>> Replace(int id, ReplaceInvoiceRequest req)
    {
        if (req.Lines is not { Count: > 0 })
            return BadRequest(new { message = "The corrected invoice needs at least one line." });
        if (req.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || string.IsNullOrWhiteSpace(l.Name)))
            return BadRequest(new { message = "Every line needs a name, a quantity and a price." });

        var o = await db.Orders.Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && x.RestaurantId == CurrentRestaurantId);
        if (o is null) return NotFound();
        if (o.Status == OrderStatus.Cancelled)
            return BadRequest(new { message = "A cancelled invoice cannot be edited — write a new one." });
        if (await db.InvoiceAudits.AnyAsync(a => a.OrderId == o.Id && a.ReplacementOrderId != null))
            return BadRequest(new { message = "This invoice was already replaced — edit the replacement instead." });

        var before = Snapshot(o, await AuthorOf(o));

        // The corrected copy inherits everything that identifies the sale — customer,
        // table, payment method — and recomputes only the money from its new lines.
        var subtotal = req.Lines.Sum(l => l.UnitPrice * l.Quantity);
        var tax = Math.Round(subtotal * o.TaxPercent / 100m, 3);
        var replacement = new Order
        {
            CustomerId = o.CustomerId,
            RestaurantId = o.RestaurantId,
            Status = o.Status,
            PaymentMethod = o.PaymentMethod,
            OrderType = o.OrderType,
            DeliveryAddress = o.DeliveryAddress,
            Subtotal = subtotal,
            DeliveryFee = o.DeliveryFee,
            ServiceFee = o.ServiceFee,
            Discount = 0m,
            TaxPercent = o.TaxPercent,
            TaxAmount = tax,
            Total = subtotal + o.DeliveryFee + o.ServiceFee + tax,
            EstimatedMinutes = o.EstimatedMinutes,
            TableName = o.TableName,
            IsPaid = req.MarkPaid,
            PaymentRef = req.MarkPaid ? $"EDIT-{DateTime.Now:yyyyMMddHHmmss}" : o.PaymentRef,
            PlacedAt = DateTime.Now,
            ReplacesOrderId = o.Id,
            Items = req.Lines.Select(l => new OrderItem
            {
                MenuItemId = l.MenuItemId, Name = l.Name.Trim(),
                UnitPrice = l.UnitPrice, Quantity = l.Quantity, Notes = Trim(l.Notes),
            }).ToList(),
            Events = { new OrderEvent { Status = o.Status, At = DateTime.Now, By = CurrentUserName } },
        };
        db.Orders.Add(replacement);

        o.Status = OrderStatus.Cancelled;
        db.Set<OrderEvent>().Add(new OrderEvent
        {
            OrderId = o.Id, Status = OrderStatus.Cancelled, At = DateTime.Now, By = CurrentUserName,
        });
        await db.SaveChangesAsync();               // the replacement needs its id first

        replacement.Number = $"MF-{1000 + replacement.Id}";
        db.InvoiceAudits.Add(new InvoiceAudit
        {
            RestaurantId = CurrentRestaurantId,
            OrderId = o.Id, OrderNumber = o.Number,
            ReplacementOrderId = replacement.Id, ReplacementNumber = replacement.Number,
            Action = "edit",
            ActorUserId = CurrentUserId, ActorName = CurrentUserName,
            Reason = Trim(req.Reason),
            BeforeJson = JsonSerializer.Serialize(before),
            AfterJson = JsonSerializer.Serialize(Snapshot(replacement, (CurrentUserName, replacement.PlacedAt))),
            At = DateTime.Now,
        });
        await db.SaveChangesAsync();

        return new InvoiceRowDto(replacement.Id, replacement.Number, replacement.PlacedAt,
            replacement.Total, replacement.Status.ToString(), replacement.TableName,
            "", replacement.Items.Count, replacement.IsPaid, o.Id, null);
    }

    // ---------- The daily report: one day's invoices, in full ----------

    [HttpGet("daily")]
    [RequirePerm(Perm.Reports)]
    public async Task<DailyInvoicesDto> Daily(DateTime? date = null)
    {
        var day = (date ?? DateTime.Today).Date;
        var next = day.AddDays(1);

        var rows = await db.Orders
            .Where(o => o.RestaurantId == CurrentRestaurantId && o.PlacedAt >= day && o.PlacedAt < next)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new
            {
                o.Id, o.Number, o.PlacedAt, o.Total, o.Status, o.TableName, o.IsPaid,
                o.ReplacesOrderId,
                CustomerName = o.Customer.FullName,
                ItemCount = o.Items.Count,
            })
            .ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var replacedBy = await db.InvoiceAudits
            .Where(a => a.RestaurantId == CurrentRestaurantId
                        && a.ReplacementOrderId != null && ids.Contains(a.OrderId))
            .ToDictionaryAsync(a => a.OrderId, a => a.ReplacementNumber);

        var live = rows.Where(r => r.Status != OrderStatus.Cancelled).ToList();
        var summary = new DailyInvoicesSummaryDto(
            rows.Count,
            live.Count,
            rows.Count - live.Count,
            live.Sum(r => r.Total),
            rows.Where(r => r.Status == OrderStatus.Cancelled).Sum(r => r.Total),
            live.Sum(r => r.ItemCount));

        return new DailyInvoicesDto(summary, rows.Select(r => new InvoiceRowDto(
            r.Id, r.Number, r.PlacedAt, r.Total, r.Status.ToString(), r.TableName,
            r.CustomerName, r.ItemCount, r.IsPaid,
            r.ReplacesOrderId, replacedBy.GetValueOrDefault(r.Id))).ToList());
    }

    // ---------- Report 1: what was removed ----------

    [HttpGet("cancelled")]
    [RequirePerm(Perm.Reports)]
    public async Task<List<CancelledInvoiceDto>> Cancelled(DateTime? from = null, DateTime? to = null,
        int skip = 0, int take = 25)
    {
        var (lo, hi) = Window(from, to);
        var rows = await db.InvoiceAudits
            .Where(a => a.RestaurantId == CurrentRestaurantId && a.At >= lo && a.At < hi)
            .OrderByDescending(a => a.At)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        return rows.Select(a =>
        {
            var before = Parse(a.BeforeJson);
            return new CancelledInvoiceDto(
                a.OrderNumber, a.At, a.ActorName, a.Reason,
                before?.Total ?? 0, before?.Lines.Sum(l => l.Quantity) ?? 0,
                before?.At ?? a.At, before?.By ?? "",
                a.ReplacementNumber);
        }).ToList();
    }

    // ---------- Report 2: the full story of every edit ----------

    [HttpGet("edits")]
    [RequirePerm(Perm.Reports)]
    public async Task<List<InvoiceEditDto>> Edits(DateTime? from = null, DateTime? to = null,
        int skip = 0, int take = 25)
    {
        var (lo, hi) = Window(from, to);
        var rows = await db.InvoiceAudits
            .Where(a => a.RestaurantId == CurrentRestaurantId
                        && a.Action == "edit" && a.At >= lo && a.At < hi)
            .OrderByDescending(a => a.At)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        return rows
            .Select(a =>
            {
                var before = Parse(a.BeforeJson);
                var after = a.AfterJson is null ? null : Parse(a.AfterJson);
                return before is null || after is null
                    ? null
                    : new InvoiceEditDto(a.OrderNumber, a.ReplacementNumber ?? "", a.At,
                                         a.ActorName, a.Reason, before, after);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
    }

    // ---------- Plumbing ----------

    private static (DateTime, DateTime) Window(DateTime? from, DateTime? to)
    {
        var lo = from?.Date ?? DateTime.Today.AddDays(-30);
        var hi = (to?.Date ?? DateTime.Today).AddDays(1);
        return (lo, hi);
    }

    /// <summary>Who first wrote this invoice, and when — from its earliest event.</summary>
    private async Task<(string By, DateTime At)> AuthorOf(Order o)
    {
        var first = await db.Set<OrderEvent>()
            .Where(e => e.OrderId == o.Id)
            .OrderBy(e => e.At)
            .Select(e => new { e.By, e.At })
            .FirstOrDefaultAsync();
        return (first?.By ?? "", first?.At ?? o.PlacedAt);
    }

    private static InvoiceSnapshotDto Snapshot(Order o, (string By, DateTime At) author) =>
        new(o.Number, author.At, author.By, o.Subtotal, o.TaxAmount, o.Total,
            o.Items.Select(i => new InvoiceLineDto(i.MenuItemId, i.Name, i.UnitPrice, i.Quantity, i.Notes)).ToList());

    private static InvoiceSnapshotDto? Parse(string json)
    {
        try { return JsonSerializer.Deserialize<InvoiceSnapshotDto>(json); }
        catch { return null; }
    }

    private static string? Trim(string? s)
    {
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s[..Math.Min(s.Length, 400)];
    }
}
