using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

public class AddressesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<List<AddressDto>> Mine() =>
        (await db.Addresses.Where(a => a.UserId == CurrentUserId).OrderBy(a => a.Id).ToListAsync())
        .Select(a => a.ToDto()).ToList();

    [HttpPost]
    public async Task<IActionResult> Create(SaveAddressRequest req)
    {
        var error = Validate(req);
        if (error is not null) return BadRequest(new { message = error });
        var label = LabelOrDefault(req);
        // One name, one address — two "Home"s make the checkout radio a guessing game.
        if (await db.Addresses.AnyAsync(a => a.UserId == CurrentUserId && a.Label == label))
            return BadRequest(new { message = "An address with this name already exists — choose another name." });
        db.Addresses.Add(new Address
        {
            UserId = CurrentUserId,
            Label = label,
            Area = (req.Area ?? "").Trim(),
            Street = (req.Street ?? "").Trim(),
            Building = (req.Building ?? "").Trim(),
            Notes = req.Notes?.Trim(),
            Lat = req.Lat,
            Lng = req.Lng
        });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, SaveAddressRequest req)
    {
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId);
        if (address is null) return NotFound();
        var error = Validate(req);
        if (error is not null) return BadRequest(new { message = error });
        var label = LabelOrDefault(req);
        if (await db.Addresses.AnyAsync(a => a.UserId == CurrentUserId && a.Label == label && a.Id != id))
            return BadRequest(new { message = "An address with this name already exists — choose another name." });
        address.Label = label;
        address.Area = (req.Area ?? "").Trim();
        address.Street = (req.Street ?? "").Trim();
        address.Building = (req.Building ?? "").Trim();
        address.Notes = req.Notes?.Trim();
        address.Lat = req.Lat;
        address.Lng = req.Lng;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>A nameless address borrows its area's name, or wears a plain pin.</summary>
    private static string LabelOrDefault(SaveAddressRequest req)
    {
        var label = (req.Label ?? "").Trim();
        if (label.Length > 0) return label;
        var area = (req.Area ?? "").Trim();
        return area.Length > 0 ? area : "📍";
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId);
        if (address is null) return NotFound();
        db.Addresses.Remove(address); // orders keep their own snapshot of the address text
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// The PIN is the address: the rider navigates by coordinates. Every text
    /// field is optional decoration on top of it.
    /// </summary>
    private static string? Validate(SaveAddressRequest req)
    {
        if (req.Lat is null || req.Lng is null) return "Pick the location on the map first.";
        return null;
    }
}
