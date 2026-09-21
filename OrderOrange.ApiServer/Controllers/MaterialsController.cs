using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The kitchen's store cupboard — rice, oil, saffron, lamb. Always scoped to the
/// signed-in owner's restaurant, like every other partner endpoint.
/// </summary>
[RequirePerm(Perm.Inventory)]
[Authorize(Roles = "RestaurantOwner")]
public class MaterialsController(AppDbContext db) : ApiControllerBase
{
    private static readonly string[] Categories =
        ["grain", "oil", "meat", "dairy", "vegetable", "spice", "drink", "packaging", "other"];

    private static readonly string[] Units = ["kg", "g", "l", "ml", "pcs", "box", "bag"];

    private IQueryable<StoreMaterial> Mine =>
        db.StoreMaterials.Where(m => m.RestaurantId == CurrentRestaurantId);

    private static StoreMaterialDto ToDto(StoreMaterial m) => new(
        m.Id, m.Name, m.Category, m.Unit, m.Quantity, m.MinQuantity, m.UnitCost,
        m.Supplier, m.Notes, m.RestockedAt, m.Code, m.Barcode);

    /// <summary>category: all or one of the known keys. search matches name or supplier.</summary>
    [HttpGet]
    public async Task<StoreMaterialsDto> List(string category = "all", string? search = null)
    {
        var query = Mine;
        if (category != "all" && Categories.Contains(category))
            query = query.Where(m => m.Category == category);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.Name.Contains(term) || (m.Supplier != null && m.Supplier.Contains(term)));
        }

        var rows = await query.OrderBy(m => m.Category).ThenBy(m => m.Name).ToListAsync();

        // Counted over everything the store holds, not just the filtered view — the
        // header should not change meaning when someone types in the search box.
        var all = await Mine.Select(m => new { m.Quantity, m.MinQuantity, m.UnitCost }).ToListAsync();

        return new StoreMaterialsDto(
            rows.Select(ToDto).ToList(),
            all.Sum(m => m.Quantity * m.UnitCost),
            all.Count(m => m.Quantity > 0 && m.MinQuantity > 0 && m.Quantity <= m.MinQuantity),
            all.Count(m => m.Quantity <= 0));
    }

    [HttpPost]
    public async Task<ActionResult<StoreMaterialDto>> Create(SaveStoreMaterialRequest req)
    {
        if (Validate(req) is { } error) return BadRequest(new { message = error });

        // No code given? Continue the simple sequence: 21, 22, … The owner can always
        // overwrite it with their own text later.
        var code = Blank(req.Code);
        if (code is null)
        {
            var numeric = await Mine.Where(m => m.Code != null).Select(m => m.Code!).ToListAsync();
            var next = numeric.Select(c => int.TryParse(c, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
            code = next.ToString();
        }

        var material = new StoreMaterial
        {
            RestaurantId = CurrentRestaurantId,
            Name = req.Name.Trim(),
            Category = Categories.Contains(req.Category) ? req.Category : "other",
            Unit = Units.Contains(req.Unit) ? req.Unit : "kg",
            Quantity = Math.Max(0, req.Quantity),
            MinQuantity = Math.Max(0, req.MinQuantity),
            UnitCost = Math.Max(0, req.UnitCost),
            Supplier = Blank(req.Supplier),
            Notes = Blank(req.Notes),
            Code = code,
            Barcode = Blank(req.Barcode),
            RestockedAt = req.Quantity > 0 ? DateTime.Now : null,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
        db.StoreMaterials.Add(material);
        await db.SaveChangesAsync();
        return Ok(ToDto(material));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StoreMaterialDto>> Update(int id, SaveStoreMaterialRequest req)
    {
        if (Validate(req) is { } error) return BadRequest(new { message = error });

        var material = await Mine.FirstOrDefaultAsync(m => m.Id == id);
        if (material is null) return NotFound();

        // A quantity typed in the edit form counts as a fresh stock count.
        if (material.Quantity != req.Quantity) material.RestockedAt = DateTime.Now;

        material.Name = req.Name.Trim();
        material.Category = Categories.Contains(req.Category) ? req.Category : "other";
        material.Unit = Units.Contains(req.Unit) ? req.Unit : "kg";
        material.Quantity = Math.Max(0, req.Quantity);
        material.MinQuantity = Math.Max(0, req.MinQuantity);
        material.UnitCost = Math.Max(0, req.UnitCost);
        material.Supplier = Blank(req.Supplier);
        material.Notes = Blank(req.Notes);
        material.Code = Blank(req.Code);
        material.Barcode = Blank(req.Barcode);
        material.UpdatedAt = DateTime.Now;

        await db.SaveChangesAsync();
        return Ok(ToDto(material));
    }

    /// <summary>A delivery arrived (+) or the kitchen used some (−). Never goes below zero.</summary>
    [HttpPost("{id:int}/adjust")]
    public async Task<ActionResult<StoreMaterialDto>> Adjust(int id, AdjustStoreMaterialRequest req)
    {
        var material = await Mine.FirstOrDefaultAsync(m => m.Id == id);
        if (material is null) return NotFound();
        if (req.Delta == 0) return Ok(ToDto(material));

        material.Quantity = Math.Max(0, material.Quantity + req.Delta);
        material.UpdatedAt = DateTime.Now;
        if (req.Delta > 0) material.RestockedAt = DateTime.Now;

        await db.SaveChangesAsync();
        return Ok(ToDto(material));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var material = await Mine.FirstOrDefaultAsync(m => m.Id == id);
        if (material is null) return NotFound();
        db.StoreMaterials.Remove(material);
        await db.SaveChangesAsync();
        return Ok();
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Validate(SaveStoreMaterialRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "Give the material a name.";
        if (req.Name.Trim().Length > 120) return "That name is too long.";
        if (req.Quantity < 0 || req.MinQuantity < 0 || req.UnitCost < 0) return "Numbers cannot be negative.";
        if (req.Quantity > 1_000_000 || req.UnitCost > 1_000_000) return "That number is too large.";
        return null;
    }

    // ─────────────────────────── stock alerts ───────────────────────────

    /// <summary>The bell: unseen first, then the recent history. Light — polled.</summary>
    [HttpGet("alerts")]
    public async Task<StockAlertsDto> Alerts()
    {
        var mine = db.StoreAlerts.Where(a => a.RestaurantId == CurrentRestaurantId);
        var unseen = await mine.CountAsync(a => a.SeenAt == null);
        var rows = await mine
            .OrderBy(a => a.SeenAt == null ? 0 : 1).ThenByDescending(a => a.Id)
            .Take(30)
            .Select(a => new StockAlertDto(a.Id, a.MaterialId, a.MaterialName, a.Unit,
                a.Quantity, a.Type, a.CreatedAt, a.SeenAt != null))
            .ToListAsync();
        return new StockAlertsDto(unseen, rows);
    }

    /// <summary>Opening the bell reads everything — the badge goes quiet until news.</summary>
    [HttpPost("alerts/seen")]
    public async Task<IActionResult> MarkAlertsSeen()
    {
        await db.StoreAlerts
            .Where(a => a.RestaurantId == CurrentRestaurantId && a.SeenAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SeenAt, DateTime.Now));
        return Ok();
    }

    // ─────────────────────────── recipes ───────────────────────────

    /// <summary>Every recipe in the store at once, with live material costs.</summary>
    [HttpGet("recipes")]
    public async Task<AllRecipesDto> Recipes()
    {
        var materials = await Mine.ToDictionaryAsync(m => m.Id);
        var lines = await db.ProductMaterials
            .Where(p => p.RestaurantId == CurrentRestaurantId)
            .ToListAsync();

        var byItem = lines
            .Where(l => materials.ContainsKey(l.MaterialId))
            .GroupBy(l => l.MenuItemId)
            .ToDictionary(
                g => g.Key,
                g => new ProductRecipeDto(g.Key, g.Select(l =>
                {
                    var m = materials[l.MaterialId];
                    return new RecipeLineDto(m.Id, m.Name, m.Unit, l.Amount, m.UnitCost);
                }).ToList()));

        return new AllRecipesDto(byItem);
    }

    /// <summary>Replace a product's recipe wholesale — the dialog always sends the full list.</summary>
    [HttpPut("recipes/{menuItemId:int}")]
    public async Task<ActionResult<ProductRecipeDto>> SaveRecipe(int menuItemId, SaveRecipeRequest req)
    {
        if (req.Lines.Count > 40) return BadRequest(new { message = "That is too many lines for one recipe." });
        if (req.Lines.Any(l => l.Amount <= 0 || l.Amount > 100_000))
            return BadRequest(new { message = "Amounts must be positive." });
        if (req.Lines.GroupBy(l => l.MaterialId).Any(g => g.Count() > 1))
            return BadRequest(new { message = "A material appears twice in the recipe." });

        var materials = await Mine.ToDictionaryAsync(m => m.Id);
        if (req.Lines.Any(l => !materials.ContainsKey(l.MaterialId)))
            return BadRequest(new { message = "An ingredient no longer exists." });

        var old = db.ProductMaterials.Where(p =>
            p.RestaurantId == CurrentRestaurantId && p.MenuItemId == menuItemId);
        db.ProductMaterials.RemoveRange(old);

        foreach (var line in req.Lines)
            db.ProductMaterials.Add(new ProductMaterial
            {
                RestaurantId = CurrentRestaurantId,
                MenuItemId = menuItemId,
                MaterialId = line.MaterialId,
                Amount = line.Amount,
            });

        await db.SaveChangesAsync();

        return Ok(new ProductRecipeDto(menuItemId, req.Lines.Select(l =>
        {
            var m = materials[l.MaterialId];
            return new RecipeLineDto(m.Id, m.Name, m.Unit, l.Amount, m.UnitCost);
        }).ToList()));
    }

    // ─────────────────────── purchase invoices ───────────────────────

    [HttpGet("purchases")]
    public async Task<MaterialPurchasePageDto> Purchases(int skip = 0, int take = 30)
    {
        take = Math.Clamp(take, 1, 100);
        var mine = db.MaterialPurchases.Where(p => p.RestaurantId == CurrentRestaurantId);

        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var monthTotal = await mine.Where(p => p.InvoiceDate >= monthStart).SumAsync(p => (decimal?)p.Total) ?? 0;
        var monthCount = await mine.CountAsync(p => p.InvoiceDate >= monthStart);
        var topSupplier = await mine.Where(p => p.InvoiceDate >= monthStart)
            .GroupBy(p => p.Supplier)
            .OrderByDescending(g => g.Sum(p => p.Total))
            .Select(g => g.Key)
            .FirstOrDefaultAsync();

        // What is owed and what is LATE come from the schedule, not the flag —
        // an invoice half-paid in installments owes exactly its unpaid rows.
        var today = DateTime.Today;
        var schedule = db.PurchasePayments.Where(x =>
            db.MaterialPurchases.Any(p => p.Id == x.PurchaseId && p.RestaurantId == CurrentRestaurantId));
        var unpaidTotal = await schedule.Where(x => x.PaidAt == null).SumAsync(x => (decimal?)x.Amount) ?? 0;
        var overdueTotal = await schedule.Where(x => x.PaidAt == null && x.DueDate < today).SumAsync(x => (decimal?)x.Amount) ?? 0;

        var rows = await mine
            .OrderByDescending(p => p.InvoiceDate).ThenByDescending(p => p.Id)
            .Skip(Math.Max(0, skip)).Take(take)
            .Include(p => p.Lines)
            .Include(p => p.Payments)
            .Select(p => new MaterialPurchaseDto(p.Id, p.Number, p.Supplier, p.InvoiceDate,
                p.Total, p.IsPaid, p.Notes,
                p.Lines.Select(l => new PurchaseLineDto(l.MaterialId, l.MaterialName, l.Unit, l.Quantity, l.UnitCost, l.IsProduct)).ToList(),
                p.Payments.OrderBy(x => x.DueDate)
                    .Select(x => new PurchasePaymentDto(x.Id, x.Amount, x.DueDate, x.PaidAt, x.Method, x.Note)).ToList()))
            .ToListAsync();

        return new MaterialPurchasePageDto(rows, monthTotal, unpaidTotal, monthCount, topSupplier, overdueTotal);
    }

    /// <summary>
    /// Save the supplier's invoice. Every line lands on the shelf: quantity is added
    /// and the material's unit cost becomes the newest paid price.
    /// </summary>
    [HttpPost("purchases")]
    public async Task<ActionResult<MaterialPurchaseDto>> CreatePurchase(SavePurchaseRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Supplier)) return BadRequest(new { message = "Who was the supplier?" });
        if (req.Lines.Count is 0 or > 60) return BadRequest(new { message = "An invoice needs 1–60 lines." });
        if (req.Lines.Any(l => l.Quantity <= 0 || l.UnitCost < 0)) return BadRequest(new { message = "Check the line numbers." });
        if (req.InvoiceDate > DateTime.Now.AddDays(1)) return BadRequest(new { message = "That date is in the future." });

        var materials = await Mine.ToDictionaryAsync(m => m.Id);
        if (req.Lines.Any(l => !l.IsProduct && !materials.ContainsKey(l.MaterialId)))
            return BadRequest(new { message = "A line points at a material that no longer exists." });

        // Finished products bought for resale ride the same invoice; their names are
        // frozen from the store's own menu.
        var productIds = req.Lines.Where(l => l.IsProduct).Select(l => l.MaterialId).Distinct().ToList();
        var products = productIds.Count == 0
            ? []
            : await db.MenuItems
                .Where(m => m.RestaurantId == CurrentRestaurantId && productIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id);
        if (productIds.Any(id => !products.ContainsKey(id)))
            return BadRequest(new { message = "A line points at a product that no longer exists." });

        var purchase = new MaterialPurchase
        {
            RestaurantId = CurrentRestaurantId,
            Number = Blank(req.Number) ?? $"P-{DateTime.Now:yyMMddHHmm}",
            Supplier = req.Supplier.Trim(),
            InvoiceDate = req.InvoiceDate,
            IsPaid = req.IsPaid,
            Notes = Blank(req.Notes),
            CreatedAt = DateTime.Now,
        };

        foreach (var line in req.Lines)
        {
            if (line.IsProduct)
            {
                purchase.Lines.Add(new MaterialPurchaseLine
                {
                    IsProduct = true,
                    MaterialId = line.MaterialId,
                    MaterialName = products[line.MaterialId].Name,
                    Unit = "pcs",
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                });
                continue;   // resale goods never touch the raw-material shelf
            }

            var material = materials[line.MaterialId];
            purchase.Lines.Add(new MaterialPurchaseLine
            {
                MaterialId = material.Id,
                MaterialName = material.Name,
                Unit = material.Unit,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
            });

            // The delivery goes straight onto the shelf.
            material.Quantity += line.Quantity;
            material.UnitCost = line.UnitCost > 0 ? line.UnitCost : material.UnitCost;
            material.RestockedAt = DateTime.Now;
            material.UpdatedAt = DateTime.Now;
        }
        purchase.Total = purchase.Lines.Sum(l => l.Quantity * l.UnitCost);

        // ── The payment plan ──────────────────────────────────────────────
        // "tbd" is honest for future money: the method is decided the day it is paid.
        var method = new[] { "cash", "card", "transfer", "cheque", "tbd" }.Contains(req.Method) ? req.Method : "cash";
        if (req.Installments is { Count: > 1 and <= 24 })
        {
            if (req.Installments.Any(i => i.Amount <= 0))
                return BadRequest(new { message = "Installment amounts must be positive." });
            var planned = req.Installments.Sum(i => i.Amount);
            if (Math.Abs(planned - purchase.Total) > 0.05m)
                return BadRequest(new { message = "The installments do not add up to the invoice total." });

            var remainder = purchase.Total;
            for (var i = 0; i < req.Installments.Count; i++)
            {
                // The LAST step absorbs rounding — the plan always sums exactly.
                var amount = i == req.Installments.Count - 1 ? remainder : req.Installments[i].Amount;
                remainder -= amount;
                purchase.Payments.Add(new PurchasePayment
                {
                    Amount = amount,
                    DueDate = req.Installments[i].DueDate.Date,
                    Method = method,
                    CreatedAt = DateTime.Now,
                });
            }
            purchase.IsPaid = false;
        }
        else if (req.IsPaid)
        {
            purchase.Payments.Add(new PurchasePayment
            {
                Amount = purchase.Total,
                DueDate = DateTime.Today,
                PaidAt = DateTime.Now,
                Method = method,
                CreatedAt = DateTime.Now,
            });
            purchase.IsPaid = true;
        }
        else
        {
            purchase.Payments.Add(new PurchasePayment
            {
                Amount = purchase.Total,
                DueDate = (req.DueDate ?? DateTime.Today.AddDays(30)).Date,
                Method = method,
                CreatedAt = DateTime.Now,
            });
            purchase.IsPaid = false;
        }

        db.MaterialPurchases.Add(purchase);
        await db.SaveChangesAsync();

        return Ok(new MaterialPurchaseDto(purchase.Id, purchase.Number, purchase.Supplier, purchase.InvoiceDate,
            purchase.Total, purchase.IsPaid, purchase.Notes,
            purchase.Lines.Select(l => new PurchaseLineDto(l.MaterialId, l.MaterialName, l.Unit, l.Quantity, l.UnitCost, l.IsProduct)).ToList(),
            purchase.Payments.OrderBy(x => x.DueDate)
                .Select(x => new PurchasePaymentDto(x.Id, x.Amount, x.DueDate, x.PaidAt, x.Method, x.Note)).ToList()));
    }

    /// <summary>Settle ONE step of the plan. The invoice flips to paid with its last step.</summary>
    [HttpPost("purchases/{id:int}/payments/{paymentId:int}/pay")]
    public async Task<IActionResult> PayInstallment(int id, int paymentId, [FromQuery] string? method = null)
    {
        var purchase = await db.MaterialPurchases.Include(p => p.Payments)
            .FirstOrDefaultAsync(p => p.Id == id && p.RestaurantId == CurrentRestaurantId);
        if (purchase is null) return NotFound();

        var payment = purchase.Payments.FirstOrDefault(x => x.Id == paymentId);
        if (payment is null) return NotFound();
        if (payment.PaidAt is not null) return BadRequest(new { message = "This installment is already paid." });

        payment.PaidAt = DateTime.Now;
        if (method is not null && new[] { "cash", "card", "transfer", "cheque" }.Contains(method))
            payment.Method = method;

        purchase.IsPaid = purchase.Payments.All(x => x.PaidAt is not null);
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>The blunt switch: everything paid ↔ everything owed. The schedule follows.</summary>
    [HttpPost("purchases/{id:int}/toggle-paid")]
    public async Task<IActionResult> TogglePurchasePaid(int id)
    {
        var purchase = await db.MaterialPurchases.Include(p => p.Payments)
            .FirstOrDefaultAsync(p => p.Id == id && p.RestaurantId == CurrentRestaurantId);
        if (purchase is null) return NotFound();

        purchase.IsPaid = !purchase.IsPaid;
        foreach (var payment in purchase.Payments)
            payment.PaidAt = purchase.IsPaid ? (payment.PaidAt ?? DateTime.Now) : null;

        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Deleting an invoice takes its stock back OFF the shelf — an undo, not a cover-up.</summary>
    [HttpDelete("purchases/{id:int}")]
    public async Task<IActionResult> DeletePurchase(int id)
    {
        var purchase = await db.MaterialPurchases.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.Id == id && p.RestaurantId == CurrentRestaurantId);
        if (purchase is null) return NotFound();

        var materials = await Mine.ToDictionaryAsync(m => m.Id);
        foreach (var line in purchase.Lines)
        {
            // Product lines never stocked the shelf — and their ids belong to the
            // MENU, so matching them against materials would rob the wrong row.
            if (line.IsProduct) continue;
            if (materials.TryGetValue(line.MaterialId, out var material))
            {
                material.Quantity = Math.Max(0, material.Quantity - line.Quantity);
                material.UpdatedAt = DateTime.Now;
            }
        }

        db.MaterialPurchases.Remove(purchase);
        await db.SaveChangesAsync();
        return Ok();
    }

    // ─────────────────────── purchase templates ───────────────────────

    /// <summary>Saved shopping lists, ready to fill the receiving ticket.</summary>
    [HttpGet("purchase-templates")]
    public async Task<List<PurchaseTemplateDto>> PurchaseTemplateList()
    {
        var rows = await db.PurchaseTemplates
            .Where(t => t.RestaurantId == CurrentRestaurantId)
            .OrderBy(t => t.Name)
            .ToListAsync();
        return rows.Select(t => new PurchaseTemplateDto(t.Id, t.Name, t.Supplier,
            System.Text.Json.JsonSerializer.Deserialize<List<PurchaseTemplateLineDto>>(t.LinesJson) ?? []))
            .ToList();
    }

    [HttpPost("purchase-templates")]
    public async Task<IActionResult> SavePurchaseTemplate(SavePurchaseTemplateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Give the template a name." });
        if (req.Lines.Count is 0 or > 60) return BadRequest(new { message = "A template needs 1–60 lines." });

        // Same name = overwrite: "the weekly meat order" should stay ONE list.
        var existing = await db.PurchaseTemplates.FirstOrDefaultAsync(t =>
            t.RestaurantId == CurrentRestaurantId && t.Name == req.Name.Trim());
        if (existing is null)
        {
            existing = new PurchaseTemplate { RestaurantId = CurrentRestaurantId, CreatedAt = DateTime.Now };
            db.PurchaseTemplates.Add(existing);
        }
        existing.Name = req.Name.Trim();
        existing.Supplier = req.Supplier.Trim();
        existing.LinesJson = System.Text.Json.JsonSerializer.Serialize(req.Lines);
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("purchase-templates/{id:int}")]
    public async Task<IActionResult> DeletePurchaseTemplate(int id)
    {
        var template = await db.PurchaseTemplates.FirstOrDefaultAsync(
            t => t.Id == id && t.RestaurantId == CurrentRestaurantId);
        if (template is null) return NotFound();
        db.PurchaseTemplates.Remove(template);
        await db.SaveChangesAsync();
        return Ok();
    }

    // ─────────────────────────── suppliers ───────────────────────────

    /// <summary>Every supplier with live purchase figures joined by name.</summary>
    [HttpGet("suppliers")]
    public async Task<List<SupplierDto>> Suppliers()
    {
        var suppliers = await db.Suppliers
            .Where(s => s.RestaurantId == CurrentRestaurantId)
            .OrderBy(s => s.Name)
            .ToListAsync();

        var stats = await db.MaterialPurchases
            .Where(p => p.RestaurantId == CurrentRestaurantId)
            .GroupBy(p => p.Supplier)
            .Select(g => new
            {
                Name = g.Key,
                Total = g.Sum(p => p.Total),
                Unpaid = g.Where(p => !p.IsPaid).Sum(p => (decimal?)p.Total) ?? 0,
                Count = g.Count(),
                Last = g.Max(p => (DateTime?)p.InvoiceDate),
            })
            .ToListAsync();

        return suppliers.Select(s =>
        {
            var stat = stats.FirstOrDefault(x => string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase));
            return new SupplierDto(s.Id, s.Name, s.Phone, s.ContactName, s.Notes,
                stat?.Total ?? 0, stat?.Unpaid ?? 0, stat?.Count ?? 0, stat?.Last);
        }).ToList();
    }

    [HttpPost("suppliers")]
    public async Task<ActionResult<SupplierDto>> CreateSupplier(SaveSupplierRequest req)
    {
        if (ValidateSupplier(req) is { } error) return BadRequest(new { message = error });
        if (await db.Suppliers.AnyAsync(s => s.RestaurantId == CurrentRestaurantId && s.Name == req.Name.Trim()))
            return BadRequest(new { message = "A supplier with this name already exists." });

        var supplier = new Supplier
        {
            RestaurantId = CurrentRestaurantId,
            Name = req.Name.Trim(),
            Phone = Blank(req.Phone),
            ContactName = Blank(req.ContactName),
            Notes = Blank(req.Notes),
            CreatedAt = DateTime.Now,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return Ok(new SupplierDto(supplier.Id, supplier.Name, supplier.Phone, supplier.ContactName, supplier.Notes, 0, 0, 0, null));
    }

    [HttpPut("suppliers/{id:int}")]
    public async Task<IActionResult> UpdateSupplier(int id, SaveSupplierRequest req)
    {
        if (ValidateSupplier(req) is { } error) return BadRequest(new { message = error });
        var supplier = await db.Suppliers.FirstOrDefaultAsync(
            s => s.Id == id && s.RestaurantId == CurrentRestaurantId);
        if (supplier is null) return NotFound();

        supplier.Name = req.Name.Trim();
        supplier.Phone = Blank(req.Phone);
        supplier.ContactName = Blank(req.ContactName);
        supplier.Notes = Blank(req.Notes);
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>The card goes; the invoices it appeared on keep their name snapshots.</summary>
    [HttpDelete("suppliers/{id:int}")]
    public async Task<IActionResult> DeleteSupplier(int id)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(
            s => s.Id == id && s.RestaurantId == CurrentRestaurantId);
        if (supplier is null) return NotFound();
        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync();
        return Ok();
    }

    private static string? ValidateSupplier(SaveSupplierRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "Give the supplier a name.";
        if (req.Name.Trim().Length > 200) return "That name is too long.";
        return null;
    }

    // ─────────────────────── consumption report ───────────────────────

    /// <summary>
    /// What the period's sales burned off the shelf: every delivered order's items,
    /// multiplied through their recipes. period: week|month|quarter.
    /// </summary>
    [HttpGet("consumption")]
    public async Task<ConsumptionReportDto> Consumption(string period = "week")
    {
        var days = period switch { "month" => 30, "quarter" => 90, _ => 7 };
        var from = DateTime.Today.AddDays(1 - days);

        var materials = await Mine.ToDictionaryAsync(m => m.Id);
        var recipeLines = await db.ProductMaterials
            .Where(p => p.RestaurantId == CurrentRestaurantId)
            .ToListAsync();
        var recipes = recipeLines.GroupBy(l => l.MenuItemId).ToDictionary(g => g.Key, g => g.ToList());

        // Sold quantities per menu item, from orders that actually completed.
        var sold = await db.Orders
            .Where(o => o.RestaurantId == CurrentRestaurantId
                        && o.Status == OrderStatus.Delivered && o.PlacedAt >= from)
            .SelectMany(o => o.Items)
            .GroupBy(i => new { i.MenuItemId, i.Name })
            .Select(g => new { g.Key.MenuItemId, g.Key.Name, Qty = g.Sum(i => i.Quantity) })
            .ToListAsync();

        var ordersCounted = await db.Orders.CountAsync(o =>
            o.RestaurantId == CurrentRestaurantId && o.Status == OrderStatus.Delivered && o.PlacedAt >= from);

        // material id → (used, cost, per-product detail)
        var usage = new Dictionary<int, (decimal Used, decimal Cost, List<ConsumptionProductDto> Products)>();
        var perProduct = new Dictionary<string, (int Sold, decimal Cost)>();

        foreach (var sale in sold)
        {
            if (!recipes.TryGetValue(sale.MenuItemId, out var lines)) continue;

            decimal productCost = 0;
            foreach (var line in lines)
            {
                if (!materials.TryGetValue(line.MaterialId, out var material)) continue;
                var used = line.Amount * sale.Qty;
                var cost = used * material.UnitCost;
                productCost += cost;

                if (!usage.TryGetValue(material.Id, out var entry))
                    entry = (0, 0, []);
                entry.Used += used;
                entry.Cost += cost;
                entry.Products.Add(new ConsumptionProductDto(sale.Name, sale.Qty, used, cost));
                usage[material.Id] = entry;
            }

            if (productCost > 0)
            {
                perProduct.TryGetValue(sale.Name, out var p);
                perProduct[sale.Name] = (p.Sold + sale.Qty, p.Cost + productCost);
            }
        }

        var rows = usage
            .Select(kv =>
            {
                var m = materials[kv.Key];
                return new ConsumptionRowDto(m.Id, m.Name, m.Unit, m.Category,
                    kv.Value.Used, kv.Value.Cost, m.Quantity,
                    kv.Value.Products.OrderByDescending(p => p.Amount).Take(8).ToList());
            })
            .OrderByDescending(r => r.Cost)
            .ToList();

        var top = perProduct
            .Select(kv => new ConsumptionProductDto(kv.Key, kv.Value.Sold, 0, kv.Value.Cost))
            .OrderByDescending(p => p.Cost)
            .Take(8)
            .ToList();

        return new ConsumptionReportDto(days, ordersCounted, rows.Sum(r => r.Cost), rows, top);
    }
}
