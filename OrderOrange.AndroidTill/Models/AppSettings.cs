using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Storage;

namespace OrderOrange.AndroidTill.Models;

/// <summary>How the app reaches a printer.</summary>
public enum PrinterConnection
{
    /// <summary>The terminal's own thermal printer, reached by whatever route works (USB, loopback TCP, serial).</summary>
    BuiltIn = 0,
    /// <summary>An ESC/POS printer on the shop Wi-Fi: raw TCP, normally port 9100.</summary>
    WiFi = 1,
    /// <summary>A printer plugged into the terminal's USB host port.</summary>
    Usb = 2,
    /// <summary>A serial device node such as /dev/ttyS1 (some terminals expose the built-in printer this way).</summary>
    Serial = 3,
    /// <summary>A paired Bluetooth printer (SPP) — including the terminal's own "InnerPrinter".</summary>
    Bluetooth = 4,
}

/// <summary>
/// Which ESC/POS cut command the printer understands. Most take the standard one; a printer
/// that feeds but never cuts wants one of the others (Majed 2026-10-18: "the printer not cut
/// the page").
/// </summary>
public enum CutMode
{
    /// <summary>GS V 66 0 — partial cut after feeding to the cutter (the common one).</summary>
    Partial = 0,
    /// <summary>GS V 65 0 — full cut after feeding to the cutter.</summary>
    Full = 1,
    /// <summary>GS V 1 — partial cut, older "function A" form.</summary>
    PartialA = 2,
    /// <summary>GS V 0 — full cut, older "function A" form.</summary>
    FullA = 3,
    /// <summary>ESC i — full cut on Epson-compatible firmware.</summary>
    EscI = 4,
    /// <summary>ESC m — partial cut on Epson-compatible firmware.</summary>
    EscM = 5,
}

/// <summary>What a printer is FOR. The role decides what gets printed on it.</summary>
public enum PrinterRole
{
    /// <summary>The customer's bill: every line, the totals, the shop's header.</summary>
    Cashier = 0,
    /// <summary>A work ticket: only the food this station cooks, no prices.</summary>
    Kitchen = 1,
    /// <summary>Same as a kitchen ticket, for drinks.</summary>
    Bar = 2,
}

/// <summary>One physical printer with a job to do.</summary>
public sealed class PrinterConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public PrinterConnection Connection { get; set; } = PrinterConnection.WiFi;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 9100;
    public string SerialPath { get; set; } = "";
    public int UsbVendorId { get; set; }
    public int UsbProductId { get; set; }
    /// <summary>MAC of the paired Bluetooth printer, and its name as Android shows it.</summary>
    public string BluetoothAddress { get; set; } = "";
    public string BluetoothName { get; set; } = "";
    public PrinterRole Role { get; set; } = PrinterRole.Kitchen;
    /// <summary>58 or 80 (mm).</summary>
    public int Paper { get; set; } = 80;
    public int Copies { get; set; } = 1;
    /// <summary>Send a partial cut after the ticket. Off for a built-in printer with no cutter.</summary>
    public bool Cut { get; set; } = true;
    /// <summary>The cut command to send when <see cref="Cut"/> is on.</summary>
    public CutMode CutMode { get; set; } = CutMode.Partial;
    public bool Enabled { get; set; } = true;

    /// <summary>Printer dots across the paper: 80 mm → 576, 58 mm → 384.</summary>
    [JsonIgnore] public int WidthDots => Paper == 58 ? 384 : 576;

    /// <summary>Two configs pointing at the same hardware share this key, so one paper prints, not two.</summary>
    [JsonIgnore] public string PhysicalKey => Connection switch
    {
        PrinterConnection.WiFi => $"tcp:{Host.Trim().ToLowerInvariant()}:{Port}",
        PrinterConnection.Usb => $"usb:{UsbVendorId:x4}:{UsbProductId:x4}",
        PrinterConnection.Serial => $"serial:{SerialPath.Trim()}",
        PrinterConnection.Bluetooth => $"bt:{BluetoothAddress.Trim().ToLowerInvariant()}",
        _ => "builtin",
    };

    [JsonIgnore] public string Target => Connection switch
    {
        PrinterConnection.WiFi => $"{Host}:{Port}",
        PrinterConnection.Usb => UsbVendorId == 0 ? "USB" : $"USB {UsbVendorId:X4}:{UsbProductId:X4}",
        PrinterConnection.Serial => SerialPath,
        PrinterConnection.Bluetooth => BluetoothName.Length > 0 ? $"BT {BluetoothName}" : $"BT {BluetoothAddress}",
        _ => "built-in",
    };

    /// <summary>What the printers list shows under the name.</summary>
    [JsonIgnore] public string Summary
    {
        get
        {
            var role = Localization.L.T(Role switch { PrinterRole.Cashier => "cashier", PrinterRole.Kitchen => "kitchen", _ => "bar" });
            return $"{role} · {Target} · {Paper} mm · ×{Copies}{(Enabled ? "" : " · OFF")}";
        }
    }
}

/// <summary>What the kitchen was last told about an open-invoice line: which tab it sits
/// on, how many, and its name/price (kept so a struck line can still be printed).</summary>
public sealed record TabLineMemo(int TabId, int Qty, string Name, decimal UnitPrice);

/// <summary>
/// What this till has already printed for ONE store, remembered across restarts so a
/// relaunch never reprints the day's orders. Only the newest ids are kept.
/// </summary>
public sealed class StoreMemory
{
    /// <summary>Orders whose kitchen tickets went out (or that were on the board before the first run).</summary>
    public List<int> Printed { get; set; } = [];
    /// <summary>Orders whose cashier bill went out.</summary>
    public List<int> Receipted { get; set; } = [];
    /// <summary>Open-table line ids already sent to the kitchen (so a round prints once).</summary>
    public List<int> SeenTabLines { get; set; } = [];
    /// <summary>Line id → what the kitchen was last told, so an edit prints as "1 → 3".</summary>
    public Dictionary<int, TabLineMemo> TabMemos { get; set; } = [];
    /// <summary>The board was seen once: everything on it then counts as history.</summary>
    public bool OrdersInitialised { get; set; }
    /// <summary>The open tabs were seen once: their lines then count as already sent.</summary>
    public bool TabsInitialised { get; set; }
    /// <summary>The recently closed orders were seen once (1.3.0): they count as already printed.</summary>
    public bool HistoryInitialised { get; set; }
}

/// <summary>
/// Everything the till remembers between runs. Stored as one JSON blob in Preferences;
/// the password and token live in SecureStorage, never here.
/// </summary>
public sealed class AppSettings
{
    public const int KeepIds = 2000;

    public string ApiBaseUrl { get; set; } = "https://api.orderorange.com/";
    /// <summary>The customer site a printed receipt's QR points at (verify/store links).</summary>
    public string ClientUrl { get; set; } = "https://orderorange.com";
    public string Username { get; set; } = "";
    /// <summary>UI language: en, ar or fa.</summary>
    public string Language { get; set; } = "en";
    public bool AutoPrintReceipt { get; set; } = true;
    public bool AutoPrintKitchen { get; set; } = true;
    /// <summary>
    /// What happens to a product with no tick on the Product printers screen: true = it still
    /// prints on the kitchen/bar printers the web map (or the default) says; false = no work
    /// ticket at all (Majed 2026-10-18: "I did not set those items for kitchen but coming").
    /// </summary>
    public bool UnroutedToKitchen { get; set; } = true;
    /// <summary>Seconds between polls — also how long a web sale takes to reach the printer.</summary>
    public int PollSeconds { get; set; } = 8;
    /// <summary>Floyd–Steinberg dithering instead of a plain threshold (better for photo logos, speckles text).</summary>
    public bool Dither { get; set; }
    /// <summary>Grey level above which a pixel prints white (0–255).</summary>
    public int Threshold { get; set; } = 160;
    public List<PrinterConfig> Printers { get; set; } = [];
    public Dictionary<int, StoreMemory> Stores { get; set; } = [];
    public int LastStoreId { get; set; }
    public string StoreName { get; set; } = "";
    /// <summary>The route that last reached the built-in printer ("usb", "tcp:127.0.0.1:9100", "serial:/dev/ttyS1").</summary>
    public string BuiltInRoute { get; set; } = "";

    /// <summary>
    /// Product id → the printers (PrinterConfig.Id) its work ticket goes to, ticked on this
    /// terminal's Product printers screen — same idea as the Windows till's routing grid
    /// (Majed 2026-10-18: "make it same to windows"). A product with no entry follows
    /// <see cref="CategoryPrinters"/>, then the web map (Kitchen/Bar roles).
    /// </summary>
    public Dictionary<int, List<string>> ProductPrinters { get; set; } = [];

    /// <summary>
    /// Category id → printers every product of that category shared when routing was saved,
    /// so a dish added later on the web lands on the same printers without a visit here.
    /// </summary>
    public Dictionary<int, List<string>> CategoryPrinters { get; set; } = [];

    /// <summary>The local ticks for a product: its own rule, else its category's. Empty = not routed here.</summary>
    public List<PrinterConfig> LocalPrintersFor(int productId, int categoryId)
    {
        var ids = ProductPrinters.TryGetValue(productId, out var own) && own.Count > 0 ? own
                : CategoryPrinters.GetValueOrDefault(categoryId) ?? [];
        if (ids.Count == 0) return [];
        // Old ticks on a Cashier printer are ignored unless it is the only printer there is.
        var picked = Printers.Where(p => p.Enabled && ids.Contains(p.Id)).ToList();
        var work = picked.Where(p => p.Role != PrinterRole.Cashier).ToList();
        return work.Count > 0 || Printers.Any(p => p.Enabled && p.Role != PrinterRole.Cashier) ? work : picked;
    }

    private const string Key = "settings";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        try
        {
            var raw = Preferences.Default.Get(Key, "");
            if (raw.Length > 0) return JsonSerializer.Deserialize<AppSettings>(raw, Json) ?? new AppSettings();
        }
        catch { /* a corrupt blob must never stop the till from opening */ }
        return new AppSettings();
    }

    public void Save()
    {
        try { Preferences.Default.Set(Key, JsonSerializer.Serialize(this, Json)); }
        catch { }
    }

    public StoreMemory For(int storeId)
    {
        if (!Stores.TryGetValue(storeId, out var m)) Stores[storeId] = m = new StoreMemory();
        return m;
    }

    /// <summary>Appends ids not already listed and trims the list to the newest <see cref="KeepIds"/>.</summary>
    public static void Remember(List<int> list, IEnumerable<int> ids)
    {
        var have = list.ToHashSet();
        foreach (var id in ids) if (have.Add(id)) list.Add(id);
        if (list.Count > KeepIds) list.RemoveRange(0, list.Count - KeepIds);
    }

    public PrinterConfig? Cashier => Printers.FirstOrDefault(p => p.Enabled && p.Role == PrinterRole.Cashier);

    public List<PrinterConfig> WithRole(PrinterRole role) => Printers.Where(p => p.Enabled && p.Role == role).ToList();
}
