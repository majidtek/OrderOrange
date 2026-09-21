using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

[Authorize(Roles = "Driver")]
public class DriversController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("state")]
    public async Task<ActionResult<DriverStateDto>> State()
    {
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (profile is null) return NotFound();

        var active = await db.Orders
            .Include(o => o.Customer).Include(o => o.Restaurant).Include(o => o.Driver)
            .Include(o => o.Items).Include(o => o.Events).Include(o => o.Review)
            .FirstOrDefaultAsync(o => o.DriverUserId == CurrentUserId
                && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);

        return new DriverStateDto(profile.IsOnline, profile.VehicleType, active?.ToDto(),
            profile.Verification, profile.VerificationReason);
    }

    // ---------- Document verification (ID card + license, both sides) ----------

    [HttpGet("verification")]
    public async Task<ActionResult<DriverVerificationDto>> Verification()
    {
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (profile is null) return NotFound();
        return new DriverVerificationDto(profile.Verification, profile.VerificationReason, profile.DocsSubmittedAt,
            profile.IdCardFront is not null, profile.IdCardBack is not null,
            profile.LicenseFront is not null, profile.LicenseBack is not null);
    }

    [HttpPost("verification")]
    public async Task<IActionResult> SubmitDocs(SubmitDriverDocsRequest req)
    {
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (profile is null) return NotFound();
        if (profile.Verification == DriverVerificationStatus.Approved)
            return BadRequest(new { message = "You are already verified." });

        var docs = new[] { req.IdCardFront, req.IdCardBack, req.LicenseFront, req.LicenseBack };
        if (docs.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { message = "All four photos are required — ID card and license, front and back." });
        if (docs.Any(d => !d.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "Only photos are accepted." });
        if (docs.Any(d => d.Length > 2_800_000)) // ≈ 2MB per photo as base64
            return BadRequest(new { message = "Each photo must be under 2MB." });

        profile.IdCardFront = req.IdCardFront;
        profile.IdCardBack = req.IdCardBack;
        profile.LicenseFront = req.LicenseFront;
        profile.LicenseBack = req.LicenseBack;
        profile.Verification = DriverVerificationStatus.Pending;
        profile.VerificationReason = null;
        profile.DocsSubmittedAt = DateTime.Now;
        await db.SaveChangesAsync();
        return Ok(new { status = profile.Verification.ToString() });
    }

    [HttpPost("toggle-online")]
    public async Task<IActionResult> ToggleOnline()
    {
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (profile is null) return NotFound();

        // Nobody delivers before an admin approves their documents.
        if (!profile.IsOnline && profile.Verification != DriverVerificationStatus.Approved)
            return BadRequest(new { message = "Your ID and license must be approved before you can go online." });

        if (profile.IsOnline)
        {
            var hasActive = await db.Orders.AnyAsync(o =>
                o.DriverUserId == CurrentUserId && o.Status != OrderStatus.Delivered
                && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Rejected);
            if (hasActive)
                return BadRequest(new { message = "Finish your active delivery before going offline." });
        }

        profile.IsOnline = !profile.IsOnline;
        await db.SaveChangesAsync();
        return Ok(new { isOnline = profile.IsOnline });
    }

    /// <summary>The rider app pushes its GPS position every few seconds while online.</summary>
    [HttpPost("location")]
    public async Task<IActionResult> UpdateLocation(UpdateLocationRequest req)
    {
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (profile is null) return NotFound();
        profile.CurrentLat = req.Lat;
        profile.CurrentLng = req.Lng;
        profile.LocationAt = DateTime.Now;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("earnings")]
    public async Task<EarningsDto> Earnings(int days = 14)
    {
        // A driver earns the delivery fee of every order they complete.
        var delivered = await db.Orders
            .Where(o => o.DriverUserId == CurrentUserId && o.Status == OrderStatus.Delivered)
            .Select(o => new { o.DeliveredAt, o.DeliveryFee })
            .ToListAsync();

        var today = DateTime.Today;
        var weekStart = today.AddDays(-6);

        var perDay = delivered
            .Where(d => d.DeliveredAt is not null)
            .GroupBy(d => d.DeliveredAt!.Value.Date)
            .OrderByDescending(g => g.Key)
            .Take(Math.Clamp(days, 1, 90))
            .Select(g => new DayEarningsDto(g.Key, g.Count(), g.Sum(x => x.DeliveryFee)))
            .ToList();

        return new EarningsDto(
            delivered.Count,
            delivered.Sum(d => d.DeliveryFee),
            delivered.Count(d => d.DeliveredAt >= today),
            delivered.Where(d => d.DeliveredAt >= today).Sum(d => d.DeliveryFee),
            delivered.Where(d => d.DeliveredAt >= weekStart).Sum(d => d.DeliveryFee),
            perDay);
    }
}
