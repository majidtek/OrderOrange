using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderOrange.ApiServer.Data;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The pictures behind the URLs MediaLinks hands out. Every response carries a week of
/// public cache: the ?v= in the URL changes when the picture does, so serving stale is
/// impossible and re-downloading is pointless — which is the whole point.
/// </summary>
public class MediaController(AppDbContext db, CatalogStore catalog) : ApiControllerBase
{
    [HttpGet("dish/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Dish(int id) =>
        FromDataUri((await catalog.ApprovedItemAsync(id))?.PhotoData);

    [HttpGet("logo/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Logo(int id) =>
        FromDataUri(await db.Restaurants.Where(r => r.Id == id && r.IsApproved)
            .Select(r => r.LogoData).FirstOrDefaultAsync());

    [HttpGet("banner/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> Banner(int id) =>
        FromDataUri(await db.RestaurantPhotos.Where(p => p.Id == id)
            .Select(p => p.Data).FirstOrDefaultAsync());

    [HttpGet("dishphoto/{id:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> DishPhoto(int id) =>
        FromDataUri(await db.MenuItemPhotos.Where(p => p.Id == id)
            .Select(p => p.Data).FirstOrDefaultAsync());

    /// <summary>"data:image/jpeg;base64,..." → the actual image response.</summary>
    private IActionResult FromDataUri(string? dataUri)
    {
        if (dataUri is not { Length: > 0 } || !dataUri.StartsWith("data:")) return NotFound();
        var comma = dataUri.IndexOf(',');
        if (comma < 0) return NotFound();
        var meta = dataUri[5..comma];                       // "image/jpeg;base64"
        var mime = meta.Split(';')[0];
        if (!mime.StartsWith("image/")) return NotFound();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUri[(comma + 1)..]); }
        catch (FormatException) { return NotFound(); }
        Response.Headers.CacheControl = "public, max-age=604800";
        return File(bytes, mime);
    }
}
