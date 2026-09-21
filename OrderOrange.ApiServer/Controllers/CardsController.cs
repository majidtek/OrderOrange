using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Saved cards for the TEST payment flow. Deliberately a toy gateway: any syntactically
/// valid card is accepted, nothing is charged, and only brand/last4/expiry are stored.
/// </summary>
[Authorize(Roles = "Customer,RestaurantOwner,Driver")]
public class CardsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<List<CardDto>> Mine() =>
        (await db.SavedCards.Where(c => c.UserId == CurrentUserId).OrderBy(c => c.Id).ToListAsync())
        .Select(c => c.ToDto()).ToList();

    [HttpPost]
    public async Task<IActionResult> Add(SaveCardRequest req)
    {
        var digits = new string((req.Number ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is < 13 or > 19)
            return BadRequest(new { message = "Card number must be 13–19 digits. Try the test card 4242 4242 4242 4242." });
        if (string.IsNullOrWhiteSpace(req.HolderName))
            return BadRequest(new { message = "Card holder name is required." });
        if (req.ExpMonth is < 1 or > 12)
            return BadRequest(new { message = "Expiry month must be 1–12." });
        if (req.ExpYear < DateTime.Now.Year || (req.ExpYear == DateTime.Now.Year && req.ExpMonth < DateTime.Now.Month))
            return BadRequest(new { message = "This card has expired." });
        if (req.Cvv is not { Length: 3 or 4 } || !req.Cvv.All(char.IsDigit))
            return BadRequest(new { message = "CVV must be 3 or 4 digits." });

        var brand = digits[0] switch
        {
            '4' => "Visa",
            '5' => "Mastercard",
            '3' => "Amex",
            _ => "Card"
        };

        db.SavedCards.Add(new SavedCard
        {
            UserId = CurrentUserId,
            Brand = brand,
            HolderName = req.HolderName.Trim(),
            Last4 = digits[^4..],
            ExpMonth = req.ExpMonth,
            ExpYear = req.ExpYear
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var card = await db.SavedCards.FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);
        if (card is null) return NotFound();
        db.SavedCards.Remove(card);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
