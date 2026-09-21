using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Models;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>Profile icon the user picked (an emoji); null falls back to the initial letter.</summary>
    public string? AvatarIcon { get; set; }

    /// <summary>An uploaded profile picture as a data URI — squared and squeezed in
    /// the browser before it ever leaves it, so a 4MB camera shot arrives as a few KB.</summary>
    public string? AvatarPhoto { get; set; }

    /// <summary>Stamped by the API on any authenticated request (throttled) — powers "online now".</summary>
    public DateTime? LastSeenAt { get; set; }

    public DriverProfile? DriverProfile { get; set; }
    public List<Address> Addresses { get; set; } = [];
}

/// <summary>Extra state only Driver accounts carry.</summary>
public class DriverProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public VehicleType VehicleType { get; set; }
    public bool IsOnline { get; set; }

    /// <summary>Vehicle plate — shown to the customer while their order is on the way.</summary>
    public string? PlateNumber { get; set; }

    /// <summary>Last GPS fix pushed by the rider app — powers live tracking for customers.</summary>
    public double? CurrentLat { get; set; }
    public double? CurrentLng { get; set; }
    public DateTime? LocationAt { get; set; }

    /// <summary>Document review: a driver can't go online until an admin approves
    /// their ID card and driving license photos (both sides, stored as data-URLs).</summary>
    public DriverVerificationStatus Verification { get; set; } = DriverVerificationStatus.NotSubmitted;
    public string? VerificationReason { get; set; }
    public DateTime? DocsSubmittedAt { get; set; }
    public string? IdCardFront { get; set; }
    public string? IdCardBack { get; set; }
    public string? LicenseFront { get; set; }
    public string? LicenseBack { get; set; }
}

public class Address
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Label { get; set; } = "";
    public string Area { get; set; } = "";
    public string Street { get; set; } = "";
    public string Building { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>Pin dropped on the map — null for addresses typed without one.</summary>
    public double? Lat { get; set; }
    public double? Lng { get; set; }
}

public class Cuisine
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "";
}

/// <summary>
/// One EXTRA type a store carries beyond its main <see cref="Restaurant.CuisineId"/> —
/// the Iranian kitchen that is also an Arabic grill. The main type deliberately does not
/// appear here, so the two can never disagree about which one leads.
/// </summary>
public class RestaurantCuisine
{
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;
    public int CuisineId { get; set; }
    public Cuisine Cuisine { get; set; } = null!;
}

public class Restaurant
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;
    public string Name { get; set; } = "";

    /// <summary>The name in every tongue the owner filled: {"en":"…","ar":"…",…}. Name mirrors the English entry.</summary>
    public string NameLocalized { get; set; } = "{}";

    /// <summary>The written address in every tongue the owner filled. Area mirrors the Arabic entry.</summary>
    public string AddressLocalized { get; set; } = "{}";

    public string Description { get; set; } = "";

    /// <summary>
    /// The shop's handle in short links: orderorange.com/<c>mazagh</c> instead of
    /// /restaurant/5000217. Chosen by the owner, lowercase a-z 0-9 and hyphens, unique
    /// across the platform (a filtered unique index enforces it, so blank is allowed and
    /// only real handles have to be distinct). Empty means the shop has not picked one and
    /// is reachable by id only.
    /// </summary>
    public string Slug { get; set; } = "";

    /// <summary>
    /// The store's MAIN type. A kitchen can carry more than one — an Iranian grill that
    /// is equally an Arabic one — and the extras live in <see cref="ExtraCuisines"/>;
    /// this one stays what browse indexes lead with and what old callers read.
    /// </summary>
    public int CuisineId { get; set; }
    public Cuisine Cuisine { get; set; } = null!;

    /// <summary>Any further types beyond the main one. Most stores have none.</summary>
    public List<RestaurantCuisine> ExtraCuisines { get; set; } = [];

    /// <summary>Which vertical this store belongs to (food, market, pharmacy, …).</summary>
    public StoreType StoreType { get; set; } = StoreType.Restaurant;

    /// <summary>Emoji stands in for a logo image; BannerColor paints the card header.</summary>
    public string LogoEmoji { get; set; } = "🍽️";

    /// <summary>The shop's real logo as a data URI. Null = fall back to the emoji.</summary>
    public string? LogoData { get; set; }

    /// <summary>
    /// The shop delivers with its own people. Its orders are never offered to the
    /// platform's riders — the partner drives them out from the Deliveries page.
    /// </summary>
    public bool SelfDelivery { get; set; }

    /// <summary>
    /// Position in the customer home's "Suggested" strip; null = not featured. Managed
    /// from the admin panel — this replaced the Suggested:RestaurantIds appsettings list,
    /// which needed an API restart per change. (EnsureCreated: column added by hand in SQL.)
    /// </summary>
    public int? SuggestedOrder { get; set; }

    /// <summary>
    /// The owner's switch in Settings: customers may book a table from the store's page
    /// online. Off by default — a shop with no tables must not grow a Reserve button.
    /// </summary>
    public bool OnlineReservations { get; set; }

    /// <summary>
    /// Dishes with no uploaded photo normally borrow a stock picture guessed from
    /// their name. Off, they show their emoji instead — for shops whose names the
    /// guesser gets wrong, or who simply want a photo-free menu.
    /// </summary>
    public bool DishStockPhotos { get; set; } = true;

    /// <summary>
    /// What a booking costs, when the house charges for one: a flat amount for each
    /// table held, plus an amount for every minute it is held. Both zero — the usual
    /// case — means booking is free and no price is shown anywhere.
    /// (EnsureCreated: columns added by hand in SQL.)
    /// </summary>
    public decimal ReservePricePerTable { get; set; }
    public decimal ReservePricePerMinute { get; set; }

    public string BannerColor { get; set; } = "#FFE8D9";

    public string Area { get; set; } = "";
    public string Street { get; set; } = "";
    public string Phone { get; set; } = "";

    // The rest of the store's card: how customers and the platform reach it,
    // and the commercial registration it trades under.
    public string Email { get; set; } = "";
    public string Whatsapp { get; set; } = "";
    public string Website { get; set; } = "";
    public string Instagram { get; set; } = "";
    public string CrNumber { get; set; } = "";

    /// <summary>The store's real VAT registration number; empty = not registered.</summary>
    public string VatNumber { get; set; } = "";

    /// <summary>
    /// The store's OWN mailbox, typed by the owner in Settings — contracts and other
    /// store paperwork go out from here, so the customer sees the restaurant's address
    /// on the envelope, not the platform's. Host empty = fall back to the platform's
    /// Smtp config. (EnsureCreated: columns added by hand in SQL.)
    /// </summary>
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpUser { get; set; } = "";
    public string SmtpPassword { get; set; } = "";
    public string SmtpFrom { get; set; } = "";

    /// <summary>
    /// The owner's own wording on the printed table-QR cards. Empty = the app's
    /// localized default line. (EnsureCreated: column added by hand in SQL.)
    /// </summary>
    public string QrCardText { get; set; } = "";

    /// <summary>Store position — powers "near you" sorting on the customer home.</summary>
    public double? Lat { get; set; }
    public double? Lng { get; set; }

    public decimal DeliveryFee { get; set; }
    public decimal MinOrder { get; set; }
    public int AvgPrepMinutes { get; set; } = 20;

    /// <summary>Owner-controlled: are we taking orders right now?</summary>
    public bool IsOpen { get; set; } = true;

    /// <summary>
    /// Owner-controlled: may customers collect the order from the counter themselves?
    /// On by default — a shop with a door can nearly always hand a bag over it, and a
    /// pickup order costs the shop no delivery at all.
    /// </summary>
    public bool AllowsPickup { get; set; } = true;

    /// <summary>What the POS save button does by default: true = park the bill open.</summary>
    public bool PosDefaultOpen { get; set; } = true;

    /// <summary>POS: saving or closing an invoice also sends it straight to the printer.</summary>
    public bool DirectPrint { get; set; }

    /// <summary>Kitchen-ticket language (ISO code); "" = as ordered. (EnsureCreated: column patched in by Program.cs.)</summary>
    public string KitchenLanguage { get; set; } = "";

    /// <summary>The last table-chat message this store has laid eyes on.</summary>
    public int LastSeenTableChatId { get; set; }

    /// <summary>
    /// Where the registration wizard stopped: 0-4 = still walking, -1 = finished
    /// (every store born before the wizard counts as finished).
    /// </summary>
    public int SetupStep { get; set; } = -1;

    /// <summary>Admin-controlled: a new restaurant is invisible to customers until approved.</summary>
    public bool IsApproved { get; set; }

    /// <summary>Platform's cut of every order's subtotal, in percent.</summary>
    public decimal CommissionPercent { get; set; } = 15m;

    /// <summary>VAT charged on the subtotal. Oman's standard rate, 5%, is the default;
    /// the partner may change it (0 for exempt goods).</summary>
    public decimal TaxPercent { get; set; } = 5m;

    public DateTime CreatedAt { get; set; }

    public List<MenuCategory> Categories { get; set; } = [];
    public List<MenuItem> Items { get; set; } = [];
}

/// <summary>
/// A staff login attached to ONE store: the user signs into the partner portal,
/// the role decides which rooms of it they may enter. Ownership outranks this.
/// </summary>
public class StoreMember
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>The built-in preset. Ignored when <see cref="RoleDefId"/> is set.</summary>
    public StoreRole Role { get; set; }

    /// <summary>
    /// A role the owner wrote themselves. When set it REPLACES the preset: the
    /// member's permissions are exactly the ones listed on that role.
    /// </summary>
    public int? RoleDefId { get; set; }
    public StoreRoleDef? RoleDef { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// A role invented by the owner — "Shift lead", "Accountant" — holding whatever
/// mix of permissions their shop actually needs. The four presets cover the common
/// shapes; this covers the rest, and belongs to one store only.
/// </summary>
public class StoreRoleDef
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>An emoji so the role is recognisable at a glance in a list.</summary>
    public string Icon { get; set; } = "🔑";

    /// <summary>
    /// Null for a role the owner invented. Set to a <see cref="StoreRole"/> value when
    /// this row is the store's REWRITE of that preset: the preset keeps its name and
    /// face, but opens the doors listed here instead of the built-in ones.
    /// </summary>
    public int? PresetRole { get; set; }

    /// <summary>The permission keys this role grants, comma separated (see Perm).</summary>
    public string Perms { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    /// <summary>The permission keys as a set, ready to test against.</summary>
    public HashSet<string> PermSet() =>
        new((Perms ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>
/// One weekday's opening window. No rows for a restaurant = open whenever the
/// manual switch is on; Close &lt; Open spans midnight (e.g. 18:00–02:00).
/// </summary>
/// <summary>
/// Inverted name index: one row per (word, storeId), capped to the first few hundred
/// stores per word — big-data search finds candidate stores by seek, never by scan.
/// </summary>
public class RestaurantWord
{
    public string Word { get; set; } = "";
    public int RestaurantId { get; set; }
}

public class RestaurantHours
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;

    /// <summary>0 = Sunday … 6 = Saturday (matches .NET DayOfWeek).</summary>
    public int Day { get; set; }
    public bool IsClosed { get; set; }
    public TimeSpan Open { get; set; }
    public TimeSpan Close { get; set; }
}

public class MenuCategory
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public List<MenuItem> Items { get; set; } = [];
}

public class MenuItem
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;
    public int CategoryId { get; set; }
    public MenuCategory Category { get; set; } = null!;

    /// <summary>Where the product stands inside its group: lowest first, ties by name.</summary>
    public int SortOrder { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>What it is made of, "; "-joined — the partner writes these freely.</summary>
    public string Ingredients { get; set; } = "";

    /// <summary>How it is sold: a unit key like "kg" or "piece"; empty = not shown.</summary>
    public string Unit { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string ImageEmoji { get; set; } = "🍽️";
    public bool IsPopular { get; set; }

    /// <summary>Owner-uploaded product photo (compressed data URL); null falls back to stock photos/emoji.</summary>
    public string? PhotoData { get; set; }

    // ---------- When may this be ordered? ----------
    // Null/empty = always. Minutes since midnight; To < From spans midnight, same rule
    // as the store's working hours. Days is a CSV of DayOfWeek ints ("5,6" = Fri,Sat).
    public int? AvailableFromMinutes { get; set; }
    public int? AvailableToMinutes { get; set; }
    public string AvailableDays { get; set; } = "";

    /// <summary>
    /// How many days' notice this item needs. 0 = made now; 2 = order today, collect
    /// in two days (cakes, bouquets, catering trays). A lead-time item may be ORDERED
    /// at any hour — the window above says when it is made, not when it is bought.
    /// </summary>
    public int LeadTimeDays { get; set; }

    /// <summary>Partner-set discount in percent (0 = none). Customers pay Price minus this.</summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>
    /// Multilingual search keywords stored per product ("water ماء مياه آب پانی …") —
    /// search matches these, not just the title. Auto-filled from the keyword
    /// dictionary whenever the item is created or renamed.
    /// </summary>
    public string SearchKeywords { get; set; } = "";
}

public class Order
{
    public int Id { get; set; }

    /// <summary>Human-facing number, e.g. MF-1042. Assigned right after the first save.</summary>
    public string Number { get; set; } = "";

    public int CustomerId { get; set; }
    public User Customer { get; set; } = null!;
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;

    /// <summary>Null until a driver claims the delivery.</summary>
    public int? DriverUserId { get; set; }
    public User? Driver { get; set; }

    // ---- The shop's OWN courier, when the store delivers for itself ----
    // No platform rider is involved, so the person and their position live on the
    // order rather than on a DriverProfile.
    public string? CourierName { get; set; }
    public string? CourierPhone { get; set; }
    public double? CourierLat { get; set; }
    public double? CourierLng { get; set; }

    /// <summary>When the courier's position was last pushed — a stale pin is not shown.</summary>
    public DateTime? CourierAt { get; set; }

    public OrderStatus Status { get; set; }
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>Set when this invoice is the corrected copy of a cancelled one.</summary>
    public int? ReplacesOrderId { get; set; }

    /// <summary>
    /// Delivery, or the customer collecting it themselves. Pickup pays no delivery fee,
    /// needs no address and must never reach a rider.
    /// </summary>
    public OrderType OrderType { get; set; } = OrderType.Delivery;

    /// <summary>Snapshot of the chosen address — editing the address book never rewrites history.</summary>
    public string DeliveryAddress { get; set; } = "";
    public double? DeliveryLat { get; set; }
    public double? DeliveryLng { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal ServiceFee { get; set; }
    public decimal Discount { get; set; }

    // VAT, snapshotted at order time — the rate the STORE had when the order was
    // placed, so a later settings change never rewrites old bills.
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }
    public string? CouponCode { get; set; }

    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public int EstimatedMinutes { get; set; }

    /// <summary>True when paid online (test gateway); cash/card-on-delivery orders stay false.</summary>
    public bool IsPaid { get; set; }
    public string? PaymentRef { get; set; }

    /// <summary>Raw materials already deducted for this order — deduction happens once, ever.</summary>
    public bool StockApplied { get; set; }

    public DateTime PlacedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    /// <summary>
    /// The promised day, when the basket held advance-notice items. Null = today's
    /// normal flow. Set to the LONGEST lead time in the basket — a cake needing two
    /// days makes the whole order a two-days-from-now order.
    /// </summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>Dine-in: which table this order is for. Text, so history survives the
    /// table being renamed or removed.</summary>
    public string? TableName { get; set; }

    public List<OrderItem> Items { get; set; } = [];
    public List<OrderEvent> Events { get; set; } = [];
    public Review? Review { get; set; }
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    /// <summary>Deliberately NOT a foreign key — the order keeps its snapshot even if the dish is deleted.</summary>
    public int MenuItemId { get; set; }

    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Status timeline entry — powers the customer's live tracking view.</summary>
/// <summary>
/// The paper trail for invoices. An order is never edited in place: correcting one means
/// cancelling it and writing a replacement, and this row records the whole act — who did
/// it, when, why, and the full before/after as JSON. The reports read ONLY this table, so
/// the history stays true even if the orders themselves are later purged.
/// </summary>
public class InvoiceAudit
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }

    /// <summary>The invoice acted on.</summary>
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = "";

    /// <summary>The corrected copy, when the action was an edit; null for a plain cancel.</summary>
    public int? ReplacementOrderId { get; set; }
    public string? ReplacementNumber { get; set; }

    /// <summary>"cancel" or "edit".</summary>
    public string Action { get; set; } = "";

    public int ActorUserId { get; set; }
    public string ActorName { get; set; } = "";
    public string? Reason { get; set; }

    /// <summary>The invoice as it stood (items, totals, who wrote it and when).</summary>
    public string BeforeJson { get; set; } = "";

    /// <summary>The replacement as written; null for a cancel.</summary>
    public string? AfterJson { get; set; }

    public DateTime At { get; set; }
}

public class OrderEvent
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public OrderStatus Status { get; set; }
    public DateTime At { get; set; }
    public string By { get; set; } = "";
}

public class Review
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int RestaurantId { get; set; }
    public int CustomerId { get; set; }
    public int? DriverUserId { get; set; }
    public int RestaurantRating { get; set; }
    public int? DriverRating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// A saved payment card — TEST mode only. Only the brand, holder, expiry and the
/// last four digits are ever stored; the full number never touches the database.
/// </summary>
public class SavedCard
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string Brand { get; set; } = "Visa";
    public string HolderName { get; set; } = "";
    public string Last4 { get; set; } = "";
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
}

/// <summary>A customer's ♥ on a restaurant.</summary>
public class FavoriteRestaurant
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;
}

/// <summary>
/// One anonymous store/product view for partner analytics. Deliberately stores NO
/// user identity — partners only ever see counts. MenuItemId null = store page
/// visit; set (with ItemName snapshot) = a product was opened.
/// </summary>
/// <summary>
/// One thing a customer looked for, and whether the catalog had an answer. The terms
/// that come back EMPTY are the most valuable line in the whole system: they are
/// demand the platform is failing to serve.
/// </summary>
public class SearchLog
{
    public int Id { get; set; }

    /// <summary>0 for a guest — searching never requires an account.</summary>
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Term { get; set; } = "";

    /// <summary>How many stores and dishes came back together.</summary>
    public int Results { get; set; }
    public string App { get; set; } = "Customer";
    public string Locale { get; set; } = "";

    /// <summary>Where it came from — the only handle a guest search has.</summary>
    public string Ip { get; set; } = "";
    public DateTime At { get; set; }
}

public class StoreVisit
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int? MenuItemId { get; set; }
    public string? ItemName { get; set; }
    public DateTime At { get; set; }
}

/// <summary>One page visit in one of the four apps — the admin activity feed.</summary>
public class ActivityLog
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public UserRole Role { get; set; }

    /// <summary>Which app: Customer, Partner, Rider, Admin.</summary>
    public string App { get; set; } = "";
    public string Page { get; set; } = "";

    /// <summary>Where the visit came from — shown beside the exact time in the admin feed.</summary>
    public string Ip { get; set; } = "";
    public DateTime At { get; set; }
}

/// <summary>
/// One order-chat message. Attachments (images, PDFs, voice notes) are stored as
/// size-capped data URLs so they flow through the same server-side API path with
/// no extra file hosting. "location" messages carry "lat,lng" in Text.
/// </summary>
/// <summary>Store gallery photo (compressed data URL) — up to four per restaurant,
/// shown to customers as an animated banner slideshow.</summary>
public class RestaurantPhoto
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;
    public string Data { get; set; } = "";

    /// <summary>Exactly one per store — shown first in the customer banner slideshow.</summary>
    public bool IsMain { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Product photo (compressed data URL) — up to four per menu item, one marked main.
/// The main photo is also cached on MenuItem.PhotoData so menu lists need no join.</summary>
public class MenuItemPhoto
{
    public int Id { get; set; }
    public int MenuItemId { get; set; }
    public string Data { get; set; } = "";
    public bool IsMain { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>The store's printed-receipt design, set in the partner "Invoice designer".</summary>
public class ReceiptDesign
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string? LogoData { get; set; }
    public string? HeaderMessage { get; set; }
    public string? FooterMessage { get; set; }
    public string? Promo { get; set; }
    public string Font { get; set; } = "mono";
    public string FontSize { get; set; } = "normal";
    public int PaperWidth { get; set; } = 80;
    public bool ShowBarcode { get; set; } = true;
    public bool ShowVat { get; set; } = true;
    public bool ShowCourier { get; set; } = true;
    public string Separator { get; set; } = "dash";
    public string TotalStyle { get; set; } = "plain";
    public string Spacing { get; set; } = "normal";
    public string LabelStyle { get; set; } = "en";
    public bool ShowQr { get; set; } = true;
    public bool ShowAddress { get; set; } = true;
    public string HeaderStyle { get; set; } = "normal";
    public string ItemStyle { get; set; } = "lines";
    public string Frame { get; set; } = "none";
    public string Ink { get; set; } = "normal";
    public string LogoSize { get; set; } = "normal";
    public string Stamp { get; set; } = "none";
    public int Copies { get; set; } = 1;

    /// <summary>What the receipt's QR encodes — any link the partner sets (menu, Instagram, WhatsApp…).</summary>
    public string? QrLink { get; set; }

    /// <summary>"verify" = the bill's own signed page, "store" = the store page, "link" = <see cref="QrLink"/>.</summary>
    public string QrMode { get; set; } = "verify";

    /// <summary>Printed under the QR instead of the stock caption.</summary>
    public string? QrCaption { get; set; }

    /// <summary>"small" | "normal" | "large" — how many print dots per QR module.</summary>
    public string QrSize { get; set; } = "normal";

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A running cost the store pays — electricity, rent, phone, salaries… Kept apart
/// from customer orders: this is the owner's own expense book.
/// </summary>
public class StoreBill
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }

    /// <summary>electricity|water|phone|internet|rent|salary|gas|supplies|maintenance|tax|other</summary>
    public string Category { get; set; } = "other";

    public string Title { get; set; } = "";
    public string? Vendor { get; set; }

    /// <summary>Account or invoice number printed on the bill.</summary>
    public string? Reference { get; set; }

    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidAt { get; set; }

    /// <summary>Repeats every month — the list offers to roll it forward when paid.</summary>
    public bool Recurring { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// A raw ingredient the kitchen buys and consumes — rice, oil, saffron, lamb.
/// Not a menu item: nobody orders a sack of rice. The store tracks how much is left,
/// what it costs, and when it drops below the level worth reordering at.
/// </summary>
public class StoreMaterial
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>grain|oil|meat|dairy|vegetable|spice|drink|packaging|other</summary>
    public string Category { get; set; } = "other";

    /// <summary>kg|g|l|ml|pcs|box|bag — how this ingredient is counted.</summary>
    public string Unit { get; set; } = "kg";

    /// <summary>How much is in the store right now.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Below this, the kitchen is about to run out and the row turns red.</summary>
    public decimal MinQuantity { get; set; }

    /// <summary>What one unit costs — quantity × cost is the value sitting on the shelf.</summary>
    public decimal UnitCost { get; set; }

    public string? Supplier { get; set; }
    public string? Notes { get; set; }

    /// <summary>The store's own short code (SKU). Empty falls back to M{Id} on labels.</summary>
    public string? Code { get; set; }

    /// <summary>The number under the barcode on the box, when the supplier printed one.</summary>
    public string? Barcode { get; set; }

    /// <summary>When stock was last counted or topped up.</summary>
    public DateTime? RestockedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One line of a product's recipe: selling one of this menu item consumes this much
/// of that material. The whole recipe is the set of lines for a MenuItemId.
/// </summary>
public class ProductMaterial
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int MenuItemId { get; set; }
    public int MaterialId { get; set; }

    /// <summary>How much of the material one sold portion uses, in the material's unit.</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// A purchase invoice for raw materials — the truck that arrived from the supplier.
/// Saving it puts every line's quantity on the shelf and refreshes each unit cost.
/// </summary>
public class MaterialPurchase
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }

    /// <summary>The supplier's invoice number, as printed on their paper.</summary>
    public string Number { get; set; } = "";
    public string Supplier { get; set; } = "";
    public DateTime InvoiceDate { get; set; }

    public decimal Total { get; set; }
    public bool IsPaid { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public List<MaterialPurchaseLine> Lines { get; set; } = [];
    public List<PurchasePayment> Payments { get; set; } = [];
}

/// <summary>
/// A saved shopping list: "the weekly meat order". One tap fills the receiving
/// ticket. Lines are JSON — [{id, p(roduct), q(ty)}] — small, versionless, enough.
/// </summary>
public class PurchaseTemplate
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string LinesJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Who the store buys from. Purchases link to suppliers by NAME on purpose — the
/// paper invoice already carries the name, and renaming a supplier must not rewrite
/// history.
/// </summary>
public class Supplier
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? ContactName { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// "برنج is running low" — born when an order's deduction crosses a threshold,
/// cleared when the owner opens the bell. One unseen alert per material per kind.
/// </summary>
public class StoreAlert
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int MaterialId { get; set; }

    /// <summary>Name frozen at alert time, so the bell reads even after a rename.</summary>
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "kg";

    /// <summary>What was left when the alert fired.</summary>
    public decimal Quantity { get; set; }

    /// <summary>low | out</summary>
    public string Type { get; set; } = "low";

    public DateTime CreatedAt { get; set; }
    public DateTime? SeenAt { get; set; }
}

/// <summary>
/// One promised payment against a purchase invoice. A cash sale is one paid row;
/// "pay next month" is one unpaid row with a due date; an installment plan is many.
/// The invoice's IsPaid stays synced: true only when every row here is paid.
/// </summary>
public class PurchasePayment
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public MaterialPurchase Purchase { get; set; } = null!;

    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }

    /// <summary>Null while the money is still owed.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>cash | card | transfer | cheque</summary>
    public string Method { get; set; } = "cash";

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MaterialPurchaseLine
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public MaterialPurchase Purchase { get; set; } = null!;

    /// <summary>A finished product bought for resale — recorded on the invoice, but it
    /// never touches the raw-material shelf.</summary>
    public bool IsProduct { get; set; }

    /// <summary>Material id — or the menu item id when IsProduct.</summary>
    public int MaterialId { get; set; }

    /// <summary>Name frozen at purchase time — the invoice must survive later renames.</summary>
    public string MaterialName { get; set; } = "";
    public string Unit { get; set; } = "kg";

    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
}

/// <summary>Someone who works at the store — barista, chef, cashier, cleaner…</summary>
/// <summary>
/// A customer the store keeps in its own book — the person who phones in an order or
/// walks up to the counter. They have no app, no password and never signed up.
///
/// Orders still hang off a real <see cref="User"/> row, because everything downstream
/// (the driver app, chat, receipts, reports) reads the customer from there. So each
/// entry owns a shadow account nobody can sign into. If the store knows a real email,
/// that account IS the customer's: the day they sign in with Google or a code, their
/// phone-order history is already waiting for them.
/// </summary>
public class StoreCustomer
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;

    /// <summary>The account orders are placed under. Shared if two stores know the same phone.</summary>
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";

    /// <summary>Where they usually want it. Copied onto each order, never read back from here.</summary>
    public string Address { get; set; } = "";
    public double? Lat { get; set; }
    public double? Lng { get; set; }

    /// <summary>"Ring the bell twice", "allergic to sesame" — for the store's own eyes.</summary>
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastOrderAt { get; set; }
    public int OrderCount { get; set; }
}

/// <summary>
/// A physical table in the shop's dining room. The floor plan belongs to the store —
/// customers never see tables; the counter seats people and tags dine-in orders.
/// </summary>
/// <summary>
/// A drawn zone on the floor plan — "Salon", "Family room", "Outside" — a rectangle
/// the owner sketches on the canvas and then fills with tables. Purely spatial: a
/// table is "in" a room by standing inside it on the plan.
/// </summary>
public class StoreRoom
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;

    public string Name { get; set; } = "";

    /// <summary>The floor this room is drawn on. "" = the main floor.</summary>
    public string Floor { get; set; } = "";

    /// <summary>Where this salon stands in the page's list — the owner reorders freely.</summary>
    public int SortOrder { get; set; }

    // Top-left corner and size, as percentages of the plan canvas.
    public double X { get; set; } = 10;
    public double Y { get; set; } = 10;
    public double W { get; set; } = 30;
    public double H { get; set; } = 30;

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// A table booked from its own QR code — no account, no app, just a name and phone
/// typed on the public page the QR opens. The store confirms, cancels, or seats it.
/// </summary>
public class TableReservation
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;

    // Plain id + name snapshot: the reservation must survive the table being redrawn.
    // TableId 0 = booked ONLINE from the store's page, no particular table — the
    // house picks one when the party arrives.
    public int TableId { get; set; }
    public string TableName { get; set; } = "";

    /// <summary>
    /// Every table the party booked, comma-separated — a big group takes two or three
    /// together. TableId/TableName hold the first of them, so everything that only ever
    /// knew about one table still reads right; TableName lists them all for the eye.
    /// Empty = the single table in TableId.
    /// (EnsureCreated: column added by hand in SQL.)
    /// </summary>
    public string TableIds { get; set; } = "";

    /// <summary>The signed-in customer who booked online, when there is one — their
    /// phone rings when the store confirms. QR walk-ups stay account-less (null).</summary>
    public int? CustomerId { get; set; }

    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public int Guests { get; set; } = 2;

    /// <summary>When the party wants the table.</summary>
    public DateTime At { get; set; }

    /// <summary>
    /// How long they expect to keep it, in minutes — the guest's own estimate ("we'll be
    /// two hours"). The floor uses it to see when the table frees up again.
    /// (EnsureCreated: column added by hand in SQL.)
    /// </summary>
    public int DurationMinutes { get; set; } = 90;

    public string? Note { get; set; }

    /// <summary>
    /// What the house quoted for this booking, frozen the moment it was made — the
    /// store's per-table and per-minute prices as they stood. 0 = booking is free.
    /// (EnsureCreated: column added by hand in SQL.)
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>pending | confirmed | cancelled | seated.</summary>
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// A standing supply agreement the store writes for one of its customers — "10 trays
/// of kabsa, delivered 10 times a month, at 45 rials a month". The store keeps it on
/// file and can email it to the customer as a lettered document wearing the store's
/// own logo and details. Paperwork only: it books no orders by itself.
/// (EnsureCreated: table created by hand in SQL.)
/// </summary>
public class StoreContract
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;

    public string CustomerName { get; set; } = "";
    public string CustomerEmail { get; set; } = "";
    public string CustomerPhone { get; set; } = "";

    /// <summary>The goods, as JSON [{n,q,p}] — name, quantity per delivery, unit price.</summary>
    public string ItemsJson { get; set; } = "[]";

    /// <summary>How many deliveries each month carries.</summary>
    public int TimesPerMonth { get; set; } = 1;

    /// <summary>How many months the agreement runs. 0 = open-ended.</summary>
    public int Months { get; set; } = 1;

    /// <summary>The agreed price per month — typed by the owner, not computed.</summary>
    public decimal MonthlyPrice { get; set; }

    public DateTime StartDate { get; set; }
    public string? Note { get; set; }

    /// <summary>When the contract was last emailed to the customer. Null = never sent.</summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// The customer said yes — from the public page, holding the signed link. The name
    /// is whatever they typed under the acceptance; the letter then wears the stamp.
    /// (EnsureCreated: columns added by hand in SQL.)
    /// </summary>
    public DateTime? AcceptedAt { get; set; }
    public string AcceptedBy { get; set; } = "";

    /// <summary>What the customer wrote back, as JSON [{t,at}] — newest last.</summary>
    public string RepliesJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// The open invoice a seated party runs up — the "tab". Opened the moment someone is
/// seated, it collects lines while they eat and turns into a real Order (receipt, tax
/// and all) when the bill is called. One open tab per table, deleted once closed.
/// </summary>
public class StoreTab
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;

    // Plain ids on purpose: another FK web onto StoreTables/StoreCustomers would
    // trip SQL Server's multiple-cascade-path rule. Ownership is enforced in code.
    public int TableId { get; set; }
    public int? StoreCustomerId { get; set; }
    public string? GuestName { get; set; }

    public DateTime OpenedAt { get; set; }

    public List<StoreTabLine> Lines { get; set; } = [];
}

/// <summary>One line on an open tab — name and price snapshotted when it was added.</summary>
public class StoreTabLine
{
    public int Id { get; set; }
    public int StoreTabId { get; set; }
    public StoreTab Tab { get; set; } = null!;

    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }

    /// <summary>When the line was rung up; every line of one round shares the stamp.
    /// Source "qr" = the guest's own phone, null = the staff till.
    /// (EnsureCreated: columns added by hand in SQL.)</summary>
    public DateTime AddedAt { get; set; }
    public string? Source { get; set; }
}

public class StoreTable
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public Restaurant Restaurant { get; set; } = null!;

    /// <summary>"T1", "Table 5", "Terrace 2" — whatever the floor already calls it.</summary>
    public string Name { get; set; } = "";

    /// <summary>indoor | outdoor | vip | family | counter | other.</summary>
    public string Type { get; set; } = "indoor";

    public int Seats { get; set; } = 4;

    /// <summary>Which floor of the venue this table stands on. "" = the main floor.</summary>
    public string Floor { get; set; } = "";

    /// <summary>The salon this table belongs to — null = standing on open floor.</summary>
    public int? RoomId { get; set; }

    /// <summary>How the table is drawn on the plan: square | round | rect.</summary>
    public string Shape { get; set; } = "square";

    // Where the table sits on the 2D floor plan, as percentages of the canvas —
    // percentages survive any screen size, so the phone and the till agree on the room.
    public double X { get; set; } = 50;
    public double Y { get; set; } = 50;

    // The table's drawn size on the plan, in canvas pixels. 0 = the default size.
    public double W { get; set; }
    public double H { get; set; }

    // ---------- Who is sitting here right now ----------
    /// <summary>Seated from the customer book — orders can be taken for them directly.</summary>
    public int? StoreCustomerId { get; set; }
    public StoreCustomer? StoreCustomer { get; set; }

    /// <summary>A walk-in with no book entry: just a name so the floor knows the party.</summary>
    public string? GuestName { get; set; }

    public DateTime? OccupiedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class StoreStaff
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string FullName { get; set; } = "";

    /// <summary>barista|chef|cook|cashier|waiter|manager|cleaner|driver|helper|other</summary>
    public string Role { get; set; } = "other";

    public string? Phone { get; set; }
    public string? NationalId { get; set; }
    public string? Photo { get; set; }
    public decimal MonthlySalary { get; set; }
    public DateTime HiredOn { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The team login this person clocks in with (StoreMember.UserId). Null = not linked yet.</summary>
    public int? UserId { get; set; }
}

/// <summary>One month's salary paid to one staff member. Unique per (staff, period).</summary>
public class SalaryPayment
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int StaffId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public decimal BaseSalary { get; set; }
    public decimal Bonus { get; set; }
    public decimal Deduction { get; set; }
    public decimal NetPaid { get; set; }

    /// <summary>cash|bank|transfer</summary>
    public string Method { get; set; } = "cash";

    public DateTime PaidAt { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// One shift of one signed-in team member: "I'm here" with the phone's position, and
/// "Leaving" with the position again. Tied to the staff register through
/// <see cref="StoreStaff.UserId"/> so payroll can show hours worked; kept even when
/// that link is missing, because the person still worked.
/// </summary>
public class StaffAttendance
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public DateTime InAt { get; set; }
    public double? InLat { get; set; }
    public double? InLng { get; set; }
    public double? InAccuracy { get; set; }
    public DateTime? OutAt { get; set; }
    public double? OutLat { get; set; }
    public double? OutLng { get; set; }
    public double? OutAccuracy { get; set; }
}

/// <summary>
/// Time off asked for by a team member: a few hours ("hourly", same day) or whole days
/// ("daily", inclusive dates). The owner approves or rejects; approved leave shows
/// beside worked hours in the attendance report and on payroll.
/// </summary>
public class StaffLeave
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    /// <summary>hourly | daily</summary>
    public string Kind { get; set; } = "daily";
    public DateTime FromAt { get; set; }
    public DateTime ToAt { get; set; }
    public string? Reason { get; set; }
    /// <summary>pending | approved | rejected</summary>
    public string Status { get; set; } = "pending";
    public DateTime RequestedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>
/// The store's shared calendar (ported from the MajidTekLaw office calendar): one row
/// per meeting, delivery, deadline or note, seen by the whole team. A reminder is a
/// number of minutes before the start; when it comes due the team's bells ring once and
/// <see cref="RemindedAt"/> records that it did.
/// </summary>
public class StoreCalendarEvent
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public string Title { get; set; } = "";
    public string? Details { get; set; }
    /// <summary>Hex colour of the chip.</summary>
    public string Color { get; set; } = "#ff7a1a";
    public DateTime StartAt { get; set; }
    public DateTime? EndAt { get; set; }
    public bool AllDay { get; set; }
    public int? RemindMinutes { get; set; }
    public DateTime? RemindedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// Order chat is no longer a SQL entity — it lives in MongoDB as ChatDoc
// (see Data/ChatStore.cs). The legacy ChatMessages table is left in place as a
// backup of the imported conversations and is never read.

public class Coupon
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public decimal Percent { get; set; }
    public decimal MinOrder { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaxUses { get; set; }
    public int Uses { get; set; }
}

/// <summary>
/// Which print station a product's kitchen ticket goes to on the shop's till
/// (LocalHandler). "kitchen" / "bar"; a product with no row prints nowhere special —
/// the till's own defaults decide. The cashier RECEIPT always prints everything.
/// </summary>
public class PrintRoute
{
    public int RestaurantId { get; set; }
    public int MenuItemId { get; set; }
    public string Station { get; set; } = "kitchen";
}

/// <summary>
/// Where a teammate is right now. The visit LOG says where they have been; this
/// says whether they are still there — a heartbeat keeps it warm, and a stale row
/// simply means the person has gone.
/// </summary>
public class TeamPresence
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int UserId { get; set; }

    /// <summary>Start of the current sitting — a long gap counts as a new one.</summary>
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? CurrentPage { get; set; }
}

/// <summary>
/// One browser's Web Push subscription for one store's devices — the address a new
/// order rings at even when the portal is closed. Endpoint is unique per browser.
/// </summary>
public class PushSubscriptionRow
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }

    /// <summary>Set for a CUSTOMER's device (order-status pushes); null for store devices.</summary>
    public int? UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
