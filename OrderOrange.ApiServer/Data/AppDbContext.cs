using OrderOrange.ApiServer.Models;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<DriverProfile> DriverProfiles => Set<DriverProfile>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Cuisine> Cuisines => Set<Cuisine>();
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<FavoriteRestaurant> Favorites => Set<FavoriteRestaurant>();
    public DbSet<SavedCard> SavedCards => Set<SavedCard>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<RestaurantHours> RestaurantHours => Set<RestaurantHours>();
    public DbSet<RestaurantCuisine> RestaurantCuisines => Set<RestaurantCuisine>();
    public DbSet<RestaurantWord> RestaurantWords => Set<RestaurantWord>();
    public DbSet<RestaurantPhoto> RestaurantPhotos => Set<RestaurantPhoto>();
    public DbSet<MenuItemPhoto> MenuItemPhotos => Set<MenuItemPhoto>();
    public DbSet<StoreVisit> StoreVisits => Set<StoreVisit>();
    public DbSet<SearchLog> SearchLogs => Set<SearchLog>();
    public DbSet<ReceiptDesign> ReceiptDesigns => Set<ReceiptDesign>();
    public DbSet<StoreBill> StoreBills => Set<StoreBill>();
    public DbSet<InvoiceAudit> InvoiceAudits => Set<InvoiceAudit>();
    public DbSet<PushSubscriptionRow> PushSubscriptions => Set<PushSubscriptionRow>();
    public DbSet<StoreMaterial> StoreMaterials => Set<StoreMaterial>();
    public DbSet<ProductMaterial> ProductMaterials => Set<ProductMaterial>();
    public DbSet<MaterialPurchase> MaterialPurchases => Set<MaterialPurchase>();
    public DbSet<MaterialPurchaseLine> MaterialPurchaseLines => Set<MaterialPurchaseLine>();
    public DbSet<StoreAlert> StoreAlerts => Set<StoreAlert>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseTemplate> PurchaseTemplates => Set<PurchaseTemplate>();
    public DbSet<PurchasePayment> PurchasePayments => Set<PurchasePayment>();
    public DbSet<StoreStaff> StoreStaff => Set<StoreStaff>();
    public DbSet<SalaryPayment> SalaryPayments => Set<SalaryPayment>();
    public DbSet<StaffAttendance> StaffAttendances => Set<StaffAttendance>();
    public DbSet<StaffLeave> StaffLeaves => Set<StaffLeave>();
    public DbSet<StoreCalendarEvent> StoreCalendarEvents => Set<StoreCalendarEvent>();
    public DbSet<StoreCustomer> StoreCustomers => Set<StoreCustomer>();
    public DbSet<StoreTable> StoreTables => Set<StoreTable>();
    public DbSet<StoreRoom> StoreRooms => Set<StoreRoom>();
    public DbSet<StoreMember> StoreMembers => Set<StoreMember>();
    public DbSet<StoreRoleDef> StoreRoleDefs => Set<StoreRoleDef>();
    public DbSet<TeamPresence> TeamPresences => Set<TeamPresence>();
    public DbSet<StoreTab> StoreTabs => Set<StoreTab>();
    public DbSet<TableReservation> TableReservations => Set<TableReservation>();
    public DbSet<StoreContract> StoreContracts => Set<StoreContract>();
    public DbSet<StoreTabLine> StoreTabLines => Set<StoreTabLine>();
    public DbSet<PrintRoute> PrintRoutes => Set<PrintRoute>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // OMR uses 3 decimal places (baisa).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 3);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Word → first N stores whose name contains it. LIKE over millions of names
        // can't be indexed; this small inverted index answers name-word lookups instantly.
        b.Entity<RestaurantWord>().HasKey(w => new { w.Word, w.RestaurantId });
        b.Entity<PrintRoute>().HasKey(p => new { p.RestaurantId, p.MenuItemId });

        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        // The store's book is looked up by phone constantly — it is how a caller is
        // recognised — and the same phone must not appear twice in one store's list.
        b.Entity<StoreCustomer>().HasIndex(c => new { c.RestaurantId, c.Phone }).IsUnique();
        b.Entity<StoreCustomer>()
            .HasOne(c => c.User).WithMany()
            .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);

        // A table name means something on ONE floor — two 'T1's in one shop is chaos.
        b.Entity<StoreTable>().HasIndex(t => new { t.RestaurantId, t.Name }).IsUnique();
        b.Entity<StoreTable>()
            .HasOne(t => t.StoreCustomer).WithMany()
            .HasForeignKey(t => t.StoreCustomerId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<Coupon>().HasIndex(c => c.Code).IsUnique();
        b.Entity<DriverProfile>().HasIndex(d => d.UserId).IsUnique();

        b.Entity<DriverProfile>()
            .HasOne(d => d.User).WithOne(u => u.DriverProfile)
            .HasForeignKey<DriverProfile>(d => d.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Address>()
            .HasOne(a => a.User).WithMany(u => u.Addresses)
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Restaurant>()
            .HasOne(r => r.Owner).WithMany()
            .HasForeignKey(r => r.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Restaurant>()
            .HasOne(r => r.Cuisine).WithMany()
            .HasForeignKey(r => r.CuisineId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<MenuCategory>()
            .HasOne(c => c.Restaurant).WithMany(r => r.Categories)
            .HasForeignKey(c => c.RestaurantId).OnDelete(DeleteBehavior.Cascade);

        // Item cascades through its category; a second cascade path via Restaurant
        // is rejected by SQL Server, hence Restrict here.
        b.Entity<MenuItem>()
            .HasOne(i => i.Restaurant).WithMany(r => r.Items)
            .HasForeignKey(i => i.RestaurantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<MenuItem>()
            .HasOne(i => i.Category).WithMany(c => c.Items)
            .HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Cascade);

        // Orders are permanent records — never let deleting anything else take them down.
        b.Entity<Order>()
            .HasOne(o => o.Customer).WithMany()
            .HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>()
            .HasOne(o => o.Restaurant).WithMany()
            .HasForeignKey(o => o.RestaurantId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Order>()
            .HasOne(o => o.Driver).WithMany()
            .HasForeignKey(o => o.DriverUserId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<OrderItem>()
            .HasOne(i => i.Order).WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<OrderEvent>()
            .HasOne(e => e.Order).WithMany(o => o.Events)
            .HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Review>()
            .HasOne(r => r.Order).WithOne(o => o.Review)
            .HasForeignKey<Review>(r => r.OrderId).OnDelete(DeleteBehavior.Cascade);

        // A store's extra types beyond its main one. Keyed on the pair, so the same type
        // cannot be attached twice; indexed on CuisineId because the cuisine filter asks
        // "which stores carry this?" far more often than the reverse.
        b.Entity<RestaurantCuisine>().HasKey(rc => new { rc.RestaurantId, rc.CuisineId });
        b.Entity<RestaurantCuisine>().HasIndex(rc => rc.CuisineId);
        b.Entity<RestaurantCuisine>()
            .HasOne(rc => rc.Restaurant).WithMany(r => r.ExtraCuisines)
            .HasForeignKey(rc => rc.RestaurantId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<RestaurantHours>().HasIndex(h => new { h.RestaurantId, h.Day }).IsUnique();
        b.Entity<RestaurantHours>()
            .HasOne(h => h.Restaurant).WithMany()
            .HasForeignKey(h => h.RestaurantId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ActivityLog>().HasIndex(a => a.At);
        b.Entity<ActivityLog>().HasIndex(a => a.UserId);

        // Both admin views read newest-first over a date window.
        b.Entity<SearchLog>().HasIndex(s => s.At);
        b.Entity<SearchLog>().HasIndex(s => s.Term);

        b.Entity<SavedCard>()
            .HasOne(c => c.User).WithMany()
            .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<FavoriteRestaurant>().HasIndex(f => new { f.UserId, f.RestaurantId }).IsUnique();
        b.Entity<FavoriteRestaurant>()
            .HasOne(f => f.User).WithMany()
            .HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<FavoriteRestaurant>()
            .HasOne(f => f.Restaurant).WithMany()
            .HasForeignKey(f => f.RestaurantId).OnDelete(DeleteBehavior.Restrict);
    }
}
