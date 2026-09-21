using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.Shared;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// The book entry nameless sales settle under. Receipts, chat and history all need a
/// customer to point at — the anonymous counter sale gets this one, created per store
/// on first use and reused forever after.
/// </summary>
public static class WalkInBook
{
    public const string Phone = "0000";

    public static async Task<StoreCustomer> GetOrCreateAsync(AppDbContext db, int restaurantId, string? guestName)
    {
        var customer = await db.StoreCustomers.FirstOrDefaultAsync(
            c => c.RestaurantId == restaurantId && c.Phone == Phone);
        if (customer is not null) return customer;

        var email = $"walkin.{restaurantId}@book.orderorange.local";
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            user = new User
            {
                FullName = "Walk-in",
                Email = email,
                Phone = Phone,
                // Nobody signs into this account — it exists so orders have a customer.
                PasswordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")),
                Role = UserRole.Customer,
                IsActive = true,
                CreatedAt = DateTime.Now,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        customer = new StoreCustomer
        {
            RestaurantId = restaurantId,
            UserId = user.Id,
            Name = string.IsNullOrWhiteSpace(guestName) ? "Walk-in" : guestName.Trim(),
            Phone = Phone,
            Address = "—",
            IsActive = true,
            CreatedAt = DateTime.Now,
        };
        db.StoreCustomers.Add(customer);
        await db.SaveChangesAsync();
        return customer;
    }
}
