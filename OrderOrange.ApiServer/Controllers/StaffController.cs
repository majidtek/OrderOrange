using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The store's team and their salaries. Staff records and monthly payroll runs are
/// the owner's own data — always scoped to their restaurant.
/// </summary>
[RequirePerm(Perm.Staff)]
[Authorize(Roles = "RestaurantOwner")]
public class StaffController(AppDbContext db) : ApiControllerBase
{
    private static readonly string[] Roles =
        ["barista", "chef", "cook", "cashier", "waiter", "manager", "cleaner", "driver", "helper", "other"];

    private static readonly string[] Methods = ["cash", "bank", "transfer"];

    private IQueryable<StoreStaff> Mine => db.StoreStaff.Where(s => s.RestaurantId == CurrentRestaurantId);

    // ---------- Team ----------

    [HttpGet]
    public async Task<StaffPageDto> List(string status = "active", string? search = null)
    {
        var now = DateTime.Today;
        var query = Mine;
        if (status == "active") query = query.Where(s => s.IsActive);
        else if (status == "inactive") query = query.Where(s => !s.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s => s.FullName.Contains(term) || (s.Phone != null && s.Phone.Contains(term)));
        }

        var total = await query.CountAsync();
        var activeCount = await Mine.CountAsync(s => s.IsActive);
        var payroll = await Mine.Where(s => s.IsActive).SumAsync(s => (decimal?)s.MonthlySalary) ?? 0;

        var paidIds = await db.SalaryPayments
            .Where(p => p.RestaurantId == CurrentRestaurantId && p.PeriodYear == now.Year && p.PeriodMonth == now.Month)
            .Select(p => p.StaffId).ToListAsync();

        var rows = await query.OrderByDescending(s => s.IsActive).ThenBy(s => s.FullName)
            .Select(s => new StaffDto(s.Id, s.FullName, s.Role, s.Phone, s.NationalId, s.Photo,
                s.MonthlySalary, s.HiredOn, s.IsActive, s.Notes, false, s.UserId))
            .ToListAsync();

        return new StaffPageDto(total, activeCount, payroll,
            rows.Select(r => r with { PaidThisPeriod = paidIds.Contains(r.Id) }).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create(SaveStaffRequest req)
    {
        var error = Validate(req);
        if (error is not null) return BadRequest(new { message = error });
        db.StoreStaff.Add(new StoreStaff
        {
            RestaurantId = CurrentRestaurantId,
            FullName = req.FullName.Trim(),
            Role = Roles.Contains(req.Role) ? req.Role : "other",
            Phone = Clean(req.Phone, 40),
            NationalId = Clean(req.NationalId, 40),
            Photo = string.IsNullOrEmpty(req.Photo) ? null : req.Photo,
            MonthlySalary = req.MonthlySalary,
            HiredOn = req.HiredOn.Date,
            IsActive = req.IsActive,
            Notes = Clean(req.Notes, 300),
            UserId = req.UserId is > 0 ? req.UserId : null,
            CreatedAt = DateTime.Now
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveStaffRequest req)
    {
        var staff = await Mine.FirstOrDefaultAsync(s => s.Id == id);
        if (staff is null) return NotFound();
        var error = Validate(req);
        if (error is not null) return BadRequest(new { message = error });

        staff.FullName = req.FullName.Trim();
        staff.Role = Roles.Contains(req.Role) ? req.Role : "other";
        staff.Phone = Clean(req.Phone, 40);
        staff.NationalId = Clean(req.NationalId, 40);
        if (req.Photo is not null) staff.Photo = req.Photo.Length == 0 ? null : req.Photo;
        staff.MonthlySalary = req.MonthlySalary;
        staff.HiredOn = req.HiredOn.Date;
        staff.IsActive = req.IsActive;
        staff.Notes = Clean(req.Notes, 300);
        staff.UserId = req.UserId is > 0 ? req.UserId : null;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var staff = await Mine.FirstOrDefaultAsync(s => s.Id == id);
        if (staff is null) return NotFound();
        var paid = await db.SalaryPayments.AnyAsync(p => p.StaffId == id);
        if (paid)
        {
            // Keep salary history intact — retire instead of deleting.
            staff.IsActive = false;
            await db.SaveChangesAsync();
            return Ok(new { retired = true });
        }
        db.StoreStaff.Remove(staff);
        await db.SaveChangesAsync();
        return Ok(new { retired = false });
    }

    // ---------- Payroll ----------

    /// <summary>Who still needs paying this month, and who is already paid.</summary>
    [RequirePerm(Perm.Payroll)]
    [HttpGet("payroll")]
    public async Task<PayrollDto> Payroll(int? year = null, int? month = null)
    {
        var today = DateTime.Today;
        var y = year ?? today.Year;
        var m = Math.Clamp(month ?? today.Month, 1, 12);

        var active = await Mine.Where(s => s.IsActive)
            .Select(s => new StaffDto(s.Id, s.FullName, s.Role, s.Phone, s.NationalId, s.Photo,
                s.MonthlySalary, s.HiredOn, s.IsActive, s.Notes, false, s.UserId))
            .ToListAsync();

        // Hours clocked this month, per linked login — attendance feeding payroll.
        var monthStart = new DateTime(y, m, 1);
        var monthEnd = monthStart.AddMonths(1);
        var shifts = await db.StaffAttendances
            .Where(a => a.RestaurantId == CurrentRestaurantId && a.InAt >= monthStart && a.InAt < monthEnd)
            .Select(a => new { a.UserId, a.InAt, a.OutAt })
            .ToListAsync();
        var hoursByUser = shifts
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(a => ((a.OutAt ?? DateTime.Now) - a.InAt).TotalHours), 1));
        // Approved leave in the same month, per login.
        var leaves = await db.StaffLeaves
            .Where(l => l.RestaurantId == CurrentRestaurantId && l.Status == "approved" && l.FromAt < monthEnd && l.ToAt >= monthStart)
            .Select(l => new { l.UserId, l.Kind, l.FromAt, l.ToAt })
            .ToListAsync();
        var leaveByUser = leaves.GroupBy(l => l.UserId).ToDictionary(g => g.Key, g => (
            Hours: Math.Round(g.Where(l => l.Kind == "hourly").Sum(l => (l.ToAt - l.FromAt).TotalHours), 1),
            Days: g.Where(l => l.Kind == "daily").Sum(l => (l.ToAt.Date - l.FromAt.Date).Days + 1)));
        active = active
            .Select(s =>
            {
                if (s.UserId is not int uid) return s;
                var h = hoursByUser.TryGetValue(uid, out var hw) ? hw : 0;
                var lv = leaveByUser.TryGetValue(uid, out var l) ? l : (Hours: 0, Days: 0);
                return s with { HoursWorked = h, LeaveHours = lv.Hours, LeaveDays = lv.Days };
            })
            .ToList();

        // Project after materialising — EF cannot order by a member of a constructed record.
        var payments = (await db.SalaryPayments
            .Where(p => p.RestaurantId == CurrentRestaurantId && p.PeriodYear == y && p.PeriodMonth == m)
            .Join(db.StoreStaff, p => p.StaffId, s => s.Id, (p, s) => new { P = p, s.FullName, s.Role })
            .ToListAsync())
            .OrderBy(x => x.FullName)
            .Select(x => new SalaryPaymentDto(x.P.Id, x.P.StaffId, x.FullName, x.Role,
                x.P.PeriodYear, x.P.PeriodMonth, x.P.BaseSalary, x.P.Bonus, x.P.Deduction,
                x.P.NetPaid, x.P.Method, x.P.PaidAt, x.P.Notes))
            .ToList();

        var paidIds = payments.Select(p => p.StaffId).ToHashSet();
        var due = active.Where(s => !paidIds.Contains(s.Id)).OrderBy(s => s.FullName).ToList();

        var expected = active.Sum(s => s.MonthlySalary);
        var paidTotal = payments.Sum(p => p.NetPaid);

        return new PayrollDto(y, m, expected, paidTotal, due.Sum(s => s.MonthlySalary),
            payments.Count, due.Count, due, payments);
    }

    [RequirePerm(Perm.Payroll)]
    [HttpPost("payroll/pay")]
    public async Task<IActionResult> Pay(PaySalaryRequest req)
    {
        var staff = await Mine.FirstOrDefaultAsync(s => s.Id == req.StaffId);
        if (staff is null) return NotFound(new { message = "Staff member not found." });
        var m = Math.Clamp(req.Month, 1, 12);
        if (req.Bonus < 0 || req.Deduction < 0) return BadRequest(new { message = "Bonus and deduction cannot be negative." });

        var already = await db.SalaryPayments.AnyAsync(p => p.StaffId == staff.Id
            && p.PeriodYear == req.Year && p.PeriodMonth == m);
        if (already) return BadRequest(new { message = $"{staff.FullName} is already paid for that month." });

        var net = staff.MonthlySalary + req.Bonus - req.Deduction;
        if (net < 0) return BadRequest(new { message = "The deduction is larger than the salary." });

        db.SalaryPayments.Add(new SalaryPayment
        {
            RestaurantId = CurrentRestaurantId,
            StaffId = staff.Id,
            PeriodYear = req.Year,
            PeriodMonth = m,
            BaseSalary = staff.MonthlySalary,
            Bonus = req.Bonus,
            Deduction = req.Deduction,
            NetPaid = net,
            Method = Methods.Contains(req.Method) ? req.Method : "cash",
            PaidAt = DateTime.Now,
            Notes = Clean(req.Notes, 300)
        });
        await db.SaveChangesAsync();
        return Ok(new { netPaid = net });
    }

    [HttpDelete("payroll/{paymentId:int}")]
    public async Task<IActionResult> UndoPayment(int paymentId)
    {
        var payment = await db.SalaryPayments
            .FirstOrDefaultAsync(p => p.Id == paymentId && p.RestaurantId == CurrentRestaurantId);
        if (payment is null) return NotFound();
        db.SalaryPayments.Remove(payment);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Salary report over a window (default: last 12 months), grouped in SQL.</summary>
    [RequirePerm(Perm.Payroll)]
    [HttpGet("salary-report")]
    public async Task<SalaryReportDto> SalaryReport(DateTime? from = null, DateTime? to = null)
    {
        var today = DateTime.Today;
        var start = (from ?? new DateTime(today.Year, today.Month, 1).AddMonths(-11)).Date;
        var endEx = (to ?? today).Date.AddDays(1);

        var scope = db.SalaryPayments.Where(p => p.RestaurantId == CurrentRestaurantId
            && p.PaidAt >= start && p.PaidAt < endEx);

        var total = await scope.SumAsync(p => (decimal?)p.NetPaid) ?? 0;
        var bonuses = await scope.SumAsync(p => (decimal?)p.Bonus) ?? 0;
        var deductions = await scope.SumAsync(p => (decimal?)p.Deduction) ?? 0;
        var count = await scope.CountAsync();

        var months = (await scope.GroupBy(p => new { p.PeriodYear, p.PeriodMonth })
            .Select(g => new { g.Key.PeriodYear, g.Key.PeriodMonth, Payments = g.Count(), Total = g.Sum(p => p.NetPaid) })
            .ToListAsync())
            .OrderByDescending(x => x.PeriodYear).ThenByDescending(x => x.PeriodMonth)
            .Select(x => new SalaryMonthRowDto(
                new DateTime(x.PeriodYear, x.PeriodMonth, 1).ToString("MMMM yyyy"),
                x.PeriodYear, x.PeriodMonth, x.Payments, x.Total))
            .ToList();

        var staff = (await scope.Join(db.StoreStaff, p => p.StaffId, s => s.Id, (p, s) => new { p.StaffId, s.FullName, s.Role, p.NetPaid })
            .GroupBy(x => new { x.StaffId, x.FullName, x.Role })
            .Select(g => new { g.Key.StaffId, g.Key.FullName, g.Key.Role, Payments = g.Count(), Total = g.Sum(x => x.NetPaid) })
            .ToListAsync())
            .OrderByDescending(x => x.Total)
            .Select(x => new SalaryStaffRowDto(x.StaffId, x.FullName, x.Role, x.Payments, x.Total))
            .ToList();

        var recent = (await scope
            .OrderByDescending(p => p.PaidAt)
            .Take(25)
            .Join(db.StoreStaff, p => p.StaffId, s => s.Id, (p, s) => new { P = p, s.FullName, s.Role })
            .ToListAsync())
            .OrderByDescending(x => x.P.PaidAt)
            .Select(x => new SalaryPaymentDto(x.P.Id, x.P.StaffId, x.FullName, x.Role,
                x.P.PeriodYear, x.P.PeriodMonth, x.P.BaseSalary, x.P.Bonus, x.P.Deduction,
                x.P.NetPaid, x.P.Method, x.P.PaidAt, x.P.Notes))
            .ToList();

        return new SalaryReportDto(total, bonuses, deductions, count, months, staff, recent);
    }

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    private static string? Validate(SaveStaffRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FullName)) return "A name is required.";
        if (req.MonthlySalary < 0) return "Salary cannot be negative.";
        if (!string.IsNullOrEmpty(req.Photo) && !req.Photo.StartsWith("data:image/")) return "Only images are allowed.";
        if (req.Photo is { Length: > 200_000 }) return "The photo is too large.";
        return null;
    }
}
