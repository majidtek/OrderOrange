using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

public class LookupsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("cuisines")]
    [AllowAnonymous]
    public async Task<List<CuisineDto>> Cuisines() =>
        await db.Cuisines.OrderBy(c => c.Name).Select(c => new CuisineDto(c.Id, c.Name, c.Emoji)).ToListAsync();

    [HttpPost("cuisines")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Create(SaveCuisineRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Name is required." });
        db.Cuisines.Add(new Cuisine { Name = req.Name.Trim(), Emoji = req.Emoji.Trim() });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("cuisines/{id:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Update(int id, SaveCuisineRequest req)
    {
        var cuisine = await db.Cuisines.FindAsync(id);
        if (cuisine is null) return NotFound();
        cuisine.Name = req.Name.Trim();
        cuisine.Emoji = req.Emoji.Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("cuisines/{id:int}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Delete(int id)
    {
        var cuisine = await db.Cuisines.FindAsync(id);
        if (cuisine is null) return NotFound();
        if (await db.Restaurants.AnyAsync(r => r.CuisineId == id))
            return BadRequest(new { message = "Cuisine is in use by a restaurant and cannot be deleted." });
        db.Cuisines.Remove(cuisine);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
