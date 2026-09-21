namespace OrderOrange.Shared;

/// <summary>
/// One thing thrown away. Kept as a record of its own rather than a silent stock
/// adjustment, because the question a shop needs answered is never "how much is left"
/// but "where did it go, and can we stop it going there again".
/// </summary>
/// <param name="Kind">"material" for something off the shelf, "product" for a finished dish.</param>
/// <param name="Reason">One of <see cref="WasteReasons.All"/>.</param>
public record WasteEntryDto(
    int Id,
    string Kind,
    int RefId,
    string Name,
    decimal Quantity,
    string Unit,
    decimal UnitCost,
    decimal Cost,
    string Reason,
    string? Notes,
    string By,
    DateTime At);

/// <summary>What to write off, and why.</summary>
public record RecordWasteRequest(
    string Kind,
    int RefId,
    decimal Quantity,
    string Reason,
    string? Notes = null);

/// <summary>A reason with what it cost over the period — the bars on the page.</summary>
public record WasteReasonTotalDto(string Reason, decimal Cost, int Count);

/// <summary>An item with what it cost over the period.</summary>
public record WasteItemTotalDto(string Kind, int RefId, string Name, string Unit, decimal Quantity, decimal Cost);

/// <summary>Everything the page needs in one call.</summary>
/// <param name="ShareOfStock">Waste cost as a percentage of what the shelf is worth; 0 when the shelf is empty.</param>
public record WasteBoardDto(
    List<WasteEntryDto> Entries,
    List<WasteReasonTotalDto> ByReason,
    List<WasteItemTotalDto> TopItems,
    decimal TotalCost,
    int TotalEntries,
    decimal ShareOfStock,
    int Days);

/// <summary>
/// The fixed set of reasons. Fixed on purpose: free text cannot be counted, and a shop
/// that cannot count its waste by cause cannot do anything about it.
/// </summary>
public static class WasteReasons
{
    public const string Spoiled = "spoiled";
    public const string Expired = "expired";
    public const string Broken = "broken";
    public const string Overproduced = "overproduced";
    public const string StaffMeal = "staff";
    public const string Complaint = "complaint";
    public const string Training = "training";
    public const string Other = "other";

    public static readonly string[] All =
        [Spoiled, Expired, Broken, Overproduced, StaffMeal, Complaint, Training, Other];

    public static bool IsKnown(string? reason) => reason is not null && All.Contains(reason);
}
