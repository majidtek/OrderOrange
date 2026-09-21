using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The store's own customer book: the people who phone in or walk up, who have no app
/// and never signed up. Everything here is scoped to the caller's own store — one
/// partner must never be able to read or edit another's customer list.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Customers, Perm.CustomersView)]
public class StoreCustomersController(AppDbContext db) : ApiControllerBase
{
    /// <summary>
    /// The shadow account a counter order is filed under.
    ///
    /// Keyed on the PHONE NUMBER, never on an email the partner typed. Letting a partner
    /// name any address would let them attach their book to a stranger's real account and
    /// place orders that show up in that stranger's app. A phone-derived address at an
    /// unroutable domain can never receive a sign-in code, so it cannot be taken over.
    /// </summary>
    private static string ShadowEmail(string phone) =>
        $"{new string(phone.Where(char.IsDigit).ToArray())}@phone.orderorange.local";

    private static string NormalisePhone(string? phone) => (phone ?? "").Trim();

    private static StoreCustomerDto ToDto(StoreCustomer c) => new(
        c.Id, c.Name, c.Phone, c.Address, c.Notes, c.OrderCount, c.LastOrderAt, c.CreatedAt, c.Lat, c.Lng);

    [HttpGet]
    public async Task<ActionResult<List<StoreCustomerDto>>> List(string? search = null, int take = 100)
    {
        if (CurrentRestaurantId == 0) return Forbid();

        var query = db.StoreCustomers.Where(c => c.RestaurantId == CurrentRestaurantId && c.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.Name.Contains(term) || c.Phone.Contains(term) || c.Address.Contains(term));
        }

        // Whoever ordered most recently is nearly always who the counter is looking for.
        var rows = await query
            .OrderByDescending(c => c.LastOrderAt ?? c.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        return Ok(rows.Select(ToDto).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StoreCustomerDto>> Get(int id)
    {
        var customer = await Owned(id);
        return customer is null ? NotFound() : Ok(ToDto(customer));
    }

    [HttpPost]
    [RequirePerm(Perm.Customers)]
    public async Task<ActionResult<StoreCustomerDto>> Create(SaveStoreCustomerRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();

        var problem = Validate(req);
        if (problem is not null) return BadRequest(new { message = problem });

        var phone = NormalisePhone(req.Phone);
        if (await db.StoreCustomers.AnyAsync(c => c.RestaurantId == CurrentRestaurantId && c.Phone == phone))
            return BadRequest(new { message = "You already have a customer with this phone number." });

        var email = ShadowEmail(phone);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            user = new User
            {
                FullName = req.Name.Trim(),
                Email = email,
                Phone = phone,
                // Nobody signs into this account — it exists so orders, chat and receipts
                // have a customer to point at.
                PasswordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                Role = UserRole.Customer,
                IsActive = true,
                CreatedAt = DateTime.Now,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        var customer = new StoreCustomer
        {
            RestaurantId = CurrentRestaurantId,
            UserId = user.Id,
            Name = req.Name.Trim(),
            Phone = phone,
            Address = req.Address.Trim(),
            Lat = req.Lat,
            Lng = req.Lng,
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            IsActive = true,
            CreatedAt = DateTime.Now,
        };
        db.StoreCustomers.Add(customer);
        await db.SaveChangesAsync();

        return Ok(ToDto(customer));
    }

    [HttpPut("{id:int}")]
    [RequirePerm(Perm.Customers)]
    public async Task<ActionResult<StoreCustomerDto>> Update(int id, SaveStoreCustomerRequest req)
    {
        var customer = await Owned(id);
        if (customer is null) return NotFound();

        var problem = Validate(req);
        if (problem is not null) return BadRequest(new { message = problem });

        var phone = NormalisePhone(req.Phone);
        if (phone != customer.Phone &&
            await db.StoreCustomers.AnyAsync(c => c.RestaurantId == CurrentRestaurantId && c.Phone == phone && c.Id != id))
            return BadRequest(new { message = "Another customer already has this phone number." });

        customer.Name = req.Name.Trim();
        customer.Phone = phone;
        customer.Address = req.Address.Trim();
        customer.Lat = req.Lat;
        customer.Lng = req.Lng;
        customer.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
        await db.SaveChangesAsync();

        return Ok(ToDto(customer));
    }

    /// <summary>
    /// Hidden rather than erased: their past orders still name them, and deleting the row
    /// would leave those orders pointing at nothing.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePerm(Perm.Customers)]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await Owned(id);
        if (customer is null) return NotFound();

        customer.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<StoreCustomer?> Owned(int id) =>
        CurrentRestaurantId == 0
            ? null
            : await db.StoreCustomers.FirstOrDefaultAsync(c => c.Id == id && c.RestaurantId == CurrentRestaurantId);

    private static string? Validate(SaveStoreCustomerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return "A name is required.";
        var digits = (req.Phone ?? "").Count(char.IsDigit);
        if (digits < 7) return "Enter a valid phone number.";
        if (string.IsNullOrWhiteSpace(req.Address)) return "A delivery address is required.";
        return null;
    }
}
