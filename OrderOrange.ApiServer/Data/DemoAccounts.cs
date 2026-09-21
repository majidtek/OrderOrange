using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Data;

/// <summary>
/// Creates (or resets) one ready-to-use demo login per app: customer, partner, rider
/// and administrator. Idempotent — running it again just resets the passwords, so it
/// doubles as "I've forgotten the demo password" recovery.
///
/// The partner account is attached to a store that already has orders, staff, bills and
/// a receipt design, so the partner portal has something to show rather than empty pages.
/// </summary>
public static class DemoAccounts
{
    public const string Password = "Pas_123";

    public sealed record Created(string Email, string Password, UserRole Role, string App, string? Note);

    public static async Task<List<Created>> SeedAsync(AppDbContext db, ILogger logger)
    {
        var made = new List<Created>();

        var customer = await UpsertAsync(db, "demo@orderorange.com", "Demo Customer",
            "+968 9000 0001", UserRole.Customer);
        made.Add(new Created(customer.Email, Password, customer.Role, "Customer app", null));

        // Partner: give them a store that already has data behind it.
        var partner = await UpsertAsync(db, "partner@orderorange.com", "Demo Partner",
            "+968 9000 0002", UserRole.RestaurantOwner);
        var store = await PickDemoStoreAsync(db);
        string? storeNote = null;
        if (store is not null)
        {
            store.OwnerUserId = partner.Id;
            storeNote = $"owns “{store.Name}” (store #{store.Id})";
        }
        made.Add(new Created(partner.Email, Password, partner.Role, "Partner portal", storeNote));

        // Rider: needs an approved profile or the app refuses to let them go online.
        var driver = await UpsertAsync(db, "driver@orderorange.com", "Demo Rider",
            "+968 9000 0003", UserRole.Driver);
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(p => p.UserId == driver.Id);
        if (profile is null)
        {
            profile = new DriverProfile { UserId = driver.Id };
            db.DriverProfiles.Add(profile);
        }
        profile.VehicleType = VehicleType.Motorbike;
        profile.PlateNumber = "1234 OR";
        profile.Verification = DriverVerificationStatus.Approved;
        profile.VerificationReason = null;
        profile.DocsSubmittedAt ??= DateTime.Now;
        made.Add(new Created(driver.Email, Password, driver.Role, "Delivery app", "verified, motorbike 1234 OR"));

        var admin = await UpsertAsync(db, "admin@orderorange.com", "Demo Admin",
            "+968 9000 0004", UserRole.Administrator);
        made.Add(new Created(admin.Email, Password, admin.Role, "Admin panel", null));

        await db.SaveChangesAsync();
        logger.LogInformation("Demo accounts ready: {Emails}", string.Join(", ", made.Select(m => m.Email)));
        return made;
    }

    private static async Task<User> UpsertAsync(
        AppDbContext db, string email, string name, string phone, UserRole role)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            user = new User { Email = email, CreatedAt = DateTime.Now };
            db.Users.Add(user);
        }
        user.FullName = name;
        user.Phone = phone;
        user.Role = role;
        user.IsActive = true;
        user.PasswordHash = PasswordHasher.Hash(Password);
        await db.SaveChangesAsync();      // need the identity before linking a store/profile
        return user;
    }

    /// <summary>
    /// The most interesting store to demo: the one with the most orders behind it, so the
    /// partner portal opens on real reports rather than zeroes.
    /// </summary>
    private static async Task<Restaurant?> PickDemoStoreAsync(AppDbContext db)
    {
        var busiest = await db.Orders
            .GroupBy(o => o.RestaurantId)
            .Select(g => new { RestaurantId = g.Key, Orders = g.Count() })
            .OrderByDescending(x => x.Orders)
            .FirstOrDefaultAsync();

        return busiest is null
            ? await db.Restaurants.FirstOrDefaultAsync(r => r.IsApproved)
            : await db.Restaurants.FirstOrDefaultAsync(r => r.Id == busiest.RestaurantId);
    }
}
