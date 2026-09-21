using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The store's own expense book — electricity, rent, phone, salaries. Always scoped
/// to the signed-in owner's restaurant; aggregation happens in SQL and lists are paged.
/// </summary>
[RequirePerm(Perm.Bills)]
[Authorize(Roles = "RestaurantOwner")]
public class BillsController(AppDbContext db) : ApiControllerBase
{
    private static readonly string[] Categories =
        ["electricity", "water", "phone", "internet", "rent", "salary", "gas", "supplies", "maintenance", "tax", "other"];

    private IQueryable<StoreBill> Mine => db.StoreBills.Where(b => b.RestaurantId == CurrentRestaurantId);

    /// <summary>status: all|unpaid|paid|overdue · category: all or one of the known keys.</summary>
    [HttpGet]
    public async Task<StoreBillPageDto> List(string status = "all", string category = "all",
        string? search = null, int skip = 0, int take = 20, DateTime? from = null, DateTime? to = null)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var today = DateTime.Today;

        var query = Mine;
        if (from.HasValue) query = query.Where(b => b.DueDate >= from.Value.Date);
        if (to.HasValue) query = query.Where(b => b.DueDate < to.Value.Date.AddDays(1));
        if (category != "all" && Categories.Contains(category)) query = query.Where(b => b.Category == category);
        query = status switch
        {
            "unpaid" => query.Where(b => !b.IsPaid),
            "paid" => query.Where(b => b.IsPaid),
            "overdue" => query.Where(b => !b.IsPaid && b.DueDate < today),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(b => b.Title.Contains(term)
                || (b.Vendor != null && b.Vendor.Contains(term))
                || (b.Reference != null && b.Reference.Contains(term)));
        }

        var total = await query.CountAsync();
        var totalAmount = await query.SumAsync(b => (decimal?)b.Amount) ?? 0;
        var unpaidAmount = await query.Where(b => !b.IsPaid).SumAsync(b => (decimal?)b.Amount) ?? 0;
        var overdueAmount = await query.Where(b => !b.IsPaid && b.DueDate < today).SumAsync(b => (decimal?)b.Amount) ?? 0;

        var rows = await query
            .OrderBy(b => b.IsPaid).ThenBy(b => b.DueDate)
            .Skip(skip).Take(take)
            .Select(b => new StoreBillDto(b.Id, b.Category, b.Title, b.Vendor, b.Reference,
                b.Amount, b.DueDate, b.IsPaid, b.PaidAt, b.Recurring, b.Notes))
            .ToListAsync();

        return new StoreBillPageDto(total, totalAmount, unpaidAmount, overdueAmount, rows);
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaveStoreBillRequest req)
    {
        var error = Validate(req);
        if (error is not null) return BadRequest(new { message = error });
        db.StoreBills.Add(new StoreBill
        {
            RestaurantId = CurrentRestaurantId,
            Category = Categories.Contains(req.Category) ? req.Category : "other",
            Title = req.Title.Trim(),
            Vendor = Clean(req.Vendor, 120),
            Reference = Clean(req.Reference, 60),
            Amount = req.Amount,
            DueDate = req.DueDate.Date,
            IsPaid = req.IsPaid,
            PaidAt = req.IsPaid ? DateTime.Now : null,
            Recurring = req.Recurring,
            Notes = Clean(req.Notes, 300),
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveStoreBillRequest req)
    {
        var bill = await Mine.FirstOrDefaultAsync(b => b.Id == id);
        if (bill is null) return NotFound();
        var error = Validate(req);
        if (error is not null) return BadRequest(new { message = error });

        bill.Category = Categories.Contains(req.Category) ? req.Category : "other";
        bill.Title = req.Title.Trim();
        bill.Vendor = Clean(req.Vendor, 120);
        bill.Reference = Clean(req.Reference, 60);
        bill.Amount = req.Amount;
        bill.DueDate = req.DueDate.Date;
        if (bill.IsPaid != req.IsPaid) bill.PaidAt = req.IsPaid ? DateTime.Now : null;
        bill.IsPaid = req.IsPaid;
        bill.Recurring = req.Recurring;
        bill.Notes = Clean(req.Notes, 300);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Marks paid/unpaid. A recurring bill marked paid spawns next month's copy.</summary>
    [HttpPost("{id:int}/toggle-paid")]
    public async Task<IActionResult> TogglePaid(int id)
    {
        var bill = await Mine.FirstOrDefaultAsync(b => b.Id == id);
        if (bill is null) return NotFound();

        bill.IsPaid = !bill.IsPaid;
        bill.PaidAt = bill.IsPaid ? DateTime.Now : null;

        if (bill.IsPaid && bill.Recurring)
        {
            var next = bill.DueDate.AddMonths(1);
            var exists = await Mine.AnyAsync(b => b.Recurring && b.Category == bill.Category
                && b.Title == bill.Title && b.DueDate == next);
            if (!exists)
            {
                db.StoreBills.Add(new StoreBill
                {
                    RestaurantId = CurrentRestaurantId,
                    Category = bill.Category,
                    Title = bill.Title,
                    Vendor = bill.Vendor,
                    Reference = bill.Reference,
                    Amount = bill.Amount,
                    DueDate = next,
                    Recurring = true,
                    Notes = bill.Notes,
                    CreatedAt = DateTime.Now
                });
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { isPaid = bill.IsPaid });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var bill = await Mine.FirstOrDefaultAsync(b => b.Id == id);
        if (bill is null) return NotFound();
        db.StoreBills.Remove(bill);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Report over a window (default: last 12 months) — grouped in SQL, three breakdowns.</summary>
    [HttpGet("report")]
    public async Task<BillReportDto> Report(DateTime? from = null, DateTime? to = null)
    {
        var today = DateTime.Today;
        var start = (from ?? new DateTime(today.Year, today.Month, 1).AddMonths(-11)).Date;
        var endEx = (to ?? today).Date.AddDays(1);

        var scope = Mine.Where(b => b.DueDate >= start && b.DueDate < endEx);

        var count = await scope.CountAsync();
        var total = await scope.SumAsync(b => (decimal?)b.Amount) ?? 0;
        var paid = await scope.Where(b => b.IsPaid).SumAsync(b => (decimal?)b.Amount) ?? 0;
        var overdue = await scope.Where(b => !b.IsPaid && b.DueDate < today).SumAsync(b => (decimal?)b.Amount) ?? 0;

        var categories = (await scope.GroupBy(b => b.Category)
            .Select(g => new
            {
                Category = g.Key,
                Count = g.Count(),
                Total = g.Sum(b => b.Amount),
                Paid = g.Sum(b => b.IsPaid ? b.Amount : 0)
            })
            .ToListAsync())
            .Select(x => new BillCategoryRowDto(x.Category, x.Count, x.Total, x.Paid, x.Total - x.Paid))
            .OrderByDescending(c => c.Total)
            .ToList();

        var byMonth = await scope.GroupBy(b => new { b.DueDate.Year, b.DueDate.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Count = g.Count(),
                Total = g.Sum(b => b.Amount),
                Paid = g.Sum(b => b.IsPaid ? b.Amount : 0)
            })
            .ToListAsync();

        var months = new List<BillMonthRowDto>();
        var cursor = new DateTime(start.Year, start.Month, 1);
        var last = new DateTime(endEx.AddDays(-1).Year, endEx.AddDays(-1).Month, 1);
        while (cursor <= last)
        {
            var slot = byMonth.FirstOrDefault(m => m.Year == cursor.Year && m.Month == cursor.Month);
            if (slot is not null)
            {
                months.Add(new BillMonthRowDto(cursor.ToString("MMMM yyyy"), slot.Count, slot.Total, slot.Paid, slot.Total - slot.Paid));
            }
            cursor = cursor.AddMonths(1);
        }

        var vendors = (await scope.Where(b => b.Vendor != null && b.Vendor != "")
            .GroupBy(b => b.Vendor!)
            .Select(g => new { Vendor = g.Key, Count = g.Count(), Total = g.Sum(b => b.Amount) })
            .ToListAsync())
            .Select(v => new BillVendorRowDto(v.Vendor, v.Count, v.Total))
            .OrderByDescending(v => v.Total)
            .Take(10)
            .ToList();

        return new BillReportDto(total, paid, total - paid, overdue, count, categories, months, vendors);
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    private static string? Validate(SaveStoreBillRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return "A title is required.";
        if (req.Amount <= 0) return "Amount must be greater than zero.";
        return null;
    }
}
