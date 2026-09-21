using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// "I'm here" and "Leaving": every signed-in team member clocks their own shift, and
/// the phone's position rides along with each press. Leave — a few hours or whole days —
/// is asked for here too and decided by whoever holds the staff permission. Payroll
/// picks hours and leave up through <see cref="StoreStaff.UserId"/>.
/// </summary>
public class AttendanceController(AppDbContext db) : ApiControllerBase
{
    private IQueryable<StaffAttendance> Store => db.StaffAttendances.Where(a => a.RestaurantId == CurrentRestaurantId);
    private IQueryable<StaffLeave> Leaves => db.StaffLeaves.Where(l => l.RestaurantId == CurrentRestaurantId);

    // ---------- The person pressing the buttons ----------

    [HttpGet("me")]
    public async Task<ActionResult<MyAttendanceDto>> Me()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        return Ok(await MyStateAsync());
    }

    /// <summary>Clock in. Refused while a shift is still open — press "Leaving" first.</summary>
    [HttpPost("in")]
    public async Task<ActionResult<MyAttendanceDto>> In(ClockRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (await Store.AnyAsync(a => a.UserId == CurrentUserId && a.OutAt == null))
            return BadRequest(new { code = "att.e.open", message = "You are already clocked in." });

        db.StaffAttendances.Add(new StaffAttendance
        {
            RestaurantId = CurrentRestaurantId,
            UserId = CurrentUserId,
            UserName = CurrentUserName,
            InAt = DateTime.Now,
            InLat = Valid(req.Lat, 90), InLng = Valid(req.Lng, 180), InAccuracy = req.Accuracy is > 0 ? Math.Round(req.Accuracy.Value) : null,
        });
        await db.SaveChangesAsync();
        return Ok(await MyStateAsync());
    }

    /// <summary>Clock out of the open shift.</summary>
    [HttpPost("out")]
    public async Task<ActionResult<MyAttendanceDto>> Out(ClockRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var open = await Store.Where(a => a.UserId == CurrentUserId && a.OutAt == null)
            .OrderByDescending(a => a.InAt).FirstOrDefaultAsync();
        if (open is null) return BadRequest(new { code = "att.e.none", message = "You are not clocked in." });

        open.OutAt = DateTime.Now;
        open.OutLat = Valid(req.Lat, 90); open.OutLng = Valid(req.Lng, 180);
        open.OutAccuracy = req.Accuracy is > 0 ? Math.Round(req.Accuracy.Value) : null;
        await db.SaveChangesAsync();
        return Ok(await MyStateAsync());
    }

    // ---------- Leave: asked by the person ----------

    /// <summary>Ask for time off. Hourly = same day with times (≤ 12 h); daily = inclusive dates (≤ 60).</summary>
    [HttpPost("leave")]
    public async Task<ActionResult<MyAttendanceDto>> RequestLeave(LeaveRequestDto req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var kind = req.Kind == "hourly" ? "hourly" : "daily";
        DateTime from, to;
        if (kind == "hourly")
        {
            from = req.From; to = req.To;
            if (to <= from || from.Date != to.Date || (to - from).TotalHours > 12)
                return BadRequest(new { code = "lv.e.span", message = "Hourly leave must be a few hours within one day." });
        }
        else
        {
            from = req.From.Date; to = req.To.Date;
            if (to < from || (to - from).TotalDays > 60)
                return BadRequest(new { code = "lv.e.span", message = "Daily leave must be 1–60 days." });
        }
        if (to < DateTime.Today.AddDays(-31))
            return BadRequest(new { code = "lv.e.past", message = "That date is too far in the past." });
        if (await Leaves.CountAsync(l => l.UserId == CurrentUserId && l.Status == "pending") >= 10)
            return BadRequest(new { code = "lv.e.many", message = "You already have several requests waiting." });

        db.StaffLeaves.Add(new StaffLeave
        {
            RestaurantId = CurrentRestaurantId,
            UserId = CurrentUserId,
            UserName = CurrentUserName,
            Kind = kind,
            FromAt = from,
            ToAt = to,
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()[..Math.Min(300, req.Reason.Trim().Length)],
            Status = "pending",
            RequestedAt = DateTime.Now,
        });
        await db.SaveChangesAsync();
        return Ok(await MyStateAsync());
    }

    /// <summary>Withdraw one's own request while it is still pending.</summary>
    [HttpDelete("leave/{id:int}")]
    public async Task<IActionResult> CancelLeave(int id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var leave = await Leaves.FirstOrDefaultAsync(l => l.Id == id && l.UserId == CurrentUserId);
        if (leave is null) return NotFound();
        if (leave.Status != "pending") return BadRequest(new { code = "lv.e.decided", message = "This request was already decided." });
        db.StaffLeaves.Remove(leave);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- Leave: decided by the store ----------

    [HttpPost("leave/{id:int}/approve")]
    [RequirePerm(Perm.Staff)]
    public Task<IActionResult> Approve(int id, LeaveDecisionRequest req) => DecideAsync(id, "approved", req.Note);

    [HttpPost("leave/{id:int}/reject")]
    [RequirePerm(Perm.Staff)]
    public Task<IActionResult> Reject(int id, LeaveDecisionRequest req) => DecideAsync(id, "rejected", req.Note);

    private async Task<IActionResult> DecideAsync(int id, string status, string? note)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var leave = await Leaves.FirstOrDefaultAsync(l => l.Id == id);
        if (leave is null) return NotFound();
        if (leave.Status != "pending") return BadRequest(new { code = "lv.e.decided", message = "This request was already decided." });
        leave.Status = status;
        leave.DecidedAt = DateTime.Now;
        leave.DecidedBy = CurrentUserName;
        leave.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(300, note.Trim().Length)];
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- The owner's report ----------

    [HttpGet("report")]
    [RequirePerm(Perm.Staff)]
    public async Task<ActionResult<AttendanceReportDto>> Report(DateTime? from = null, DateTime? to = null, int? userId = null)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var start = (from ?? DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek)).Date;
        var end = (to ?? DateTime.Today).Date.AddDays(1);
        if (end <= start) end = start.AddDays(1);
        if ((end - start).TotalDays > 400) start = end.AddDays(-400);

        var query = Store.Where(a => a.InAt >= start && a.InAt < end);
        if (userId is > 0) query = query.Where(a => a.UserId == userId);
        var rows = await query.OrderByDescending(a => a.InAt).Take(2000).ToListAsync();

        var staffNames = await NamesByUserAsync();
        var storePin = await db.Restaurants.Where(r => r.Id == CurrentRestaurantId).Select(r => new { r.Lat, r.Lng }).FirstOrDefaultAsync();
        var dtos = rows.Select(a => ToDto(a, staffNames.GetValueOrDefault(a.UserId), storePin?.Lat, storePin?.Lng)).ToList();

        // Leave: everything still pending (whatever its date), plus what was decided for days inside the range.
        var leaveQuery = Leaves.Where(l => l.Status == "pending" || (l.FromAt < end && l.ToAt >= start));
        if (userId is > 0) leaveQuery = leaveQuery.Where(l => l.UserId == userId);
        var leaves = (await leaveQuery.OrderBy(l => l.Status == "pending" ? 0 : 1).ThenByDescending(l => l.FromAt).Take(500).ToListAsync())
            .Select(l => ToDto(l, staffNames.GetValueOrDefault(l.UserId))).ToList();
        var approvedInRange = leaves.Where(l => l.Status == "approved").ToList();

        var people = dtos.Select(d => (d.UserId, d.UserName, d.StaffName))
            .Concat(approvedInRange.Select(l => (l.UserId, l.UserName, l.StaffName)))
            .GroupBy(x => x.UserId)
            .Select(g =>
            {
                var shifts = dtos.Where(d => d.UserId == g.Key).ToList();
                var lv = approvedInRange.Where(l => l.UserId == g.Key).ToList();
                return new AttendancePersonDto(g.Key, g.First().UserName, g.First().StaffName,
                    shifts.Select(d => d.InAt.Date).Distinct().Count(), Math.Round(shifts.Sum(d => d.Hours), 1),
                    shifts.Count == 0 ? null : shifts.Max(d => d.InAt),
                    Math.Round(lv.Sum(l => l.Hours), 1), lv.Sum(l => l.Days));
            })
            .OrderByDescending(p => p.Hours).ToList();

        var openNow = await Store.CountAsync(a => a.OutAt == null);
        return Ok(new AttendanceReportDto(start, end.AddDays(-1), people, dtos, openNow, leaves, leaves.Count(l => l.Status == "pending"),
            storePin?.Lat, storePin?.Lng));
    }

    // ---------- helpers ----------

    private async Task<MyAttendanceDto> MyStateAsync()
    {
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var mine = await Store.Where(a => a.UserId == CurrentUserId && a.InAt >= monthStart)
            .OrderByDescending(a => a.InAt).ToListAsync();
        var staffName = (await NamesByUserAsync()).GetValueOrDefault(CurrentUserId);
        var leaves = (await Leaves.Where(l => l.UserId == CurrentUserId && (l.Status == "pending" || l.ToAt >= monthStart.AddMonths(-1)))
            .OrderBy(l => l.Status == "pending" ? 0 : 1).ThenByDescending(l => l.FromAt).Take(50).ToListAsync())
            .Select(l => ToDto(l, staffName)).ToList();

        var open = mine.FirstOrDefault(a => a.OutAt == null);
        var todays = mine.Where(a => a.InAt.Date == today).Select(a => ToDto(a, staffName)).ToList();
        return new MyAttendanceDto(
            open is null ? null : ToDto(open, staffName),
            todays,
            Math.Round(todays.Sum(t => t.Hours), 1),
            Math.Round(mine.Sum(a => Hours(a)), 1),
            mine.Select(a => a.InAt.Date).Distinct().Count(),
            leaves);
    }

    /// <summary>Staff-register names keyed by linked login, so the report can show both.</summary>
    private async Task<Dictionary<int, string>> NamesByUserAsync() =>
        (await db.StoreStaff.Where(s => s.RestaurantId == CurrentRestaurantId && s.UserId != null)
            .Select(s => new { s.UserId, s.FullName }).ToListAsync())
        .GroupBy(s => s.UserId!.Value).ToDictionary(g => g.Key, g => g.First().FullName);

    private static double Hours(StaffAttendance a) => Math.Max(0, ((a.OutAt ?? DateTime.Now) - a.InAt).TotalHours);

    private static double? Valid(double? v, double limit) => v is double d && Math.Abs(d) <= limit && d != 0 ? d : null;

    private static AttendanceDto ToDto(StaffAttendance a, string? staffName, double? storeLat = null, double? storeLng = null) =>
        new(a.Id, a.UserId, a.UserName, staffName, a.InAt, a.OutAt,
            a.InLat, a.InLng, a.InAccuracy, a.OutLat, a.OutLng, a.OutAccuracy, Math.Round(Hours(a), 2),
            Metres(storeLat, storeLng, a.InLat, a.InLng), Metres(storeLat, storeLng, a.OutLat, a.OutLng));

    /// <summary>Great-circle distance in metres, or null when either side is unknown.</summary>
    private static double? Metres(double? lat1, double? lng1, double? lat2, double? lng2)
    {
        if (lat1 is null || lng1 is null || lat2 is null || lng2 is null) return null;
        if (lat1 == 0 && lng1 == 0) return null;
        const double R = 6371000;
        double ToRad(double d) => d * Math.PI / 180;
        var dLat = ToRad(lat2.Value - lat1.Value); var dLng = ToRad(lng2.Value - lng1.Value);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(ToRad(lat1.Value)) * Math.Cos(ToRad(lat2.Value)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return Math.Round(2 * R * Math.Asin(Math.Sqrt(h)));
    }

    private static LeaveDto ToDto(StaffLeave l, string? staffName) =>
        new(l.Id, l.UserId, l.UserName, staffName, l.Kind, l.FromAt, l.ToAt,
            l.Kind == "hourly" ? Math.Round((l.ToAt - l.FromAt).TotalHours, 1) : 0,
            l.Kind == "daily" ? (l.ToAt.Date - l.FromAt.Date).Days + 1 : 0,
            l.Reason, l.Status, l.RequestedAt, l.DecidedAt, l.DecidedBy, l.DecisionNote);
}
