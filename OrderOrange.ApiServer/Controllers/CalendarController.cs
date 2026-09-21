using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The store's shared calendar, ported from the MajidTekLaw office calendar: every team
/// member reads it, the calendar permission writes it. A reminder is minutes before the
/// start; <see cref="DueReminders"/> hands the bell whatever is due and unacknowledged.
/// </summary>
public class CalendarController(AppDbContext db) : ApiControllerBase
{
    private IQueryable<StoreCalendarEvent> Mine => db.StoreCalendarEvents.Where(e => e.RestaurantId == CurrentRestaurantId);

    /// <summary>Events in a window — by default three months back and nine ahead.</summary>
    [HttpGet]
    public async Task<ActionResult<List<CalendarEventDto>>> List(DateTime? from = null, DateTime? to = null)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var start = from ?? DateTime.Today.AddMonths(-3);
        var end = to ?? DateTime.Today.AddMonths(9);
        var rows = await Mine.Where(e => e.StartAt < end && (e.EndAt ?? e.StartAt) >= start)
            .OrderBy(e => e.StartAt).Take(2000).ToListAsync();
        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpPost]
    [RequirePerm(Perm.Calendar)]
    public async Task<ActionResult<CalendarEventDto>> Create(SaveCalendarEventRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (Validate(req) is { } wrong) return BadRequest(new { code = wrong });
        var e = new StoreCalendarEvent
        {
            RestaurantId = CurrentRestaurantId,
            CreatedByUserId = CurrentUserId,
            CreatedBy = CurrentUserName,
            CreatedAt = DateTime.Now,
        };
        Apply(e, req);
        db.StoreCalendarEvents.Add(e);
        await db.SaveChangesAsync();
        return Ok(ToDto(e));
    }

    [HttpPut("{id:int}")]
    [RequirePerm(Perm.Calendar)]
    public async Task<ActionResult<CalendarEventDto>> Update(int id, SaveCalendarEventRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var e = await Mine.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        if (Validate(req) is { } wrong) return BadRequest(new { code = wrong });
        var timeMoved = e.StartAt != req.StartAt || e.RemindMinutes != req.RemindMinutes;
        Apply(e, req);
        if (timeMoved) e.RemindedAt = null;   // a moved event earns a fresh reminder
        e.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return Ok(ToDto(e));
    }

    [HttpDelete("{id:int}")]
    [RequirePerm(Perm.Calendar)]
    public async Task<IActionResult> Delete(int id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var e = await Mine.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        db.StoreCalendarEvents.Remove(e);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Reminders whose moment has come and that nobody has dismissed yet. The bell
    /// polls this; the first poll after the moment marks the event as reminded so the
    /// system notification rings once, while the item stays listed until the event starts.
    /// </summary>
    [HttpGet("reminders/due")]
    public async Task<ActionResult<List<CalendarReminderDto>>> DueReminders()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var now = DateTime.Now;
        var candidates = await Mine
            .Where(e => e.RemindMinutes != null && e.StartAt > now.AddHours(-2) && e.StartAt < now.AddDays(2))
            .ToListAsync();
        var due = candidates.Where(e => e.StartAt.AddMinutes(-e.RemindMinutes!.Value) <= now).OrderBy(e => e.StartAt).ToList();
        var fresh = false;
        foreach (var e in due.Where(e => e.RemindedAt is null)) { e.RemindedAt = now; fresh = true; }
        if (fresh) await db.SaveChangesAsync();
        return Ok(due.Select(e => new CalendarReminderDto(e.Id, e.Title, e.StartAt, e.AllDay, e.Color,
            (int)Math.Round((e.StartAt - now).TotalMinutes))).ToList());
    }

    /// <summary>Dismiss a reminder for everyone: the bell stops showing it.</summary>
    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var e = await Mine.FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        e.RemindMinutes = null;          // reminded and dismissed — nothing more to say
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- helpers ----------

    private static string? Validate(SaveCalendarEventRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Trim().Length > 120) return "cal.e.title";
        if (req.EndAt is DateTime end && end < req.StartAt) return "cal.e.end";
        if (req.RemindMinutes is < 0 or > 43200) return "cal.e.remind";
        return null;
    }

    private static void Apply(StoreCalendarEvent e, SaveCalendarEventRequest req)
    {
        e.Title = req.Title.Trim();
        e.Details = string.IsNullOrWhiteSpace(req.Details) ? null : req.Details.Trim()[..Math.Min(2000, req.Details.Trim().Length)];
        e.Color = System.Text.RegularExpressions.Regex.IsMatch(req.Color ?? "", "^#[0-9a-fA-F]{6}$") ? req.Color! : "#ff7a1a";
        e.AllDay = req.AllDay;
        e.StartAt = req.AllDay ? req.StartAt.Date : req.StartAt;
        e.EndAt = req.AllDay ? null : req.EndAt;
        e.RemindMinutes = req.RemindMinutes;
    }

    private static CalendarEventDto ToDto(StoreCalendarEvent e) =>
        new(e.Id, e.Title, e.Details, e.Color, e.StartAt, e.EndAt, e.AllDay, e.RemindMinutes, e.RemindedAt, e.CreatedBy, e.CreatedAt);
}
