using System.IO;
using System.Text.Json;

namespace LocalHandler.Models;

/// <summary>What a print station is FOR. The role decides what gets printed on it.</summary>
public enum StationRole
{
    /// <summary>The customer's bill: every line, the totals, the shop's header.</summary>
    Cashier = 0,

    /// <summary>A work ticket: only the food this station cooks, no prices.</summary>
    Kitchen = 1,

    /// <summary>Same as a kitchen ticket, for drinks.</summary>
    Bar = 2
}

/// <summary>One physical printer with a job to do.</summary>
public sealed class Station
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string PrinterName { get; set; } = "";
    public StationRole Role { get; set; } = StationRole.Kitchen;

    /// <summary>Receipt width in characters — 32 for 58mm paper, 42 for 80mm.</summary>
    public int Columns { get; set; } = 42;

    /// <summary>How many copies of each ticket this station prints.</summary>
    public int Copies { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>What the routing dropdowns show: "k1 · Kitchen" — the name alone means nothing to a new user.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? Role.ToString() : $"{Name} · {Role}";

    public override string ToString() => $"{Name} · {PrinterName}";
}

/// <summary>
/// Everything this till remembers between runs: who it signs in as, which printers
/// it drives, and which product goes to which of them.
/// </summary>
public sealed class Setup
{
    public string ApiBaseUrl { get; set; } = "https://orderorange.com:8500/";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string StoreName { get; set; } = "";

    /// <summary>The customer site a printed receipt's QR points at (verify/store links).</summary>
    public string ClientUrl { get; set; } = "https://orderorange.com";

    /// <summary>
    /// The shared <c>Bill:Secret</c> the server signs bill-verification codes with. Leave
    /// empty to use the platform default; set it only if this deployment overrode it, or
    /// the receipt's "verify" QR will not validate.
    /// </summary>
    public string? BillSecret { get; set; }

    /// <summary>Printers this machine drives.</summary>
    public List<Station> Stations { get; set; } = [];

    /// <summary>Product id → station id. A product with no entry falls back to its category.</summary>
    public Dictionary<int, string> ProductStation { get; set; } = [];

    /// <summary>Category id → station id, the rule most shops actually want.</summary>
    public Dictionary<int, string> CategoryStation { get; set; } = [];

    /// <summary>Print the customer's bill automatically as each new order arrives.</summary>
    public bool AutoPrintReceipt { get; set; } = true;

    /// <summary>Send kitchen tickets automatically too.</summary>
    public bool AutoPrintKitchen { get; set; } = true;

    /// <summary>Registered in the user's Windows Run list, so the till is up as soon as the PC is.</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>How often to ask the server for new orders.</summary>
    public int PollSeconds { get; set; } = 15;

    /// <summary>
    /// Which language the KITCHEN/BAR tickets print product names (and descriptions) in.
    /// "" keeps the name exactly as the order carries it. A cook who reads Farsi can have
    /// the pass tickets in Farsi while the customer's bill stays as designed.
    /// </summary>
    public string KitchenLanguage { get; set; } = "";

    /// <summary>
    /// Store id → orders this till has already printed. Remembered on disk so a restart
    /// never reprints the board; only the newest few hundred per store are kept.
    /// </summary>
    public Dictionary<int, List<int>> PrintedOrders { get; set; } = [];

    /// <summary>Store id → orders whose CASHIER BILL this till has already printed.</summary>
    public Dictionary<int, List<int>> ReceiptedOrders { get; set; } = [];

    /// <summary>Store id → open-table line ids already sent to the kitchen (so a round prints once).</summary>
    public Dictionary<int, List<int>> TicketedTabLines { get; set; } = [];

    /// <summary>Store id → line id → what the kitchen was last told about that open-invoice
    /// line, so an edit prints as "1 → 3" and a struck line as "5 → 0".</summary>
    public Dictionary<int, Dictionary<int, TabLineMemo>> TabLineMemos { get; set; } = [];

    public Dictionary<int, TabLineMemo> TabMemosFor(int restaurantId) =>
        TabLineMemos.TryGetValue(restaurantId, out var m) ? new Dictionary<int, TabLineMemo>(m) : [];

    public void ReplaceTabMemos(int restaurantId, IReadOnlyDictionary<int, TabLineMemo> memos) =>
        TabLineMemos[restaurantId] = new Dictionary<int, TabLineMemo>(memos);

    public HashSet<int> TabLinesFor(int restaurantId) =>
        TicketedTabLines.TryGetValue(restaurantId, out var ids) ? [.. ids] : [];

    public void RememberTabLines(int restaurantId, IEnumerable<int> lineIds) =>
        Remember(TicketedTabLines, restaurantId, lineIds);

    public HashSet<int> PrintedFor(int restaurantId) =>
        PrintedOrders.TryGetValue(restaurantId, out var ids) ? [.. ids] : [];

    public HashSet<int> ReceiptedFor(int restaurantId) =>
        ReceiptedOrders.TryGetValue(restaurantId, out var ids) ? [.. ids] : [];

    public void RememberPrinted(int restaurantId, IEnumerable<int> orderIds) =>
        Remember(PrintedOrders, restaurantId, orderIds);

    public void RememberReceipted(int restaurantId, IEnumerable<int> orderIds) =>
        Remember(ReceiptedOrders, restaurantId, orderIds);

    private static void Remember(Dictionary<int, List<int>> book, int restaurantId, IEnumerable<int> orderIds)
    {
        var list = book.GetValueOrDefault(restaurantId) ?? [];
        list.AddRange(orderIds.Where(id => !list.Contains(id)));
        if (list.Count > 400) list.RemoveRange(0, list.Count - 400);
        book[restaurantId] = list;
    }

    // ---------- Where it lives ----------
    //
    // One PC can run one window per partner: each window is a PROFILE with its own
    // settings file (login, printers, routing), so two shops on the same counter never
    // overwrite each other. The default profile keeps the historic file name.

    /// <summary>Which profile this setup belongs to; "" is the default window.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Profile { get; private set; } = "";

    private static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrderOrange");

    private static string FileFor(string profile) =>
        Path.Combine(Folder, string.IsNullOrWhiteSpace(profile) ? "localhandler.json" : $"localhandler-{Clean(profile)}.json");

    private static string Clean(string profile) =>
        new(profile.Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Setup Load(string profile = "")
    {
        profile = Clean(profile);
        Setup setup = new();
        try
        {
            var path = FileFor(profile);
            if (File.Exists(path))
                setup = JsonSerializer.Deserialize<Setup>(File.ReadAllText(path), Json) ?? new Setup();
        }
        catch { /* a corrupt file must never stop the till from opening */ }
        setup.Profile = profile;
        setup.MigrateSingleRules();
        return setup;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FileFor(Profile), JsonSerializer.Serialize(this, Json));
        }
        catch { /* read-only profile: the till still works, it just forgets */ }
    }

    /// <summary>The first "partner2", "partner3"… name that has no settings file yet.</summary>
    public static string NextFreeProfile()
    {
        for (var i = 2; i < 100; i++)
            if (!File.Exists(FileFor($"partner{i}"))) return $"partner{i}";
        return $"partner{DateTime.Now:HHmmss}";
    }

    /// <summary>
    /// Which printer this product belongs to: its own rule first, then its category's,
    /// then the first kitchen station — so a new dish is never silently unprinted.
    /// </summary>
    public Station? StationFor(int productId, int categoryId) => StationsFor(productId, categoryId).FirstOrDefault();

    // ---------- Many printers per product ----------
    // A dish can print on several stations at once (the grill AND the bar for a combo).
    // These supersede ProductStation/CategoryStation, which are kept only to read old files.

    /// <summary>Product id → every station it prints at.</summary>
    public Dictionary<int, List<string>> ProductStations { get; set; } = [];

    /// <summary>Category id → the stations every product of it prints at, unless overridden.</summary>
    public Dictionary<int, List<string>> CategoryStations { get; set; } = [];

    /// <summary>Old single-printer rules folded into the multi-printer ones, once.</summary>
    private void MigrateSingleRules()
    {
        if (ProductStations.Count == 0)
            foreach (var (id, st) in ProductStation) ProductStations[id] = [st];
        if (CategoryStations.Count == 0)
            foreach (var (id, st) in CategoryStation) CategoryStations[id] = [st];
    }

    /// <summary>
    /// Every printer this product goes to: its own rule first, then its category's,
    /// then the first kitchen station — so a new dish is never silently unprinted.
    /// </summary>
    public List<Station> StationsFor(int productId, int categoryId)
    {
        List<Station> Live(List<string> ids) =>
            ids.Select(id => Stations.FirstOrDefault(s => s.Id == id && s.Enabled)).Where(s => s is not null).ToList()!;

        if (ProductStations.TryGetValue(productId, out var byProduct) && Live(byProduct) is { Count: > 0 } p) return p;
        if (CategoryStations.TryGetValue(categoryId, out var byCategory) && Live(byCategory) is { Count: > 0 } c) return c;
        var first = Stations.FirstOrDefault(s => s.Enabled && s.Role is StationRole.Kitchen or StationRole.Bar);
        return first is null ? [] : [first];
    }

    public Station? Cashier => Stations.FirstOrDefault(s => s.Enabled && s.Role == StationRole.Cashier);
}
