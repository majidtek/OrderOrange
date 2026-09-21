namespace OrderOrange.Shared;

/// <summary>What the customer sees on the browse grid.</summary>
public record RestaurantCardDto(
    int Id,
    string Name,
    string Description,
    string Cuisine,
    string LogoEmoji,
    string BannerColor,
    string Area,
    double Rating,
    int RatingCount,
    decimal DeliveryFee,
    decimal MinOrder,
    int AvgPrepMinutes,
    bool IsOpen,
    string? TodayHours = null,
    StoreType StoreType = StoreType.Restaurant,
    double? DistanceKm = null,
    // Does this shop let customers collect from the counter? Drives the checkout toggle.
    bool AllowsPickup = true,
    // VAT the shop charges on the subtotal (Oman standard rate 5%). Checkout shows it.
    decimal TaxPercent = 5m,
    // The shop's uploaded logo as a data URI; null means show LogoEmoji instead.
    string? LogoPhoto = null,
    // The name the owner wrote in each language. A real shop names itself; only the
    // generated catalog needs its name rebuilt word by word at render time.
    Dictionary<string, string>? Names = null,
    // Every type the shop carries, main first — "Iranian" then "Arabic & Grill".
    // Null or empty means the single Cuisine above is the whole story.
    List<string>? Cuisines = null,
    // The store has REAL banner photos — cards show them instead of stock slides.
    bool HasPhotos = false,
    // The owner takes table bookings from this page — shows the Reserve button.
    bool OnlineReservations = false,
    // What a booking costs, when the house charges: flat per table + per minute held.
    // Both zero (the usual case) means booking is free and no price is shown.
    decimal ReservePricePerTable = 0m,
    decimal ReservePricePerMinute = 0m,
    // Dishes without their own photo borrow a stock picture; off = emoji instead.
    bool DishStockPhotos = true,
    // The shop's own contacts, straight on its page: tap-to-call and Instagram.
    string Phone = "",
    string Instagram = "",
    // Where the shop actually stands — the page's map link. Null = no pin set.
    double? MapLat = null,
    double? MapLng = null,
    // The handle in short links (orderorange.com/<slug>) — the page's CANONICAL address.
    string Slug = "");

/// <summary>One weekday's schedule. Times as "HH:mm"; Close before Open spans midnight.</summary>
public record DayHoursDto(int Day, bool IsClosed, string Open, string Close);

public record MenuItemDto(
    int Id,
    int CategoryId,
    string Name,
    string Description,
    decimal Price,
    bool IsAvailable,
    string ImageEmoji,
    bool IsPopular,
    string? Photo = null,
    decimal DiscountPercent = 0,
    ProductStatus Status = ProductStatus.Approved,
    string? RejectionReason = null,
    // When may this be ordered? Nulls/empty = always. Same shape as the entity.
    int? AvailableFromMinutes = null,
    int? AvailableToMinutes = null,
    string AvailableDays = "",
    int LeadTimeDays = 0,
    string Ingredients = "",
    string Unit = "",
    Dictionary<string, string>? Names = null,
    // What the dish IS, in each language the partner wrote. A menu that names a dish in
    // Persian but explains it only in English is half-translated to the reader.
    Dictionary<string, string>? Descriptions = null,
    // Where the product stands inside its group: lowest first, ties fall back to the name.
    int SortOrder = 0,
    bool InStoreOnly = false)
{
    public bool HasDiscount => DiscountPercent > 0;

    /// <summary>The viewer's language first; a missing translation falls back to Arabic, then the canonical name.</summary>
    public string NameFor(string locale) =>
        PickName(locale) ?? PickName("ar") ?? Name;

    private string? PickName(string locale) =>
        Names is not null && Names.TryGetValue(locale, out var name) && name.Length > 0 ? name : null;

    /// <summary>The description in the viewer's language, falling back the same way.</summary>
    public string DescriptionFor(string locale) =>
        Pick(Descriptions, locale) ?? Pick(Descriptions, "ar") ?? Description;

    private static string? Pick(Dictionary<string, string>? from, string locale) =>
        from is not null && from.TryGetValue(locale, out var text) && text.Length > 0 ? text : null;

    /// <summary>The stored materials, ready to wear as chips. Empty list = none set.</summary>
    public string[] IngredientList =>
        Ingredients.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Waiting for an administrator — the partner sees it, customers do not.</summary>
    public bool IsPending => Status == ProductStatus.Pending;
    public bool IsRejected => Status == ProductStatus.Rejected;

    /// <summary>What the customer actually pays — Price minus the partner's discount.</summary>
    public decimal FinalPrice => DiscountPercent <= 0 ? Price : Math.Round(Price * (1 - DiscountPercent / 100m), 3);

    /// <summary>Restricted at all — by clock, by weekday, or by needing notice?</summary>
    public bool HasWindow => AvailableFromMinutes is not null || AvailableDays.Length > 0;

    /// <summary>May it go into the basket right now? (Lead-time items always may.)</summary>
    public bool OrderableAt(DateTime now) => ItemAvailability.IsOrderable(this, now);
}

/// <summary>
/// The one place the "may this be ordered now?" question is answered — the API enforces
/// with it and the apps explain with it, so the door and the sign can never disagree.
/// </summary>
public static class ItemAvailability
{
    public static bool IsOrderable(MenuItemDto item, DateTime now)
    {
        // Needing notice is not a closed door: a two-day cake is ordered TODAY.
        if (item.LeadTimeDays > 0) return true;
        return IsWithinWindow(item.AvailableFromMinutes, item.AvailableToMinutes, item.AvailableDays, now);
    }

    public static bool IsWithinWindow(int? fromMinutes, int? toMinutes, string days, DateTime now)
    {
        if (days.Length > 0)
        {
            var today = ((int)now.DayOfWeek).ToString();
            var listed = days.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!listed.Contains(today)) return false;
        }

        if (fromMinutes is null || toMinutes is null) return true;

        var minute = (int)now.TimeOfDay.TotalMinutes;
        // To before From spans midnight (18:00–02:00) — same rule as store hours.
        return fromMinutes <= toMinutes
            ? minute >= fromMinutes && minute < toMinutes
            : minute >= fromMinutes || minute < toMinutes;
    }

    /// <summary>"06:00–11:00" — the clock part of the sign on the door.</summary>
    public static string TimeLabel(int? fromMinutes, int? toMinutes) =>
        fromMinutes is null || toMinutes is null
            ? ""
            : $"{fromMinutes / 60:00}:{fromMinutes % 60:00}–{toMinutes / 60:00}:{toMinutes % 60:00}";
}

public record MenuCategoryDto(int Id, string Name, int SortOrder, List<MenuItemDto> Items,
    Dictionary<string, string>? Names = null)
{
    /// <summary>The viewer's language first; a missing translation falls back to Arabic, then the canonical name.</summary>
    public string NameFor(string locale) =>
        PickName(locale) ?? PickName("ar") ?? Name;

    private string? PickName(string locale) =>
        Names is not null && Names.TryGetValue(locale, out var name) && name.Length > 0 ? name : null;
}

public record RestaurantDetailDto(RestaurantCardDto Info, List<MenuCategoryDto> Categories);

/// <summary>The owner's own restaurant — includes commercial fields the public never sees.</summary>
public record MyRestaurantDto(
    int Id,
    string Name,
    string Description,
    int CuisineId,
    string Cuisine,
    string LogoEmoji,
    string BannerColor,
    string Area,
    string Street,
    string Phone,
    decimal DeliveryFee,
    decimal MinOrder,
    int AvgPrepMinutes,
    bool IsOpen,
    bool IsApproved,
    decimal CommissionPercent,
    double Rating,
    int RatingCount,
    StoreType StoreType = StoreType.Restaurant,
    bool AllowsPickup = true,
    decimal TaxPercent = 5m,
    bool PosDefaultOpen = true,
    string Email = "",
    string Whatsapp = "",
    string Website = "",
    string Instagram = "",
    string CrNumber = "",
    double? Lat = null,
    double? Lng = null,
    Dictionary<string, string>? Names = null,
    int SetupStep = -1,
    Dictionary<string, string>? Addresses = null,
    // The shop's uploaded logo as a data URI; null means the emoji stands in.
    string? LogoData = null,
    // The shop delivers with its own people.
    bool SelfDelivery = false,
    // Every type the shop carries, main first. Null on old payloads.
    List<int>? CuisineIds = null,
    // The handle in short links: orderorange.com/<slug>. Empty = not chosen yet.
    string Slug = "",
    // POS: saving or closing an invoice also sends it straight to the printer.
    bool DirectPrint = false,
    // The store's real VAT registration; empty = not VAT-registered, nothing prints.
    string VatNumber = "",
    // Customers may book a table from the store's page online; the store confirms each.
    bool OnlineReservations = false,
    // What a booking costs: flat per table + per minute held. Both zero = free.
    decimal ReservePricePerTable = 0m,
    decimal ReservePricePerMinute = 0m,
    // The store's own mailbox — contracts go out from here. Host empty = platform mail.
    string SmtpHost = "", int SmtpPort = 587, string SmtpUser = "",
    string SmtpPassword = "", string SmtpFrom = "",
    // The owner's own wording on the printed table-QR cards. Empty = the default line.
    string QrCardText = "",
    // Dishes without their own photo borrow a stock picture; off = emoji instead.
    bool DishStockPhotos = true,
    // The language of everything the KITCHEN printer prints — product names, edit
    // labels, headings. "" = as the order was typed (the till's own setting applies).
    string KitchenLanguage = "",
    // What money is called on this shop's screens and printouts: "OMR", "﷼", "$".
    // Empty = the platform default.
    string Currency = "");

public record QrTextRequest(string? Text);

/// <summary>
/// One of a partner's businesses, as shown in the picker after login and the app-bar
/// switcher. A single email may own several stores; the session works in one at a time.
/// </summary>
public record StoreSummaryDto(
    int Id,
    string Name,
    string LogoEmoji,
    StoreType StoreType,
    string Area,
    bool IsApproved,
    bool IsOpen,
    // The shop's uploaded logo, so the switcher shows businesses by their real mark.
    string? LogoData = null);

/// <summary>
/// A partner opening ANOTHER business under the same account, from inside the portal.
/// It starts unapproved and closed — the admin decides when it may trade, exactly as if
/// it had applied from scratch.
/// </summary>
public record CreateMyStoreRequest(
    string Name,
    int CuisineId,
    StoreType StoreType = StoreType.Restaurant,
    string Area = "",
    double? Lat = null,
    double? Lng = null,
    Dictionary<string, string>? Names = null,
    // Every type the shop carries, main first. Null = just the CuisineId above.
    List<int>? CuisineIds = null);

public record UpdateRestaurantRequest(
    string Name,
    string Description,
    int CuisineId,
    string LogoEmoji,
    string BannerColor,
    string Area,
    string Street,
    string Phone,
    decimal DeliveryFee,
    decimal MinOrder,
    int AvgPrepMinutes,
    StoreType StoreType = StoreType.Restaurant,
    bool AllowsPickup = true,
    decimal TaxPercent = 5m,
    bool PosDefaultOpen = true,
    string Email = "",
    string Whatsapp = "",
    string Website = "",
    string Instagram = "",
    string CrNumber = "",
    double? Lat = null,
    double? Lng = null,
    Dictionary<string, string>? Names = null,
    Dictionary<string, string>? Addresses = null,
    // The shop delivers with its own people; platform riders never see these orders.
    bool SelfDelivery = false,
    // Every type the shop carries, main first. Null keeps the single CuisineId behaviour,
    // so an old client that never sends this changes nothing.
    List<int>? CuisineIds = null,
    // POS: saving or closing an invoice also sends it straight to the printer.
    bool DirectPrint = false,
    // The store's real VAT registration; empty = not VAT-registered, nothing prints.
    string VatNumber = "",
    // Customers may book a table from the store's page online; the store confirms each.
    bool OnlineReservations = false,
    // What a booking costs: flat per table + per minute held. Both zero = free.
    decimal ReservePricePerTable = 0m,
    decimal ReservePricePerMinute = 0m,
    // The store's own mailbox settings, as typed in Settings. Host empty = platform mail.
    string SmtpHost = "", int SmtpPort = 587, string SmtpUser = "",
    string SmtpPassword = "", string SmtpFrom = "",
    // Dishes without their own photo borrow a stock picture; off = emoji instead.
    bool DishStockPhotos = true,
    // The language of everything the KITCHEN printer prints — product names, edit
    // labels, headings. "" = as the order was typed (the till's own setting applies).
    string KitchenLanguage = "",
    // What money is called on this shop's screens and printouts: "OMR", "﷼", "$".
    // Empty = the platform default.
    string Currency = "");

/// <summary>A spot on the map, read from a pin or a pasted Google Maps link.</summary>
public record MapPointDto(double Lat, double Lng);
public record MapLinkRequest(string Url);

public record SaveCategoryRequest(string Name, int SortOrder, Dictionary<string, string>? Names = null);

/// <summary>
/// Photo semantics: null = keep the current photo, "" = remove it, data URL = replace it.
/// Photos (preferred): null = keep the current set, a list (max 4) = replace the whole set,
/// with MainPhoto as the index of the photo customers see first.
/// </summary>
public record SaveMenuItemRequest(
    int CategoryId,
    string Name,
    string Description,
    decimal Price,
    string ImageEmoji,
    bool IsPopular,
    bool IsAvailable,
    string? Photo = null,
    List<string>? Photos = null,
    int MainPhoto = 0,
    decimal DiscountPercent = 0,
    int? AvailableFromMinutes = null,
    int? AvailableToMinutes = null,
    string AvailableDays = "",
    int LeadTimeDays = 0,
    string Ingredients = "",
    string Unit = "",
    Dictionary<string, string>? Names = null,
    // The description in each language the partner wrote. Null leaves whatever is stored.
    Dictionary<string, string>? Descriptions = null,
    // Where the product stands inside its group: lowest first.
    int SortOrder = 0,
    bool InStoreOnly = false);

public record RestaurantPhotoDto(int Id, string Data, bool IsMain = false);
public record AddStorePhotoRequest(string Data);

public record DishPhotoDto(int Id, string Data, bool IsMain);

/// <summary>
/// Printed-receipt design. Font: mono|sans|serif|cairo · FontSize: small|normal|large ·
/// PaperWidth: 80|58 (mm) · Separator: dash|dots|stars|solid · TotalStyle: plain|invert ·
/// Spacing: compact|normal|relaxed · LabelStyle: en|bilingual.
/// </summary>
public record ReceiptDesignDto(
    string? Logo,
    string? HeaderMessage,
    string? FooterMessage,
    string? Promo,
    string Font = "mono",
    string FontSize = "normal",
    int PaperWidth = 80,
    bool ShowBarcode = true,
    bool ShowVat = true,
    bool ShowCourier = true,
    string Separator = "dash",
    string TotalStyle = "plain",
    string Spacing = "normal",
    string LabelStyle = "en",
    bool ShowQr = true,
    bool ShowAddress = true,
    string HeaderStyle = "normal",
    string ItemStyle = "lines",
    string Frame = "none",
    string Ink = "normal",
    string LogoSize = "normal",
    string Stamp = "none",
    int Copies = 1,
    string? QrLink = null,
    string QrMode = "verify",
    string? QrCaption = null,
    string QrSize = "normal")
{
    public static ReceiptDesignDto Default => new(null, null, null, null);
}

// ---------- Partner visit analytics (anonymous — counts only, never users) ----------
public record TrackStoreVisitRequest(int RestaurantId, int? ItemId = null, string? ItemName = null);
public record VisitBucketDto(string Label, int Count);
public record ProductViewsDto(string Name, int Views);
public record StoreVisitStatsDto(
    int StoreVisits,
    int ProductViews,
    List<VisitBucketDto> Buckets,
    List<ProductViewsDto> TopProducts,
    // The same length of time immediately before this one — a number means nothing
    // without something to compare it against.
    int PrevStoreVisits = 0,
    int PrevProductViews = 0);

// ---------- Raw materials: what the kitchen buys and burns through ----------

public record StoreMaterialDto(
    int Id, string Name, string Category, string Unit,
    decimal Quantity, decimal MinQuantity, decimal UnitCost,
    string? Supplier, string? Notes, DateTime? RestockedAt,
    string? Code = null, string? Barcode = null)
{
    /// <summary>The code labels print: the store's own, or M{Id} when none was given.</summary>
    public string LabelCode => string.IsNullOrWhiteSpace(Code) ? $"M{Id}" : Code!;

    /// <summary>What this much of the ingredient is worth on the shelf.</summary>
    public decimal Value => Quantity * UnitCost;

    /// <summary>Nothing left at all — the kitchen cannot cook with it today.</summary>
    public bool IsOut => Quantity <= 0;

    /// <summary>Still some, but under the level the owner set as "reorder now".</summary>
    public bool IsLow => !IsOut && MinQuantity > 0 && Quantity <= MinQuantity;
}

public record SaveStoreMaterialRequest(
    string Name, string Category, string Unit,
    decimal Quantity, decimal MinQuantity, decimal UnitCost,
    string? Supplier = null, string? Notes = null,
    string? Code = null, string? Barcode = null);

/// <summary>Add to or take from the shelf — a delivery arrived, or the kitchen used some.</summary>
public record AdjustStoreMaterialRequest(decimal Delta);

public record StoreMaterialsDto(
    List<StoreMaterialDto> Items,
    decimal TotalValue,
    int LowCount,
    int OutCount);

// ---------- The notification center: everything that wants the owner's eyes ----------

/// <summary>One thing to look at. Type: order | chat | stock | payment | reservation.</summary>
public record NotificationDto(
    string Type, string Title, string Body, DateTime At, string Nav,
    // urgent = act now · warn = act today · info = good to know
    string Severity);

public record NotificationsDto(int Badge, List<NotificationDto> Items);

// ---------- Stock alerts: the bell that says the rice is running out ----------

public record StockAlertDto(int Id, int MaterialId, string MaterialName, string Unit,
    decimal Quantity, string Type, DateTime CreatedAt, bool Seen);

public record StockAlertsDto(int UnseenCount, List<StockAlertDto> Alerts);

// ---------- Recipes: which materials one sold portion consumes ----------

public record RecipeLineDto(int MaterialId, string MaterialName, string Unit, decimal Amount, decimal UnitCost)
{
    /// <summary>What this line adds to the cost of one portion.</summary>
    public decimal Cost => Amount * UnitCost;
}

public record ProductRecipeDto(int MenuItemId, List<RecipeLineDto> Lines)
{
    public decimal PortionCost => Lines.Sum(l => l.Cost);
}

public record SaveRecipeLineRequest(int MaterialId, decimal Amount);
public record SaveRecipeRequest(List<SaveRecipeLineRequest> Lines);

/// <summary>All recipes of the store at once — the recipes page paints from one trip.</summary>
public record AllRecipesDto(Dictionary<int, ProductRecipeDto> ByMenuItem);

// ---------- Purchase invoices: the truck from the supplier ----------

public record PurchaseLineDto(int MaterialId, string MaterialName, string Unit, decimal Quantity, decimal UnitCost,
    bool IsProduct = false)
{
    public decimal Total => Quantity * UnitCost;
}

/// <summary>One promised payment: paid, waiting, or overdue.</summary>
public record PurchasePaymentDto(int Id, decimal Amount, DateTime DueDate, DateTime? PaidAt, string Method, string? Note)
{
    public bool IsPaid => PaidAt is not null;
    public bool IsOverdue => PaidAt is null && DueDate.Date < DateTime.Today;
}

public record MaterialPurchaseDto(
    int Id, string Number, string Supplier, DateTime InvoiceDate,
    decimal Total, bool IsPaid, string? Notes, List<PurchaseLineDto> Lines,
    List<PurchasePaymentDto> Payments)
{
    public decimal PaidAmount => Payments.Where(p => p.IsPaid).Sum(p => p.Amount);
    public decimal OwedAmount => Payments.Where(p => !p.IsPaid).Sum(p => p.Amount);
    public bool HasOverdue => Payments.Any(p => p.IsOverdue);
}

public record SavePurchaseLineRequest(int MaterialId, decimal Quantity, decimal UnitCost, bool IsProduct = false);

/// <summary>One planned installment the client asks for.</summary>
public record PlanInstallmentRequest(decimal Amount, DateTime DueDate);

public record SavePurchaseRequest(
    string Number, string Supplier, DateTime InvoiceDate, bool IsPaid,
    List<SavePurchaseLineRequest> Lines, string? Notes = null,
    // How the money moves: method always; DueDate when paying later;
    // Installments when the plan has several steps (overrides IsPaid/DueDate).
    string Method = "cash",
    DateTime? DueDate = null,
    List<PlanInstallmentRequest>? Installments = null);

public record MaterialPurchasePageDto(
    List<MaterialPurchaseDto> Items,
    decimal MonthTotal, decimal UnpaidTotal, int MonthCount, string? TopSupplier,
    decimal OverdueTotal = 0);

// ---------- Purchase templates: the weekly order, saved ----------

public record PurchaseTemplateLineDto(int ItemId, bool IsProduct, decimal Quantity);
public record PurchaseTemplateDto(int Id, string Name, string Supplier, List<PurchaseTemplateLineDto> Lines);
public record SavePurchaseTemplateRequest(string Name, string Supplier, List<PurchaseTemplateLineDto> Lines);

// ---------- Suppliers: who the store buys from ----------

public record SupplierDto(
    int Id, string Name, string? Phone, string? ContactName, string? Notes,
    // Live figures joined from the purchase book by name.
    decimal TotalSpend, decimal UnpaidTotal, int InvoiceCount, DateTime? LastPurchase);

public record SaveSupplierRequest(string Name, string? Phone = null, string? ContactName = null, string? Notes = null);

// ---------- Consumption: what selling the menu burned off the shelf ----------

public record ConsumptionProductDto(string ProductName, int Sold, decimal Amount, decimal Cost);

public record ConsumptionRowDto(
    int MaterialId, string MaterialName, string Unit, string Category,
    decimal Used, decimal Cost, decimal InStock,
    List<ConsumptionProductDto> Products)
{
    /// <summary>At this burn rate, how many more days the shelf lasts. Null = no usage.</summary>
    public double? DaysLeft(int periodDays) =>
        Used <= 0 ? null : (double)(InStock / (Used / periodDays));
}

public record ConsumptionReportDto(
    int PeriodDays, int OrdersCounted, decimal TotalCost,
    List<ConsumptionRowDto> Rows,
    // Products ranked by what they cost the kitchen in materials over the period.
    List<ConsumptionProductDto> TopProducts);

// ---------- Store bills: the owner's own running costs (utilities, rent, salaries…) ----------
public record StoreBillDto(
    int Id,
    string Category,
    string Title,
    string? Vendor,
    string? Reference,
    decimal Amount,
    DateTime DueDate,
    bool IsPaid,
    DateTime? PaidAt,
    bool Recurring,
    string? Notes)
{
    public bool IsOverdue => !IsPaid && DueDate.Date < DateTime.Today;
    public int DaysLeft => (DueDate.Date - DateTime.Today).Days;
}

public record SaveStoreBillRequest(
    string Category,
    string Title,
    string? Vendor,
    string? Reference,
    decimal Amount,
    DateTime DueDate,
    bool IsPaid,
    bool Recurring,
    string? Notes);

public record StoreBillPageDto(int Total, decimal TotalAmount, decimal UnpaidAmount, decimal OverdueAmount, List<StoreBillDto> Rows);

public record BillCategoryRowDto(string Category, int Count, decimal Total, decimal Paid, decimal Unpaid);
public record BillMonthRowDto(string Label, int Count, decimal Total, decimal Paid, decimal Unpaid);
public record BillVendorRowDto(string Vendor, int Count, decimal Total);
public record BillReportDto(
    decimal Total,
    decimal Paid,
    decimal Unpaid,
    decimal Overdue,
    int Count,
    List<BillCategoryRowDto> Categories,
    List<BillMonthRowDto> Months,
    List<BillVendorRowDto> Vendors);

// ---------- Staff & salaries ----------
public record StaffDto(
    int Id,
    string FullName,
    string Role,
    string? Phone,
    string? NationalId,
    string? Photo,
    decimal MonthlySalary,
    DateTime HiredOn,
    bool IsActive,
    string? Notes,
    bool PaidThisPeriod = false,
    // The team login this person clocks in with; null = attendance not linked yet.
    int? UserId = null,
    // Hours clocked in the period the caller asked about (payroll month), 0 when unlinked.
    double HoursWorked = 0,
    // Approved leave in that same month.
    double LeaveHours = 0,
    int LeaveDays = 0);

public record SaveStaffRequest(
    string FullName,
    string Role,
    string? Phone,
    string? NationalId,
    string? Photo,
    decimal MonthlySalary,
    DateTime HiredOn,
    bool IsActive,
    string? Notes,
    int? UserId = null);

public record StaffPageDto(int Total, int ActiveCount, decimal MonthlyPayroll, List<StaffDto> Rows);

public record PaySalaryRequest(int StaffId, int Year, int Month, decimal Bonus, decimal Deduction, string Method, string? Notes);

public record SalaryPaymentDto(
    int Id,
    int StaffId,
    string StaffName,
    string Role,
    int Year,
    int Month,
    decimal BaseSalary,
    decimal Bonus,
    decimal Deduction,
    decimal NetPaid,
    string Method,
    DateTime PaidAt,
    string? Notes);

/// <summary>The payroll run for one month: who is paid, who is still due.</summary>
public record PayrollDto(
    int Year,
    int Month,
    decimal Expected,
    decimal PaidTotal,
    decimal Remaining,
    int PaidCount,
    int DueCount,
    List<StaffDto> Due,
    List<SalaryPaymentDto> Paid);

public record SalaryMonthRowDto(string Label, int Year, int Month, int Payments, decimal Total);
public record SalaryStaffRowDto(int StaffId, string StaffName, string Role, int Payments, decimal Total);
public record SalaryReportDto(
    decimal Total,
    decimal Bonuses,
    decimal Deductions,
    int Payments,
    List<SalaryMonthRowDto> Months,
    List<SalaryStaffRowDto> Staff,
    List<SalaryPaymentDto> Recent);

// ---------- Partner reports: three separate, server-paged views over the store's orders ----------
public record MoneyBucketDto(string Label, decimal Amount, int Orders = 0);
public record SalesSummaryDto(int Orders, decimal Revenue, decimal AvgOrder, int Cancelled, List<MoneyBucketDto> Buckets);
public record ProductSalesDto(string Name, int Qty, decimal Revenue);
public record ProductSalesPageDto(int Total, List<ProductSalesDto> Rows);
public record CustomerOrderRowDto(string Number, DateTime At, OrderStatus Status, decimal Amount);
public record CustomerSalesDto(string Name, int Orders, decimal Total, List<CustomerOrderRowDto>? Rows = null);
public record CustomerSalesPageDto(int Total, List<CustomerSalesDto> Rows);


// ---------- Product moderation (admin review queue) ----------

/// <summary>One product awaiting an administrator's decision.</summary>
public record PendingProductDto(
    int Id,
    int RestaurantId,
    string RestaurantName,
    string CategoryName,
    string Name,
    string Description,
    decimal Price,
    string ImageEmoji,
    string? Photo,
    DateTime SubmittedAt);

public record PendingProductPageDto(List<PendingProductDto> Items, int Total);

public record RejectProductRequest(string? Reason);

/// <summary>One product's kitchen-ticket destination on the shop's till ("kitchen"/"bar").</summary>
public record PrintRouteDto(int MenuItemId, string Station);

/// <summary>An amount the web POS asks the till (LocalHandler) to charge on the card terminal.</summary>
public record TillChargeDto(decimal Amount, string? Reference, DateTime At);

public record SendTillChargeRequest(decimal Amount, string? Reference);

/// <summary>The owner claims a short-link handle; empty clears it.</summary>
public record SetSlugRequest(string? Slug);

/// <summary>Answer to "is this handle free?" — reason is "invalid", "reserved" or "taken".</summary>
public record SlugCheckDto(bool Available, string Reason);

/// <summary>What a handle resolves to.</summary>
public record SlugResolveDto(int Id);

// ---------- Web Push: orders ring the partner's device even with the portal closed ----------
public record PushVapidDto(string PublicKey);
public record PushSubscribeRequest(string Endpoint, string P256dh, string Auth);

// ---------- Contracts: standing supply agreements the store writes ----------

/// <summary>One line of goods on a contract: what, how many per delivery, at what price.</summary>
public record ContractLineDto(string Name, int Quantity, decimal UnitPrice);

/// <summary>
/// A standing agreement — "10 trays, 10 deliveries a month, 45 OMR/month" — kept on the
/// store's file and emailable to the customer as a document wearing the store's own
/// logo and details. Months 0 = open-ended.
/// </summary>
public record ContractDto(
    int Id, string CustomerName, string CustomerEmail, string CustomerPhone,
    List<ContractLineDto> Lines, int TimesPerMonth, int Months, decimal MonthlyPrice,
    DateTime StartDate, string? Note, DateTime? SentAt, DateTime CreatedAt,
    // The public page for this contract — the same link the email carries.
    string PublicUrl = "",
    // The letter as a file, straight from a link. Empty = no public API base set.
    string PdfUrl = "",
    // The customer's yes, from the public page; and whatever they wrote back.
    DateTime? AcceptedAt = null, string AcceptedBy = "",
    List<ContractReplyDto>? Replies = null);

/// <summary>One thing the customer wrote back from the public page.</summary>
public record ContractReplyDto(string Text, DateTime At);

public record AcceptContractRequest(string? Name = null);
public record ReplyContractRequest(string Text);

public record SaveContractRequest(
    string CustomerName, string CustomerEmail, string CustomerPhone,
    List<ContractLineDto> Lines, int TimesPerMonth, int Months, decimal MonthlyPrice,
    DateTime StartDate, string? Note = null);

/// <summary>
/// A contract as its public page shows it — opened from the emailed link by whoever
/// holds it, no account anywhere. Carries the store's face and details because the
/// page IS the letter.
/// </summary>
public record PublicContractDto(
    string StoreName, Dictionary<string, string>? StoreNames, string? StoreLogo, string StoreEmoji,
    string StoreArea, string StoreStreet, string StorePhone, string StoreEmail,
    string CrNumber, string VatNumber, string StoreUrl,
    string CustomerName, List<ContractLineDto> Lines,
    int TimesPerMonth, int Months, decimal MonthlyPrice,
    DateTime StartDate, string? Note, DateTime CreatedAt,
    // The letter as a file, straight from a link. Empty = no public API base set.
    string PdfUrl = "",
    // Set once the customer accepted from this very page.
    DateTime? AcceptedAt = null, string AcceptedBy = "",
    // What the customer already wrote back, oldest first.
    List<ContractReplyDto>? Replies = null);

/// <summary>
/// The customer's counter-offer from the public page: extra products and/or a new
/// term. Only for a contract not yet accepted — a handshake is not renegotiated.
/// </summary>
public record ProposeContractRequest(List<ContractLineDto>? AddLines = null, int? Months = null);
