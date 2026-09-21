namespace OrderOrange.Shared;

/// <summary>
/// A customer in the store's own book — someone who phones in or walks up, with no app
/// and no account of their own. The store owns this record; the customer never sees it.
/// </summary>
public record StoreCustomerDto(
    int Id,
    string Name,
    string Phone,
    string Address,
    string? Notes,
    int OrderCount,
    DateTime? LastOrderAt,
    DateTime CreatedAt,
    double? Lat = null,
    double? Lng = null);

public record SaveStoreCustomerRequest(
    string Name,
    string Phone,
    string Address,
    string? Notes = null,
    double? Lat = null,
    double? Lng = null);

/// <summary>
/// An order the store places on someone's behalf. No address id: the counter staff type
/// the address, or take the one already on the customer's record. TableId marks a
/// dine-in order — the food goes to the table, not out the door.
/// </summary>
/// <summary>
/// StoreCustomerId 0 = an anonymous walk-up: the sale settles under the store's
/// Walk-in book entry. CounterSale = sold across the counter, nothing delivered.
/// </summary>
public record PlaceCounterOrderRequest(
    int StoreCustomerId,
    PaymentMethod PaymentMethod,
    List<PlaceOrderItem> Items,
    string? Notes = null,
    string? AddressOverride = null,
    bool MarkPaid = false,
    int? TableId = null,
    bool CounterSale = false,
    // A sale being booked after the fact ("yesterday's phone order I forgot to type in"):
    // the day it really happened. Null = now. The API accepts up to 31 days back, never the future.
    DateTime? PlacedAt = null);

// ---------- The dining room ----------

/// <summary>A table on the shop's floor, with whoever is sitting at it right now.</summary>
public record StoreTableDto(
    int Id,
    string Name,
    string Type,
    int Seats,
    int? StoreCustomerId,
    string? CustomerName,
    string? GuestName,
    DateTime? OccupiedAt,
    string Floor = "",
    double X = 50,
    double Y = 50,
    string Shape = "square",
    double W = 0,
    double H = 0,
    int? RoomId = null)
{
    public bool IsOccupied => StoreCustomerId is not null || GuestName is not null;
}

public record SaveStoreTableRequest(string Name, string Type, int Seats, string Floor = "", string Shape = "square",
    int? RoomId = null);

/// <summary>A table was dragged somewhere new on the plan. Coordinates are canvas percentages.</summary>
public record MoveTableRequest(double X, double Y, string? Floor = null, double? W = null, double? H = null);

/// <summary>Rename a floor — every table standing on it moves to the new name.</summary>
public record RenameFloorRequest(string From, string To);

// ---------- Rooms drawn on the plan ----------

/// <summary>A zone sketched on the floor plan: "Salon", "Outside", "Family room"…</summary>
public record StoreRoomDto(int Id, string Name, string Floor, double X, double Y, double W, double H, int SortOrder = 0);

public record SaveStoreRoomRequest(string Name, string Floor, double X, double Y, double W, double H);

/// <summary>A room was dragged or resized. Percentages of the canvas, top-left anchored.</summary>
public record RoomRectRequest(double X, double Y, double W, double H);

// ---------- Open invoices (tabs) ----------

/// <summary>One line on an open tab, priced when it was added.</summary>
public record TabLineDto(int Id, int MenuItemId, string Name, decimal UnitPrice, int Quantity, string? Notes);

/// <summary>
/// The open invoice on a table. Totals are computed server-side so the panel shows
/// exactly what the final bill will say: subtotal + service fee + VAT.
/// </summary>
public record StoreTabDto(
    int Id,
    int TableId,
    string TableName,
    int? StoreCustomerId,
    string? CustomerName,
    string? GuestName,
    DateTime OpenedAt,
    List<TabLineDto> Lines,
    decimal Subtotal,
    decimal ServiceFee,
    decimal TaxPercent,
    decimal TaxAmount,
    decimal Total);

/// <summary>Seat a party AND open their invoice in one move. Book customer or walk-in.</summary>
public record OpenTabRequest(int TableId, int? StoreCustomerId = null, string? GuestName = null);

/// <summary>
/// One save of the open invoice: edits to lines already on it (quantity, note; 0 strikes
/// the line) together with the new items. One request on purpose — the till polls the
/// invoice and prints whatever changed, so the changes must land in a single step or
/// the kitchen gets two tickets for one save.
/// </summary>
public record AddTabLinesRequest(List<PlaceOrderItem> Items, List<TabLineEditRequest>? Edits = null);

public record TabLineEditRequest(int LineId, int Quantity, string? Notes = null);

/// <summary>The bill is called: settle the tab into a real order and free the table.</summary>
public record CloseTabRequest(PaymentMethod PaymentMethod, bool MarkPaid = true, string? Notes = null);

/// <summary>Nudge one line's quantity on an open tab. 0 strikes the line entirely.</summary>
public record SetTabLineQtyRequest(int Quantity, string? Notes = null);   // Notes null = leave as is

// ---------- Table QR reservations ----------

/// <summary>
/// What the public reservation page shows: the store, the table — and the menu, so
/// whoever scanned the QR can browse the food before (or instead of) reserving.
/// </summary>
public record TableReserveInfoDto(
    string StoreName, string LogoEmoji, string Area, string TableName, int Seats,
    List<MenuCategoryDto>? Menu = null,
    Dictionary<string, string>? StoreNames = null,
    // The shop's real logo as a data URI. Null = fall back to the emoji.
    string? LogoData = null,
    // Dishes without their own photo borrow a stock picture; off = emoji instead.
    bool DishStockPhotos = true,
    // Contacts for the header: tap-to-call and the way to the door.
    string Phone = "",
    double? MapLat = null,
    double? MapLng = null,
    // Which store the table belongs to — the survey link needs it.
    int StoreId = 0)
{
    /// <summary>The store's name in the guest's language, when the owner provided it.</summary>
    public string NameFor(string locale) =>
        PickName(locale) ?? PickName("ar") ?? StoreName;

    private string? PickName(string locale) =>
        StoreNames is not null && StoreNames.TryGetValue(locale, out var name) && name.Length > 0 ? name : null;
}

/// <summary>A reservation typed by whoever scanned the QR — no account behind it.
/// TableId (online bookings only): the table the guest picked from the floor; 0 = any.
/// TableIds: a party big enough to need two or three tables at once; TableId is the first.</summary>
public record PublicReserveRequest(string Name, string Phone, int Guests, DateTime At, string? Note = null, int TableId = 0,
    // How long the guest expects to keep the table.
    int DurationMinutes = 90,
    List<int>? TableIds = null);

/// <summary>The store's floor as the booking page shows it: salons with their tables.</summary>
public record PublicSalonDto(int Id, string Name, List<PublicTableDto> Tables);
public record PublicTableDto(int Id, string Name, string Type, int Seats, string Shape);

/// <summary>A reservation the OWNER books from inside the store — phone call, walk-up, or the assistant.</summary>
public record OwnerReserveRequest(int TableId, string? Name, int Guests, DateTime At, string? Note = null);

public record ReservationDto(
    int Id, int TableId, string TableName, string Name, string Phone,
    int Guests, DateTime At, string? Note, string Status, DateTime CreatedAt,
    int DurationMinutes = 90,
    // Every table the party holds, comma-separated. Empty = just TableId.
    string TableIds = "",
    // What the house quoted for the booking, frozen when it was made. 0 = free.
    decimal Price = 0m)
{
    /// <summary>When the table frees up again, by the guest's own estimate.</summary>
    public DateTime Until => At.AddMinutes(DurationMinutes <= 0 ? 90 : DurationMinutes);

    /// <summary>How many tables the party booked — 1 unless they took a row of them.</summary>
    public int TableCount => TableIds.Length == 0 ? 1 : TableIds.Split(',').Length;
}

public record SetReservationStatusRequest(string Status);

/// <summary>A signed-in customer's own booking, with the store it belongs to.</summary>
public record MyReservationDto(
    int Id, int RestaurantId, string StoreName, string StoreEmoji,
    Dictionary<string, string>? StoreNames, string TableName,
    int Guests, DateTime At, string? Note, string Status,
    int DurationMinutes = 90,
    decimal Price = 0m)
{
    public DateTime Until => At.AddMinutes(DurationMinutes <= 0 ? 90 : DurationMinutes);
}

/// <summary>What a guest at the table sends from the QR page — items, nothing else.</summary>
public record PublicTableOrderRequest(List<PlaceOrderItem> Items, string? GuestName = null);

/// <summary>What they get back: proof the kitchen has it, and the table's running bill.</summary>
public record PublicOrderResultDto(int TabId, string TableName, int LineCount, decimal Subtotal, decimal Total,
    // A ready PDF of the running invoice — the guest's copy, one tap after ordering.
    string? InvoicePdfUrl = null);

/// <summary>The table's running invoice, trimmed for the GUEST's eyes: the shop,
/// the table, the lines and the money — no staff, no other tables, no customer file.</summary>
public record PublicTabInvoiceDto(
    string StoreName,
    Dictionary<string, string>? StoreNames,
    string? LogoData,
    string TableName,
    DateTime OpenedAt,
    List<PublicTabLineDto> Lines,
    decimal Subtotal,
    decimal TaxPercent,
    decimal Tax,
    decimal Total,
    // Everything the POS receipt prints, so the guest's copy IS the store's slip.
    string Area = "",
    string Street = "",
    string Phone = "",
    string CrNumber = "",
    string VatNumber = "",
    ReceiptDesignDto? Design = null,
    // A ready QR (data URI) pointing back to this table's menu page.
    string? QrDataUrl = null)
{
    public string NameFor(string locale) =>
        StoreNames is not null && StoreNames.TryGetValue(locale, out var n) && n.Length > 0 ? n : StoreName;
}

public record PublicTabLineDto(string Name, int Quantity, decimal UnitPrice);

// ---------- Table chat: the guest's phone ↔ the store ----------

public record TableChatMessageDto(
    int Id, string From, string Text, DateTime At,
    string? Audio = null, string? Image = null, bool Deleted = false, DateTime? EditedAt = null,
    string? ReplyPreview = null, string? File = null, string? FileName = null);
public record SendTableChatRequest(string Text, string? Audio = null, string? Image = null, int? ReplyToId = null, string? File = null, string? FileName = null);
public record EditTableChatRequest(string Text);
public record MarkChatReadRequest(int LastId);

/// <summary>
/// One poll answers everything: new messages, changes to old ones (edits/deletes),
/// a stamp to pass back next time, and how far the OTHER side has read.
/// </summary>
public record TableChatSyncDto(
    List<TableChatMessageDto> New,
    List<TableChatMessageDto> Changed,
    long Stamp,
    int OtherReadId,
    DateTime? OtherReadAt);
// ---------- Team chat: direct messages between a business's teammates ----------

public record CommunityChatMessageDto(
    int Id, int UserId, string UserName, string Text, DateTime At,
    string? Audio = null, string? Image = null, bool Deleted = false, DateTime? EditedAt = null,
    string? ReplyPreview = null, string? File = null, string? FileName = null);
public record CommunityChatSyncDto(
    List<CommunityChatMessageDto> New,
    List<CommunityChatMessageDto> Changed,
    long Stamp);

/// <summary>One teammate you can message, with a preview of your last exchange.</summary>
public record TeamContactDto(
    int UserId, string Name, string Role, bool IsOwner,
    string LastText, string LastFrom, DateTime? LastAt, int Unread);

public record TableChatThreadDto(int TableId, string TableName, string LastText, string LastFrom, DateTime LastAt, int Unread = 0);
public record TableChatUnreadDto(int Count, int MaxId);

/// <summary>One archived chat session — a table's conversation up to the moment its invoice closed.</summary>
public record ChatArchiveSessionDto(string Id, int TableId, string TableName, string? InvoiceNumber, DateTime ClosedAt, int Count);

/// <summary>One table's printable QR: the code, and the URL the QR encodes.</summary>
public record TableQrDto(int TableId, string Name, string Floor, string Code, string Url);

/// <summary>A signed table code on its own — what the printed number-cards resolve to.</summary>
public record TableCodeDto(string Code);

/// <summary>Seat someone: a book customer by id, or a walk-in by name. One or the other.</summary>
public record SeatTableRequest(int? StoreCustomerId = null, string? GuestName = null);
