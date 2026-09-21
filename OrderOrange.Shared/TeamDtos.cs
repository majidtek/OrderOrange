namespace OrderOrange.Shared;

/// <summary>
/// What a team member may do inside the partner portal. The owner is not a role —
/// ownership comes from the Restaurants table and outranks all of these.
/// </summary>
public enum StoreRole
{
    /// <summary>Everything except the owner's businesses and their deletion.</summary>
    Manager = 0,

    /// <summary>The till: POS, orders, bills.</summary>
    Cashier = 1,

    /// <summary>The pass: live orders only.</summary>
    Kitchen = 2,

    /// <summary>The floor: tables, reservations, guest chat.</summary>
    Waiter = 3,
}

/// <summary>
/// Every distinct thing a person can DO in a store, named once. Roles are built out
/// of these, the API checks them, and the portal hides what a key does not open —
/// all three read this same list, so a hidden button is genuinely a closed door.
/// </summary>
public static class Perm
{
    // ---- The floor and the till ----
    public const string Pos = "pos";                    // ring up sales
    public const string Orders = "orders";              // see and advance live orders
    public const string OrdersCancel = "orders.cancel"; // reject or cancel an order
    public const string Tables = "tables";              // floor plan, seating, reservations
    public const string GuestChat = "chat.guest";       // answer the tables

    // ---- The goods ----
    public const string Menu = "menu";                  // products and prices (full)
    public const string MenuView = "menu.view";         // see the shelf, change nothing
    public const string Inventory = "inventory";        // materials, recipes, purchases (full)
    public const string InventoryView = "inventory.view";
    public const string Suppliers = "suppliers";
    public const string SuppliersView = "suppliers.view";

    // ---- The invoices ----
    // ADD is the till itself (Pos covers ringing up); these two govern what happens to an
    // invoice AFTER it is written: taking one back, and replacing one with a corrected
    // copy. Separate doors, because the shop that lets every cashier sell rarely wants
    // every cashier able to void yesterday's paperwork.
    public const string InvoiceCancel = "invoice.cancel";
    public const string InvoiceEdit = "invoice.edit";

    // ---- The money ----
    public const string Reports = "reports";            // sales, products, customers
    public const string Bills = "bills";                // the store's own bills
    public const string Payments = "payments";          // pay suppliers
    public const string Payroll = "payroll";            // staff salaries
    public const string Discounts = "discounts";        // change prices, give discounts

    // ---- The paperwork ----
    public const string Contracts = "contracts";        // standing supply agreements
    public const string Reservations = "reservations";  // the booking book
    public const string Deliveries = "deliveries";      // the store's own delivery run

    // ---- The house ----
    public const string Customers = "customers";        // the customer book (full)
    public const string CustomersView = "customers.view";
    public const string TablesView = "tables.view";     // see the floor, move nothing
    public const string Staff = "staff";                // the staff register
    public const string Settings = "settings";          // store settings, receipt design
    public const string Team = "team";                  // hand out portal logins
    public const string TeamChat = "chat.team";         // message teammates
    public const string Surveys = "surveys";            // ask the guests, read their answers
    public const string Calendar = "calendar";          // write on the store's shared calendar (everyone reads it)
    public const string Support = "support";            // open and follow support tickets with OrderOrange

    /// <summary>Everything, in the order a permissions page should read.</summary>
    public static readonly string[] All =
    [
        Pos, Orders, OrdersCancel, InvoiceCancel, InvoiceEdit,
        Tables, TablesView, GuestChat, Reservations, Deliveries,
        Menu, MenuView, Inventory, InventoryView, Suppliers, SuppliersView,
        Contracts, Reports, Bills, Payments, Payroll, Discounts,
        Customers, CustomersView, Staff, Settings, Team, TeamChat, Surveys, Calendar, Support,
    ];

    /// <summary>
    /// What each role may do. The owner is deliberately absent: ownership is not a
    /// role, it is the deed to the shop, and <see cref="Allows"/> grants it all.
    /// </summary>
    private static readonly Dictionary<StoreRole, HashSet<string>> ByRole = new()
    {
        // A manager runs the business day to day — everything but handing out keys
        // and rewriting the shop itself, which stay with the owner.
        [StoreRole.Manager] = new(All.Except([Team, Settings])),

        // The till: sells, takes money, sees what was sold today.
        [StoreRole.Cashier] = new([Pos, Orders, Tables, GuestChat, Reservations, Menu, Customers, Bills, TeamChat]),

        // The pass: the kitchen needs the queue and what it is cooking from.
        [StoreRole.Kitchen] = new([Orders, Menu, Inventory, TeamChat]),

        // The floor: seating, guests and their orders.
        [StoreRole.Waiter] = new([Pos, Orders, Tables, GuestChat, Reservations, Menu, Customers, TeamChat]),
    };

    /// <summary>Does this store role open that door? "owner" (or empty) opens all.</summary>
    public static bool Allows(string? storeRole, string permission)
    {
        if (string.IsNullOrEmpty(storeRole) || storeRole.Equals("owner", StringComparison.OrdinalIgnoreCase))
            return true;   // tokens minted before team roles existed belong to owners
        return Enum.TryParse<StoreRole>(storeRole, true, out var role)
            && ByRole.TryGetValue(role, out var set)
            && Covers(set, permission);
    }

    /// <summary>
    /// The same question, but for a session carrying an EXPLICIT permission list —
    /// a role the owner wrote themselves. The list wins outright: a custom role is
    /// exactly what it says it is, no preset showing through underneath.
    /// </summary>
    public static bool Allows(string? storeRole, string? explicitPerms, string permission)
    {
        if (string.IsNullOrEmpty(storeRole) || storeRole.Equals("owner", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(explicitPerms))
            return Covers(Split(explicitPerms), permission);
        return Allows(storeRole, permission);
    }

    /// <summary>The FULL key opens the view door too: "menu" covers "menu.view".</summary>
    private static bool Covers(HashSet<string> set, string permission) =>
        set.Contains(permission)
        || (permission.EndsWith(".view") && set.Contains(permission[..^5]));

    /// <summary>Parses a stored "pos,orders,menu" list.</summary>
    public static HashSet<string> Split(string? csv) =>
        new((csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Everything this role may do — for the permissions matrix.</summary>
    public static IReadOnlyCollection<string> Of(StoreRole role) =>
        ByRole.TryGetValue(role, out var set) ? set : [];
}

/// <summary>A role the owner wrote themselves, and how many people hold it.</summary>
public record StoreRoleDefDto(
    int Id,
    string Name,
    string Icon,
    List<string> Perms,
    int Members);

/// <summary>Create or rename a custom role and set exactly what it opens.</summary>
public record SaveStoreRoleDefRequest(string Name, string Icon, List<string> Perms);

/// <summary>What one PRESET role opens in this store, after the owner's rewrite.</summary>
public record PresetRolePermsDto(StoreRole Role, List<string> Perms);

public record SavePresetPermsRequest(List<string> Perms);

/// <summary>One person with a key to this store's portal.</summary>
public record StoreMemberDto(
    int Id,
    int UserId,
    string FullName,
    string Email,
    string Phone,
    StoreRole Role,
    bool IsActive,
    DateTime CreatedAt,
    bool IsOwner = false,
    int? RoleDefId = null,
    string? RoleDefName = null,
    string? RoleDefIcon = null);

/// <summary>Create or update a team member. Password: required on create, optional reset on update.</summary>
public record SaveStoreMemberRequest(
    string FullName,
    string Email,
    StoreRole Role,
    string? Password = null,
    string Phone = "",
    bool IsActive = true,
    /// <summary>Set to hand this person a custom role instead of the preset.</summary>
    int? RoleDefId = null);

// ---------- Who is at work, and where they have been ----------

/// <summary>A teammate's presence: online now, last sitting, and today's traffic.</summary>
public record TeamActivityDto(
    int UserId,
    string FullName,
    string Role,
    bool IsOwner,
    string? Photo,
    string? Icon,
    DateTime? LastLoginAt,
    DateTime? LastSeenAt,
    string? CurrentPage,
    bool IsOnline,
    int PagesToday);

/// <summary>One page a teammate opened.</summary>
public record TeamVisitDto(string Page, DateTime At, string Ip);

/// <summary>The portal's heartbeat: "still here, on this page".</summary>
public record TeamBeatRequest(string? Page);

// ---------- Invoices: the paper trail ----------

/// <summary>One invoice in the partner's management list.</summary>
public record InvoiceRowDto(
    int Id,
    string Number,
    DateTime PlacedAt,
    decimal Total,
    string Status,
    string? TableName,
    string CustomerName,
    int ItemCount,
    bool IsPaid,
    // Lineage: what this replaced, and what replaced it. Both null for an untouched invoice.
    int? ReplacesOrderId = null,
    string? ReplacedByNumber = null);

/// <summary>An invoice's full lines, for the edit dialog.</summary>
public record InvoiceDetailDto(
    int Id,
    string Number,
    DateTime PlacedAt,
    string Status,
    decimal Subtotal,
    decimal TaxPercent,
    decimal TaxAmount,
    decimal Total,
    string? TableName,
    string CustomerName,
    bool IsPaid,
    List<InvoiceLineDto> Lines);

public record InvoiceLineDto(int MenuItemId, string Name, decimal UnitPrice, int Quantity, string? Notes);

public record CancelInvoiceRequest(string? Reason);

/// <summary>Replace an invoice: the original is cancelled, these lines become the new one.</summary>
public record ReplaceInvoiceRequest(List<InvoiceLineDto> Lines, string? Reason, bool MarkPaid = true);

/// <summary>A row in the cancelled-invoices report.</summary>
public record CancelledInvoiceDto(
    string Number,
    DateTime CancelledAt,
    string CancelledBy,
    string? Reason,
    decimal Total,
    int ItemCount,
    DateTime PlacedAt,
    string PlacedBy,
    // When the cancel was part of an edit, the invoice that took its place.
    string? ReplacedByNumber = null);

/// <summary>
/// A row in the edit-history report — the whole story of one correction: who wrote the
/// original and when, who replaced it and when, and both invoices in full.
/// </summary>
public record InvoiceEditDto(
    string OriginalNumber,
    string ReplacementNumber,
    DateTime EditedAt,
    string EditedBy,
    string? Reason,
    InvoiceSnapshotDto Before,
    InvoiceSnapshotDto After);

/// <summary>An invoice frozen at a moment: its author, time, lines and money.</summary>
public record InvoiceSnapshotDto(
    string Number,
    DateTime At,
    string By,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    List<InvoiceLineDto> Lines);

/// <summary>One day of invoices, totalled — the partner's daily report.</summary>
public record DailyInvoicesDto(DailyInvoicesSummaryDto Summary, List<InvoiceRowDto> Rows);

public record DailyInvoicesSummaryDto(
    int Count,
    int Live,
    int Cancelled,
    decimal Total,
    decimal CancelledTotal,
    int ItemCount);
