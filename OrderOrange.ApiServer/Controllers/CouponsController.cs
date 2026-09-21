using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

public class CouponsController(AppDbContext db) : ApiControllerBase
{
    /// <summary>Checked live from the cart before checkout, then re-checked when the order is placed.</summary>
    [HttpPost("validate")]
    public async Task<ValidateCouponResponse> Validate(ValidateCouponRequest req)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(c => c.Code == req.Code.Trim().ToUpper());
        var problem = OrdersController.CouponProblem(coupon, req.Subtotal);
        if (problem is not null) return new ValidateCouponResponse(false, problem, 0m);
        var discount = Math.Round(req.Subtotal * coupon!.Percent / 100m, 3);
        return new ValidateCouponResponse(true, $"{coupon.Percent:0.#}% off — you save {discount:0.000} OMR.", discount);
    }

    /// <summary>Live promos for the home-page carousel — public, like Talabat's offer banners.</summary>
    [HttpGet("promos")]
    [AllowAnonymous]
    public async Task<List<PromoDto>> Promos() =>
        await db.Coupons
            .Where(c => c.IsActive && c.Uses < c.MaxUses && (c.ExpiresAt == null || c.ExpiresAt > DateTime.Now))
            .OrderByDescending(c => c.Percent)
            .Select(c => new PromoDto(c.Code, c.Percent, c.MinOrder, c.ExpiresAt))
            .ToListAsync();

    // ---------- Admin management ----------

    [HttpGet]
    [Authorize(Roles = "Administrator")]
    public async Task<List<CouponDto>> All() =>
        (await db.Coupons.OrderByDescending(c => c.IsActive).ThenBy(c => c.Code).ToListAsync())
        .Select(c => c.ToDto()).ToList();

    [HttpPost]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Create(SaveCouponRequest req)
    {
        var error = Validate(req);
        if (error is not null) return BadRequest(new { message = error });
        var code = req.Code.Trim().ToUpper();
        if (await db.Coupons.AnyAsync(c => c.Code == code))
            return BadRequest(new { message = "A coupon with this code already exists." });
        db.Coupons.Add(new Coupon
        {
            Code = code, Percent = req.Percent, MinOrder = req.MinOrder,
            ExpiresAt = req.ExpiresAt, IsActive = req.IsActive, MaxUses = req.MaxUses
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Update(int id, SaveCouponRequest req)
    {
        var coupon = await db.Coupons.FindAsync(id);
        if (coupon is null) return NotFound();
        var error = Validate(req);
        if (error is not null) return BadRequest(new { message = error });
        coupon.Code = req.Code.Trim().ToUpper();
        coupon.Percent = req.Percent;
        coupon.MinOrder = req.MinOrder;
        coupon.ExpiresAt = req.ExpiresAt;
        coupon.IsActive = req.IsActive;
        coupon.MaxUses = req.MaxUses;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Delete(int id)
    {
        var coupon = await db.Coupons.FindAsync(id);
        if (coupon is null) return NotFound();
        db.Coupons.Remove(coupon);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string? Validate(SaveCouponRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code)) return "Code is required.";
        if (req.Percent is <= 0 or > 100) return "Percent must be between 1 and 100.";
        if (req.MinOrder < 0) return "Minimum order cannot be negative.";
        if (req.MaxUses <= 0) return "Max uses must be at least 1.";
        return null;
    }
}
