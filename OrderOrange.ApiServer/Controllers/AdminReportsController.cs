using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Admin → Partner reports (Majed 2026-10-14: "in admin I can see all today report of all
/// partner" / "select partner then I see today report"). Same numbers as the partner's own
/// daily report.
/// </summary>
[ApiController]
[Route("api/admin/reports")]
[Authorize(Roles = "Administrator")]
public sealed class AdminReportsController(AppDbContext db) : ControllerBase
{
    /// <summary>Every partner with sales in the span (today by default).</summary>
    [HttpGet("stores")]
    public Task<AllStoresReportDto> Stores(DateTime? from = null, DateTime? to = null)
    {
        var (start, end) = SalesReport.Span(null, from, to);
        return SalesReport.AllStoresAsync(db, start, end);
    }

    /// <summary>One partner's full report: summary, sales items, invoices.</summary>
    [HttpGet("stores/{storeId:int}")]
    public Task<DailyInvoicesDto> Store(int storeId, DateTime? from = null, DateTime? to = null)
    {
        var (start, end) = SalesReport.Span(null, from, to);
        return SalesReport.ForStoreAsync(db, storeId, start, end);
    }
}