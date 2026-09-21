namespace OrderOrange.Shared;

/// <summary>A place stock is kept: the dry store, the cold room, the bar, a second branch.</summary>
/// <param name="Icon">Which picture the node wears on the map — see <see cref="WarehouseIcons"/>.</param>
/// <param name="X">Where it sits on the map, as a percentage of the canvas width.</param>
/// <param name="Y">Where it sits on the map, as a percentage of the canvas height.</param>
/// <param name="Links">The places this one sends stock to: the lines on the map.</param>
public record WarehouseDto(
    int Id,
    string Name,
    string Kind,
    string? Notes,
    bool IsDefault,
    int ItemCount,
    decimal Value,
    string Icon = "warehouse",
    double X = 20,
    double Y = 30,
    List<int>? Links = null,
    bool IsCentral = false);

/// <summary>The one place an owner's branches all draw from. Lives in one store, seen by every store of that owner.</summary>
/// <param name="IsMine">True when the central place belongs to the store that is asking.</param>
public record CentralDto(int WarehouseId, int StoreId, string StoreName, string Name, bool IsMine, int ItemCount, decimal Value);

/// <summary>Another store of the same owner: somewhere the central warehouse can send to.</summary>
public record BranchDto(int StoreId, string Name);

/// <summary>Stock leaving the central warehouse for a branch. Lines name the CENTRAL store's materials.</summary>
public record CentralTransferRequest(int ToStoreId, List<TransferLineRequest> Lines, string? Note = null);

/// <summary>One material's balance inside one warehouse.</summary>
public record WarehouseStockDto(
    int MaterialId,
    string Name,
    string Unit,
    decimal Quantity,
    decimal UnitCost,
    decimal Value);

/// <summary>
/// What the whole shelf looks like once it is split by place.
/// </summary>
/// <param name="Unassigned">
/// Stock the shop owns but has not put anywhere yet: the total on the materials page minus
/// what the warehouses hold. Buying, selling and writing off all move the total without
/// naming a place, so this is where the difference shows rather than being hidden.
/// </param>
public record WarehouseBoardDto(
    List<WarehouseDto> Warehouses,
    List<WarehouseStockDto> Unassigned,
    decimal UnassignedValue,
    decimal TotalValue,
    int StoreId = 0,
    CentralDto? Central = null,
    List<BranchDto>? Branches = null);

public record SaveWarehouseRequest(string Name, string Kind, string? Notes = null, bool MakeDefault = false,
    string Icon = "warehouse", bool MakeCentral = false);

/// <summary>Where a node was dropped on the map.</summary>
public record MoveNodeRequest(double X, double Y);

/// <summary>The pictures a place can wear. A fixed set, drawn by the app, so they read the same everywhere.</summary>
public static class WarehouseIcons
{
    public static readonly string[] All =
        ["factory", "truck", "warehouse", "store", "restaurant", "kitchen", "cold", "freezer", "bar", "customers"];
    public static bool IsKnown(string? icon) => icon is not null && All.Contains(icon);
}

/// <summary>One line of a move.</summary>
public record TransferLineRequest(int MaterialId, decimal Quantity);

/// <summary>
/// Moving stock from one place to another, or out of the unassigned pool into a place.
/// </summary>
/// <param name="FromId">Null means "from stock that is not in a warehouse yet".</param>
public record TransferRequest(int? FromId, int ToId, List<TransferLineRequest> Lines, string? Note = null);

public record TransferLineDto(int MaterialId, string Name, string Unit, decimal Quantity, decimal UnitCost, decimal Value);

public record TransferDto(
    int Id,
    int? FromId,
    string FromName,
    int ToId,
    string ToName,
    List<TransferLineDto> Lines,
    decimal TotalValue,
    string? Note,
    string By,
    DateTime At);

/// <summary>The kinds a warehouse can be. Fixed so the icons and the reports can rely on them.</summary>
public static class WarehouseKinds
{
    public const string Main = "main";
    public const string Dry = "dry";
    public const string Cold = "cold";
    public const string Freezer = "freezer";
    public const string Kitchen = "kitchen";
    public const string Bar = "bar";
    public const string Branch = "branch";

    public static readonly string[] All = [Main, Dry, Cold, Freezer, Kitchen, Bar, Branch];
    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}
