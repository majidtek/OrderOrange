using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The daily / period sales report, computed once for everyone who shows it: the partner's
/// /daily-report and the admin's per-partner and all-partner views (Majed 2026-10-14) read the
/// same numbers. Claim = cancelled invoices; Credit = live invoices not yet paid.
/// </summary>
public static class SalesReport
{
    /// <summary>Clamps a requested span: whole day by default, end after start, at most 93 days.</summary>
    public static (DateTime Start, DateTime End) Span(DateTime? date, DateTime? from, DateTime? to)
    {
        var start = from ?? (date ?? DateTime.Today).Date;
        var end = to ?? start.AddDays(1);
        if (end <= start) end = start.AddMinutes(1);
        if ((end - start).TotalDays > 93) end = start.AddDays(93);
        return (start, end);
    }

    /// <summary>One store's report: summary, dish-by-dish sales and every invoice.</summary>
    public static async Task<DailyInvoicesDto> ForStoreAsync(AppDbContext db, int storeId, DateTime start, DateTime end)
    {
        var rows = await db.Orders
            .Where(o => o.RestaurantId == storeId && o.PlacedAt >= start && o.PlacedAt < end)
            .OrderByDescending(o => o.PlacedAt)
            .Select(o => new
            {
                o.Id, o.Number, o.PlacedAt, o.Total, o.Status, o.TableName, o.IsPaid,
                o.ReplacesOrderId, o.Subtotal, o.TaxAmount, o.Discount, o.DeliveryFee, o.PaymentMethod,
                CustomerName = o.Customer.FullName,
                ItemCount = o.Items.Count,
                Quantity = o.Items.Sum(i => (int?)i.Quantity) ?? 0,
            })
            .ToListAsync();

        var ids = rows.Select(r => r.Id).ToList();
        var replacedBy = await db.InvoiceAudits
            .Where(a => a.RestaurantId == storeId && a.ReplacementOrderId != null && ids.Contains(a.OrderId))
            .ToDictionaryAsync(a => a.OrderId, a => a.ReplacementNumber);

        var live = rows.Where(r => r.Status != OrderStatus.Cancelled).ToList();
        var dead = rows.Where(r => r.Status == OrderStatus.Cancelled).ToList();
        var liveIds = live.Select(r => r.Id).ToList();

        var items = await db.OrderItems
            .Where(i => liveIds.Contains(i.OrderId))
            .GroupBy(i => i.Name)
            .Select(g => new SalesItemDto(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.UnitPrice * i.Quantity)))
            .ToListAsync();
        items = items.OrderByDescending(i => i.Quantity).ThenByDescending(i => i.Amount).ToList();

        var summary = new DailyInvoicesSummaryDto(
            rows.Count, live.Count, dead.Count,
            live.Sum(r => r.Total), dead.Sum(r => r.Total), live.Sum(r => r.ItemCount),
            Quantity: live.Sum(r => r.Quantity),
            ClaimQuantity: dead.Sum(r => r.Quantity),
            Tax: live.Sum(r => r.TaxAmount),
            Discounts: live.Sum(r => r.Discount),
            DeliveryFees: live.Sum(r => r.DeliveryFee),
            Credit: live.Where(r => !r.IsPaid).Sum(r => r.Total),
            Subtotal: live.Sum(r => r.Subtotal),
            Cash: live.Where(r => r.IsPaid && r.PaymentMethod == PaymentMethod.CashOnDelivery).Sum(r => r.Total),
            Card: live.Where(r => r.IsPaid && r.PaymentMethod != PaymentMethod.CashOnDelivery).Sum(r => r.Total));

        return new DailyInvoicesDto(summary, rows.Select(r => new InvoiceRowDto(
            r.Id, r.Number, r.PlacedAt, r.Total, r.Status.ToString(), r.TableName,
            r.CustomerName, r.ItemCount, r.IsPaid,
            r.ReplacesOrderId, replacedBy.GetValueOrDefault(r.Id))).ToList(),
            items, start, end);
    }

    /// <summary>Every store with sales in the span, one summary row each, biggest total first.</summary>
    public static async Task<AllStoresReportDto> AllStoresAsync(AppDbContext db, DateTime start, DateTime end)
    {
        var rows = await db.Orders
            .Where(o => o.PlacedAt >= start && o.PlacedAt < end)
            .Select(o => new
            {
                o.RestaurantId, o.Total, o.Status, o.TaxAmount, o.Discount, o.DeliveryFee, o.IsPaid,
                Quantity = o.Items.Sum(i => (int?)i.Quantity) ?? 0,
            })
            .ToListAsync();
        var storeIds = rows.Select(r => r.RestaurantId).Distinct().ToList();
        var names = await db.Restaurants.Where(r => storeIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name, r.LogoEmoji }).ToDictionaryAsync(r => r.Id);

        var stores = rows.GroupBy(r => r.RestaurantId).Select(g =>
        {
            var live = g.Where(r => r.Status != OrderStatus.Cancelled).ToList();
            var dead = g.Where(r => r.Status == OrderStatus.Cancelled).ToList();
            var n = names.GetValueOrDefault(g.Key);
            return new StoreReportRowDto(g.Key, n?.Name ?? $"#{g.Key}", n?.LogoEmoji ?? "",
                live.Count, dead.Count, live.Sum(r => r.Quantity), live.Sum(r => r.Total),
                live.Sum(r => r.TaxAmount), live.Sum(r => r.Discount), live.Sum(r => r.DeliveryFee),
                live.Where(r => !r.IsPaid).Sum(r => r.Total), dead.Sum(r => r.Total));
        }).OrderByDescending(s => s.Total).ToList();

        return new AllStoresReportDto(start, end, stores);
    }
}
