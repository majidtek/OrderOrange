using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Menu management — restaurant owners only, always scoped to their own restaurant.
/// The catalog lives in MongoDB (<see cref="CatalogStore"/>).
///
/// A newly created product is live at once (<see cref="ProductStatus.Approved"/>); the
/// review gate was removed on 2026-09-26. An administrator can still reject a product
/// from the admin panel, which hides it again.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Menu, Perm.MenuView)]   // reading the shelf; every write below demands the full key
public class MenuController(CatalogStore catalog) : ApiControllerBase
{
    /// <summary>
    /// The owner's whole menu. <c>light=true</c> strips every photo — the till opens on
    /// that outline (names, prices, codes) and fetches one group's pictures at a time
    /// through <see cref="CategoryItems"/>, instead of a hundred photos before the first sale.
    /// </summary>
    [HttpGet]
    /// <c>basic=true</c> goes further for pages that only pick products by name (routing,
    /// purchases, recipes, labels, dialogs): no photos, no descriptions, no ingredients —
    /// ids, names (all languages), prices and availability.
    /// With <c>lang</c>, names come pre-translated and the per-language dictionaries stay home.
    public async Task<List<MenuCategoryDto>> MyMenu(bool light = false, bool basic = false, string? lang = null)
    {
        var menu = await catalog.OwnerMenuAsync(CurrentRestaurantId);
        if (basic)
        {
            var pick = !string.IsNullOrWhiteSpace(lang);
            return menu.Select(c => c with
            {
                Name = pick ? c.NameFor(lang!) : c.Name,
                Names = pick ? null : c.Names,
                Items = c.Items.Select(i => i with
                {
                    Name = pick ? i.NameFor(lang!) : i.Name,
                    Names = pick ? null : i.Names,
                    Photo = null, Description = "", Descriptions = null, Ingredients = "", RejectionReason = null,
                }).ToList(),
            }).ToList();
        }
        if (!light) return menu;
        return menu.Select(c => c with { Items = c.Items.Select(i => i with { Photo = null }).ToList() }).ToList();
    }

    /// <summary>One group's items with their photos — what the till pulls when that group is tapped.</summary>
    [HttpGet("categories/{id:int}/items")]
    public async Task<ActionResult<List<MenuItemDto>>> CategoryItems(int id)
    {
        var menu = await catalog.OwnerMenuAsync(CurrentRestaurantId);
        var category = menu.FirstOrDefault(c => c.Id == id);
        return category is null ? NotFound() : Ok(category.Items);
    }

    /// <summary>How many of this partner's products are still waiting for review.</summary>
    [HttpGet("pending-count")]
    public async Task<int> PendingCount() => await catalog.PendingCountAsync(CurrentRestaurantId);

    // ---------- Categories ----------

    [HttpPost("categories")]
    [RequirePerm(Perm.Menu)]
    public async Task<IActionResult> CreateCategory(SaveCategoryRequest req)
    {
        var (canonical, names, nameError) = ResolveCategoryNames(req);
        if (nameError is not null) return BadRequest(new { message = nameError });
        await catalog.AddCategoryAsync(new MenuCategoryDoc
        {
            RestaurantId = CurrentRestaurantId,
            Name = canonical,
            SortOrder = req.SortOrder,
            Names = names,
        });
        return NoContent();
    }

    [HttpPut("categories/{id:int}")]
    [RequirePerm(Perm.Menu)]
    public async Task<IActionResult> UpdateCategory(int id, SaveCategoryRequest req)
    {
        var categories = await catalog.CategoriesAsync(CurrentRestaurantId);
        var category = categories.FirstOrDefault(c => c.Id == id);
        if (category is null) return NotFound();

        var (canonical, names, nameError) = ResolveCategoryNames(req);
        if (nameError is not null) return BadRequest(new { message = nameError });
        category.Name = canonical;
        if (names is not null) category.Names = names;
        category.SortOrder = req.SortOrder;
        await catalog.UpdateCategoryAsync(category);
        return NoContent();
    }

    /// <summary>
    /// The canonical name comes from whatever was given: the multilingual set
    /// (Arabic required, English preferred for the canonical) or the plain Name.
    /// </summary>
    private static (string Canonical, Dictionary<string, string>? Names, string? Error) ResolveCategoryNames(SaveCategoryRequest req)
    {
        var names = req.Names is null ? null : RestaurantsController.CleanNames(req.Names);
        if (names is { Count: > 0 })
        {
            if (!names.ContainsKey("ar")) return ("", null, "Arabic name is required.");
            return (names.GetValueOrDefault("en") ?? names["ar"], names, null);
        }
        return string.IsNullOrWhiteSpace(req.Name)
            ? ("", null, "Category name is required.")
            : (req.Name.Trim(), null, null);
    }

    [HttpDelete("categories/{id:int}")]
    [RequirePerm(Perm.Menu)]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var categories = await catalog.CategoriesAsync(CurrentRestaurantId);
        if (categories.All(c => c.Id != id)) return NotFound();
        await catalog.DeleteCategoryAsync(id, CurrentRestaurantId);   // items and photos go too
        return NoContent();
    }

    // ---------- Items ----------

    [HttpPost("items")]
    [RequirePerm(Perm.Menu)]
    public async Task<ActionResult<MenuItemDto>> CreateItem(SaveMenuItemRequest req)
    {
        var error = await ValidateItem(req);
        if (error is not null) return BadRequest(new { message = error });

        var item = await catalog.AddItemAsync(new MenuItemDoc
        {
            RestaurantId = CurrentRestaurantId,
            CategoryId = req.CategoryId,
            Name = req.Name.Trim(),
            Description = req.Description.Trim(),
            Price = req.Price,
            ImageEmoji = string.IsNullOrWhiteSpace(req.ImageEmoji) ? "🍽️" : req.ImageEmoji.Trim(),
            IsPopular = req.IsPopular,
            IsAvailable = req.IsAvailable,
            InStoreOnly = req.InStoreOnly,
            PhotoData = string.IsNullOrEmpty(req.Photo) ? null : req.Photo,
            DiscountPercent = Math.Clamp(req.DiscountPercent, 0, 90),
            SearchKeywords = SearchAliases.KeywordsFor(req.Name),
            AvailableFromMinutes = req.AvailableFromMinutes,
            AvailableToMinutes = req.AvailableToMinutes,
            AvailableDays = req.AvailableDays ?? "",
            LeadTimeDays = Math.Clamp(req.LeadTimeDays, 0, 30),
            Ingredients = CleanIngredients(req.Ingredients),
            Unit = (req.Unit ?? "").Trim(),
            SortOrder = req.SortOrder,
            Names = req.Names is null ? null : RestaurantsController.CleanNames(req.Names),
            Descriptions = req.Descriptions is null ? null : RestaurantsController.CleanTexts(req.Descriptions),
        });

        if (req.Photos is not null)
            await catalog.ReplaceItemPhotosAsync(item.Id, req.Photos, req.MainPhoto);

        // Returned so the partner app can say "this one is waiting for review".
        return item.ToDto();
    }

    [HttpPut("items/{id:int}")]
    [RequirePerm(Perm.Menu)]
    public async Task<IActionResult> UpdateItem(int id, SaveMenuItemRequest req)
    {
        var item = await catalog.OwnedItemAsync(id, CurrentRestaurantId);
        if (item is null) return NotFound();
        var error = await ValidateItem(req);
        if (error is not null) return BadRequest(new { message = error });

        item.CategoryId = req.CategoryId;
        item.Name = req.Name.Trim();
        item.Description = req.Description.Trim();
        item.Price = req.Price;
        item.ImageEmoji = string.IsNullOrWhiteSpace(req.ImageEmoji) ? "🍽️" : req.ImageEmoji.Trim();
        item.IsPopular = req.IsPopular;
        item.IsAvailable = req.IsAvailable;
        item.InStoreOnly = req.InStoreOnly;
        if (req.Photo is not null) item.PhotoData = req.Photo.Length == 0 ? null : req.Photo;
        item.DiscountPercent = Math.Clamp(req.DiscountPercent, 0, 90);
        item.SearchKeywords = SearchAliases.KeywordsFor(item.Name);
        item.AvailableFromMinutes = req.AvailableFromMinutes;
        item.AvailableToMinutes = req.AvailableToMinutes;
        item.AvailableDays = req.AvailableDays ?? "";
        item.LeadTimeDays = Math.Clamp(req.LeadTimeDays, 0, 30);
        item.Ingredients = CleanIngredients(req.Ingredients);
        item.Unit = (req.Unit ?? "").Trim();
        item.SortOrder = req.SortOrder;
        if (req.Names is not null) item.Names = RestaurantsController.CleanNames(req.Names);
        if (req.Descriptions is not null) item.Descriptions = RestaurantsController.CleanTexts(req.Descriptions);

        await catalog.UpdateItemAsync(item);   // status intentionally unchanged
        if (req.Photos is not null)
            await catalog.ReplaceItemPhotosAsync(item.Id, req.Photos, req.MainPhoto);
        return NoContent();
    }

    /// <summary>At most 20 materials, 40 chars each, "; "-joined — nothing exotic gets stored.</summary>
    private static string CleanIngredients(string? raw) =>
        string.Join("; ", (raw ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length is > 0 and <= 40).Take(20));

    [HttpGet("items/{id:int}/photos")]
    public async Task<ActionResult<List<DishPhotoDto>>> ItemPhotos(int id)
    {
        if (await catalog.OwnedItemAsync(id, CurrentRestaurantId) is null) return NotFound();
        return (await catalog.ItemPhotosAsync(id))
            .Select(p => new DishPhotoDto(p.Id, p.Data, p.IsMain)).ToList();
    }

    [HttpPost("items/{id:int}/toggle-available")]
    [RequirePerm(Perm.Menu)]
    public async Task<IActionResult> ToggleAvailable(int id)
    {
        var item = await catalog.OwnedItemAsync(id, CurrentRestaurantId);
        if (item is null) return NotFound();
        await catalog.SetAvailabilityAsync(id, !item.IsAvailable);
        return Ok(new { isAvailable = !item.IsAvailable });
    }

    [HttpDelete("items/{id:int}")]
    [RequirePerm(Perm.Menu)]
    public async Task<IActionResult> DeleteItem(int id)
    {
        if (await catalog.OwnedItemAsync(id, CurrentRestaurantId) is null) return NotFound();
        await catalog.DeleteItemAsync(id);   // past orders keep their snapshot rows
        return NoContent();
    }

    private async Task<string?> ValidateItem(SaveMenuItemRequest req)
    {
        if (req.Names is not null && !RestaurantsController.CleanNames(req.Names).ContainsKey("ar"))
            return "The Arabic name is required.";
        if (string.IsNullOrWhiteSpace(req.Name)) return "Item name is required.";
        if (req.Price <= 0) return "Price must be greater than zero.";
        if (!string.IsNullOrEmpty(req.Photo))
        {
            if (!req.Photo.StartsWith("data:image/")) return "Only images are allowed.";
            if (req.Photo.Length > 700_000) return "The photo is too large (max 0.5 MB).";
        }
        if (req.Photos is not null)
        {
            if (req.Photos.Count > 4) return "Maximum 4 photos.";
            foreach (var photo in req.Photos)
            {
                if (!photo.StartsWith("data:image/")) return "Only images are allowed.";
                if (photo.Length > 700_000) return "A photo is too large (max 0.5 MB).";
            }
        }
        return await catalog.OwnsCategoryAsync(req.CategoryId, CurrentRestaurantId)
            ? null : "Category not found.";
    }
}
