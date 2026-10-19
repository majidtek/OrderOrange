using Microsoft.Maui.Storage;
using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Models;
using OrderOrange.AndroidTill.Printing;
using OrderOrange.Shared;

namespace OrderOrange.AndroidTill.Services;

/// <summary>One line of the on-screen print history.</summary>
public sealed record PrintJob(DateTime At, string Kind, string OrderNo, string Printer, bool Ok, string? Error)
{
    public string Time => At.ToString("HH:mm:ss");
    public string Title => string.IsNullOrEmpty(OrderNo) ? Kind : $"{Kind} #{OrderNo}";
    public string Result => Ok ? L.T("ok") : $"{L.T("failed")}: {Error}";
}

/// <summary>
/// The till's brain: signs in, polls the board and the open tables, and decides what to
/// print where — a straight port of the Windows till's <c>MainWindow.RefreshOrdersAsync</c>
/// with the same two-stage rule (kitchen ticket on arrival, cashier bill when due), the
/// same closed-table and open-round handling and the same per-store memory.
/// One difference, deliberately: a ticket that failed to print is NOT marked printed —
/// it is retried on the next poll and a red banner says which printer is unreachable.
/// </summary>
public sealed class TillEngine
{
    public static TillEngine Instance { get; } = new();

    public ApiService Api { get; } = new();
    public AppSettings Settings => AppSettings.Current;

    /// <summary>Raised (on any thread) whenever something the screens show has changed.</summary>
    public event Action? Changed;

    public string Status { get; private set; } = "";
    public string? Banner { get; private set; }
    public bool Online { get; private set; }
    public bool Running => _cts is not null;
    public List<PrintJob> Jobs { get; } = [];
    public List<StoreSummaryDto> Stores { get; private set; } = [];
    public bool HasSavedSession => Preferences.Default.Get(SessionFlag, false);

    private const string SessionFlag = "till.hasSession";
    private const string TokenKey = "till.token";
    private const string PasswordKey = "till.password";

    // ---- what the store looks like ----
    private ReceiptDesignDto _design = ReceiptDesignDto.Default;
    private KitchenDesignDto _kitchenDesign = KitchenDesignDto.Default;
    private StoreProfile _store = StoreProfile.Fallback("", "https://orderorange.com");
    private string _kitchenLang = "";
    private Dictionary<int, string> _routes = [];
    private readonly Dictionary<string, MenuItemDto> _menuByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _menuIdByName = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _storeLoadedAt = DateTime.MinValue;
    private static readonly TimeSpan StoreRefresh = TimeSpan.FromMinutes(10);

    // ---- what has been printed ----
    private readonly HashSet<int> _printed = [];
    private readonly HashSet<int> _receipted = [];
    private readonly HashSet<int> _seenTabLines = [];
    private readonly HashSet<int> _warnedNoCashier = [];
    private Dictionary<int, TabLineMemo> _tabMemos = [];
    private bool _memoryLoaded;
    /// <summary>Table name → (product name → quantity already sent to the kitchen for that table).</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _kitchenTally = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>A job that reached some printers but not all: which physical printers are done.</summary>
    private readonly Dictionary<string, HashSet<string>> _partial = [];

    private (PrinterConfig Printer, string Html, int CssWidth)? _lastReceipt;
    private List<(PrinterConfig Printer, string Html, int CssWidth)> _lastTickets = [];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private bool _pollHadFailure;

    private TillEngine()
    {
        Api.BaseUrl = Settings.ApiBaseUrl;
        Api.Relogin = ReloginAsync;
    }

    private void Notify() => Changed?.Invoke();

    // =====================================================================
    //  Session
    // =====================================================================

    public async Task<string?> SignInAsync(string apiUrl, string username, string password)
    {
        apiUrl = apiUrl.Trim();
        if (!apiUrl.EndsWith('/')) apiUrl += "/";
        Settings.ApiBaseUrl = apiUrl;
        Api.BaseUrl = apiUrl;
        var err = await Api.SignInAsync(username.Trim(), password);
        if (err is not null) return err;

        Settings.Username = username.Trim();
        Settings.LastStoreId = Api.RestaurantId ?? 0;
        Settings.StoreName = Api.StoreName ?? "";
        Settings.Save();
        await SaveSecretsAsync(password);
        ResetStoreState();
        await LoadStoreAsync();
        TillLog.Write($"signed in as {Settings.Username} → store {Settings.LastStoreId} {Settings.StoreName}");
        return null;
    }

    /// <summary>Resumes the session of an earlier run. False when there is none to resume.</summary>
    public async Task<bool> RestoreSessionAsync()
    {
        if (Api.SignedIn) return true;
        string? token = null;
        try { token = await SecureStorage.Default.GetAsync(TokenKey); } catch { }
        if (string.IsNullOrEmpty(token)) return false;
        Api.BaseUrl = Settings.ApiBaseUrl;
        Api.UseSession(token, Settings.LastStoreId, Settings.StoreName);
        ResetStoreState();
        await LoadStoreAsync();
        return true;
    }

    /// <summary>A 401 came back: sign in again with the saved credentials, back into the same store.</summary>
    private async Task<bool> ReloginAsync()
    {
        string? password = null;
        try { password = await SecureStorage.Default.GetAsync(PasswordKey); } catch { }
        if (string.IsNullOrEmpty(password) || Settings.Username.Length == 0) return false;

        var wanted = Settings.LastStoreId;
        var err = await Api.SignInAsync(Settings.Username, password);
        if (err is not null) { TillLog.Write($"re-login failed: {err}"); return false; }
        if (wanted > 0 && Api.RestaurantId != wanted)
        {
            var e2 = await Api.SwitchStoreAsync(wanted);
            if (e2 is not null) TillLog.Write($"re-login: could not return to store {wanted}: {e2}");
        }
        await SaveSecretsAsync(null);
        TillLog.Write("session renewed");
        return true;
    }

    private async Task SaveSecretsAsync(string? password)
    {
        try
        {
            await SecureStorage.Default.SetAsync(TokenKey, Api.Token ?? "");
            if (password is not null) await SecureStorage.Default.SetAsync(PasswordKey, password);
            Preferences.Default.Set(SessionFlag, Api.SignedIn);
        }
        catch (Exception ex) { TillLog.Write($"secure storage: {ex.Message}"); }
    }

    public async Task<string?> SwitchStoreAsync(int restaurantId)
    {
        if (restaurantId == Api.RestaurantId) return null;
        await _gate.WaitAsync();   // never swap stores under a running poll
        try
        {
            var err = await Api.SwitchStoreAsync(restaurantId);
            if (err is not null) return err;
            Settings.LastStoreId = restaurantId;
            Settings.StoreName = Api.StoreName ?? "";
            Settings.Save();
            await SaveSecretsAsync(null);
            ResetStoreState();   // a different business — its orders are unrelated to the last one's
            await LoadStoreAsync();
            TillLog.Write($"switched to store {restaurantId} {Settings.StoreName}");
        }
        finally { _gate.Release(); }
        Notify();
        return null;
    }

    public async Task SignOutAsync()
    {
        Stop();
        Api.SignOut();
        try { SecureStorage.Default.Remove(TokenKey); SecureStorage.Default.Remove(PasswordKey); } catch { }
        Preferences.Default.Set(SessionFlag, false);
        ResetStoreState();
        Status = L.T("notSignedIn");
        Banner = null;
        Notify();
        await Task.CompletedTask;
    }

    private void ResetStoreState()
    {
        _printed.Clear(); _receipted.Clear(); _seenTabLines.Clear(); _tabMemos = [];
        _kitchenTally.Clear(); _partial.Clear();
        _memoryLoaded = false;
        _storeLoadedAt = DateTime.MinValue;
    }

    /// <summary>
    /// Pulls the owner's receipt and kitchen designs, the store profile, the print routes
    /// and the menu, so every paper this till prints matches what the owner laid out.
    /// </summary>
    public async Task LoadStoreAsync()
    {
        _design = await Api.ReceiptDesignAsync();
        _kitchenDesign = await Api.KitchenDesignAsync();
        var profile = await Api.ProfileAsync();
        _kitchenLang = profile?.KitchenLanguage ?? "";
        var clientUrl = string.IsNullOrWhiteSpace(Settings.ClientUrl) ? "https://orderorange.com" : Settings.ClientUrl;
        _store = profile is not null
            ? StoreProfile.From(profile, clientUrl)
            : StoreProfile.Fallback(Api.StoreName ?? Settings.StoreName, clientUrl);
        _routes = await Api.PrintRoutesAsync();
        _menuByName.Clear(); _menuIdByName.Clear();
        foreach (var category in await Api.MenuAsync())
            foreach (var item in category.Items)
            {
                _menuByName[item.Name] = item;
                _menuIdByName[item.Name] = item.Id;
            }
        Stores = await Api.MyStoresAsync();
        _storeLoadedAt = DateTime.UtcNow;
        // Piggy-back the update check on the store refresh (every ~10 min): the home screen offers a newer release.
        try { await UpdateService.CheckAsync(Api); } catch { }
        Notify();
    }

    // =====================================================================
    //  The poll loop
    // =====================================================================

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(() => LoopAsync(ct));
        Notify();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        Notify();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await RefreshAsync(); }
            catch (Exception ex)
            {
                TillLog.Write($"poll threw: {ex}");
                Banner = ex.Message;
                Notify();
            }
            try { await WaitForNextAsync(ct); }
            catch (OperationCanceledException) { }
        }
    }

    private long _changeVersion;

    /// <summary>
    /// Sleeps until the next pass: the server's long poll wakes us the moment the store's
    /// orders change (a web sale prints in about a second); the timed poll stays as the
    /// safety net and as the only mechanism against an older server.
    /// </summary>
    private async Task WaitForNextAsync(CancellationToken ct)
    {
        var poll = TimeSpan.FromSeconds(Math.Clamp(Settings.PollSeconds, 3, 300));
        if (!Api.SignedIn || Api.PushUnsupported || !Online)
        {
            await Task.Delay(poll, ct);
            return;
        }
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var wait = Api.WaitForChangeAsync(_changeVersion, waitCts.Token);
        var timer = Task.Delay(poll, ct);
        var first = await Task.WhenAny(wait, timer);
        if (first == wait && wait.Result is { } version)
        {
            if (version > _changeVersion) { _changeVersion = version; return; }   // changed → refresh now
            // unchanged after the server's timeout → fall through to a normal pass
        }
        // The timer won (or the wait failed): one ordinary pass; drop the stray long poll.
        waitCts.Cancel();
    }

    /// <summary>One pass over the board and the open tables. Safe to call from a button too.</summary>
    public async Task RefreshAsync()
    {
        if (!Api.SignedIn && !await RestoreSessionAsync())
        {
            Status = L.T("notSignedIn");
            Notify();
            return;
        }
        if (!await _gate.WaitAsync(0)) return;   // a pass is already running
        try
        {
            _pollHadFailure = false;
            if (DateTime.UtcNow - _storeLoadedAt > StoreRefresh) await LoadStoreAsync();

            var board = await Api.BoardAsync();
            if (board is null)
            {
                Online = false;
                Status = $"{L.T("offline")} · {Api.LastError}";
                Notify();
                return;
            }
            Online = true;

            // Orders finished in the last half hour that this till never saw: a web counter sale
            // is placed and closed in one go, so by the next poll it has already left the board
            // (Majed 2026-10-18: "when I do invoice from web not coming print").
            var recentClosed = ((await Api.HistoryAsync()) ?? [])
                .Where(o => o.Status == OrderStatus.Delivered && o.PlacedAt >= DateTime.Now.AddMinutes(-30))
                .ToList();

            var storeId = Api.RestaurantId ?? 0;
            var memory = Settings.For(storeId);
            if (!_memoryLoaded && !await LoadMemoryAsync(memory, storeId, board, recentClosed)) return;

            // Open tables: a round rung onto a table has no order yet, so print its NEW lines
            // to the kitchen straight away (never the bill — that waits for the table to close).
            if (Settings.AutoPrintKitchen) await PrintTabRoundsAsync(memory);

            // Two-stage printing:
            //   • the KITCHEN ticket prints the moment an order appears, and
            //   • the CASHIER bill waits until the bill is due — a dine-in/counter order is
            //     paid (table closed), or a delivery/pickup order is Ready or beyond.
            var newTickets = new List<int>();
            var newReceipts = new List<int>();
            foreach (var order in board.Concat(recentClosed.Where(c => board.All(b => b.Id != c.Id))))
            {
                // A closed table's bill lands on the board as a paid dine-in order — but the
                // kitchen already got every round while the table was open, so it must NOT be
                // ticketed again; only the lines the kitchen has not had yet, then the bill.
                var fromClosedTable = IsDineIn(order)
                    && (order.IsPaid || (order.PaymentRef?.StartsWith("TAB-", StringComparison.OrdinalIgnoreCase) ?? false));

                // The kitchen ticket and the bill go to different printers, so they print at the
                // same time instead of one after the other (PrintService serialises per printer).
                Task<bool>? ticketTask = null;
                if (!_printed.Contains(order.Id))
                {
                    if (fromClosedTable)
                    {
                        ticketTask = Settings.AutoPrintKitchen && ChangeItems(order) is { Count: > 0 } change
                            ? PrintTicketsCore(order with { Items = change }, null, L.T("ticket"))
                            : Task.FromResult(true);
                    }
                    else
                        ticketTask = Settings.AutoPrintKitchen ? PrintTicketsCore(order, null, L.T("ticket")) : Task.FromResult(true);
                }

                Task<string?>? billTask = null;
                if (Settings.AutoPrintReceipt && !_receipted.Contains(order.Id) && ReadyForReceipt(order))
                {
                    if (Settings.Cashier is { } cashier) billTask = PrintReceiptAsync(order, cashier);
                    else if (_warnedNoCashier.Add(order.Id))
                    {
                        // Say so instead of staying silent: the bill is due and no printer is marked Cashier.
                        TillLog.Write($"bill #{order.Number} due but no enabled Cashier printer is set up");
                        _pollHadFailure = true;
                        Banner = L.T("noCashierPrinter");
                    }
                }

                if (ticketTask is not null)
                {
                    var ok = await ticketTask;
                    if (ok && fromClosedTable) _kitchenTally.Remove(order.TableName ?? "");   // table is done
                    if (ok) { _printed.Add(order.Id); newTickets.Add(order.Id); }
                }
                if (billTask is not null && await billTask is null) { _receipted.Add(order.Id); newReceipts.Add(order.Id); }
            }
            if (newTickets.Count > 0) AppSettings.Remember(memory.Printed, newTickets);
            if (newReceipts.Count > 0) AppSettings.Remember(memory.Receipted, newReceipts);
            if (newTickets.Count > 0 || newReceipts.Count > 0) Settings.Save();

            if (!_pollHadFailure) Banner = null;
            Status = $"{board.Count} {L.T("live")} · {L.T("lastCheck")} {DateTime.Now:HH:mm:ss}";
            Notify();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Reads what this till already printed for the store. A till that has never remembered
    /// anything for this store treats what is on the board and in the tabs right now as
    /// history — it must not reprint the day's orders.
    /// </summary>
    private async Task<bool> LoadMemoryAsync(StoreMemory memory, int storeId, List<OrderDto> board, List<OrderDto> recentClosed)
    {
        _printed.Clear(); _printed.UnionWith(memory.Printed);
        _receipted.Clear(); _receipted.UnionWith(memory.Receipted);
        if (!memory.OrdersInitialised)
        {
            var seen = board.Select(o => o.Id).ToList();
            _printed.UnionWith(seen); _receipted.UnionWith(seen);
            AppSettings.Remember(memory.Printed, seen);
            AppSettings.Remember(memory.Receipted, seen);
            memory.OrdersInitialised = true;
            Settings.Save();
            TillLog.Write($"store {storeId}: first run — {seen.Count} order(s) on the board taken as history");
        }
        if (!memory.HistoryInitialised)
        {
            // First run with the closed-orders sweep: what is already finished stays unprinted.
            var seen = recentClosed.Select(o => o.Id).ToList();
            _printed.UnionWith(seen); _receipted.UnionWith(seen);
            AppSettings.Remember(memory.Printed, seen);
            AppSettings.Remember(memory.Receipted, seen);
            memory.HistoryInitialised = true;
            Settings.Save();
            TillLog.Write($"store {storeId}: {seen.Count} recently closed order(s) taken as history");
        }

        _seenTabLines.Clear(); _seenTabLines.UnionWith(memory.SeenTabLines);
        _tabMemos = new Dictionary<int, TabLineMemo>(memory.TabMemos);
        if (!memory.TabsInitialised)
        {
            var tabsNow = await Api.OpenTabsAsync();
            if (tabsNow is null) return false;   // can't take the baseline safely; next poll
            var lineIds = tabsNow.SelectMany(t => t.Lines.Select(l => l.Id)).ToList();
            _seenTabLines.UnionWith(lineIds);
            AppSettings.Remember(memory.SeenTabLines, lineIds);
            memory.TabsInitialised = true;
            Settings.Save();
            TillLog.Write($"store {storeId}: first run — {lineIds.Count} open-table line(s) taken as sent");
        }
        _memoryLoaded = true;
        return true;
    }

    // =====================================================================
    //  Open-table rounds
    // =====================================================================

    /// <summary>
    /// Prints a kitchen ticket for every NEW line on an open table — the round just rung up —
    /// routed to the same station printers as an order. The bill is never printed here.
    /// </summary>
    private async Task PrintTabRoundsAsync(StoreMemory memory)
    {
        var tabs = await Api.OpenTabsAsync();
        if (tabs is null) return;   // server unreachable this time round — keep what we know

        // Diff against a copy: a tab whose round failed to print keeps its old memos, so the
        // same change is offered again next poll instead of being forgotten.
        var memos = new Dictionary<int, TabLineMemo>(_tabMemos);
        var rounds = TabRounds.Diff(tabs, _seenTabLines, memos, out var memosChanged);
        var freshlySeen = new List<int>();
        var failedTabs = new HashSet<int>();
        foreach (var round in rounds)
        {
            TillLog.Write($"open invoice {round.Tab.Id} ({round.Tab.TableName}): {(round.Edited ? "edit" : "new")} round, " +
                string.Join(", ", round.Items.Select(i => round.Changes.TryGetValue(i.Id, out var c) ? $"{i.Name} {c.From}→{c.To}" : $"{i.Name} ×{i.Quantity}")));
            var pseudo = TabToOrder(round.Tab, round.Items);
            var ok = await PrintTicketsCore(pseudo, round.Edited ? round.Changes : null, L.T("round"));
            if (!ok) { failedTabs.Add(round.Tab.Id); continue; }
            // The running bill: the whole table so far, on the cashier printer, every time it is saved.
            if (Settings.ReceiptOnTabSave && Settings.AutoPrintReceipt && Settings.Cashier is { } cashier)
                await PrintReceiptAsync(TabBill(round.Tab), cashier);
            foreach (var it in round.Items)
            {
                if (_seenTabLines.Add(it.Id)) freshlySeen.Add(it.Id);
                // The close-time delta ticket counts what the kitchen has really been given.
                var delta = round.Changes.TryGetValue(it.Id, out var c) ? c.Delta : it.Quantity;
                TallyKitchen(round.Tab.TableName, it.Name, delta);
            }
        }

        if (memosChanged)
        {
            var merged = new Dictionary<int, TabLineMemo>();
            foreach (var (id, m) in memos) if (!failedTabs.Contains(m.TabId)) merged[id] = m;
            foreach (var (id, m) in _tabMemos) if (failedTabs.Contains(m.TabId)) merged[id] = m;
            _tabMemos = merged;
            memory.TabMemos = new Dictionary<int, TabLineMemo>(merged);
        }
        if (freshlySeen.Count > 0) AppSettings.Remember(memory.SeenTabLines, freshlySeen);
        if (freshlySeen.Count > 0 || memosChanged) Settings.Save();
    }

    /// <summary>Wraps an open table's new lines as an order, only so the ticket printer can route them.</summary>
    private OrderDto TabToOrder(StoreTabDto tab, List<OrderItemDto> items) => new(
        Id: tab.Id,
        Number: tab.Id.ToString(),   // the open table's invoice number, as the POS shows it
        Status: OrderStatus.Pending, PlacedAt: DateTime.Now,
        CustomerId: 0, CustomerName: tab.CustomerName ?? tab.GuestName ?? "", CustomerPhone: "",
        RestaurantId: Api.RestaurantId ?? 0, RestaurantName: "", RestaurantLogoEmoji: "",
        RestaurantArea: "", RestaurantPhone: "",
        DriverId: null, DriverName: null, DriverPhone: null,
        DeliveryAddress: $"Dine-in — {tab.TableName}", DeliveryLat: null, DeliveryLng: null,
        PaymentMethod: PaymentMethod.CashOnDelivery,
        Subtotal: 0, DeliveryFee: 0, ServiceFee: 0, Discount: 0, Total: 0,
        Notes: null, CancelReason: null, EstimatedMinutes: 0, IsPaid: false, PaymentRef: null,
        HasReview: false, Items: items, History: [],
        TableName: tab.TableName);

    /// <summary>The open table's bill so far — every line and the server's totals — as an unpaid order for the receipt layout.</summary>
    private OrderDto TabBill(StoreTabDto tab) => new(
        Id: tab.Id,
        Number: tab.Id.ToString(),
        Status: OrderStatus.Pending, PlacedAt: tab.OpenedAt,
        CustomerId: 0, CustomerName: tab.CustomerName ?? tab.GuestName ?? "", CustomerPhone: "",
        RestaurantId: Api.RestaurantId ?? 0, RestaurantName: "", RestaurantLogoEmoji: "",
        RestaurantArea: "", RestaurantPhone: "",
        DriverId: null, DriverName: null, DriverPhone: null,
        DeliveryAddress: $"Dine-in — {tab.TableName}", DeliveryLat: null, DeliveryLng: null,
        PaymentMethod: PaymentMethod.CashOnDelivery,
        Subtotal: tab.Subtotal, DeliveryFee: 0, ServiceFee: tab.ServiceFee, Discount: 0, Total: tab.Total,
        Notes: null, CancelReason: null, EstimatedMinutes: 0, IsPaid: false, PaymentRef: null,
        HasReview: false,
        Items: tab.Lines.Select(l => new OrderItemDto(l.Id, l.Name, l.UnitPrice, l.Quantity, l.Notes)).ToList(),
        History: [],
        TaxPercent: tab.TaxPercent, TaxAmount: tab.TaxAmount,
        TableName: tab.TableName);

    private void TallyKitchen(string? table, string name, int qty)
    {
        var t = table ?? "";
        if (!_kitchenTally.TryGetValue(t, out var m)) _kitchenTally[t] = m = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        m[name] = m.GetValueOrDefault(name) + qty;
    }

    /// <summary>The order's lines the kitchen has NOT already been given for this table.</summary>
    private List<OrderItemDto> ChangeItems(OrderDto order)
    {
        var printed = _kitchenTally.GetValueOrDefault(order.TableName ?? "") ?? [];
        var change = new List<OrderItemDto>();
        foreach (var g in order.Items.GroupBy(i => i.Name))
        {
            var need = g.Sum(i => i.Quantity) - printed.GetValueOrDefault(g.Key);
            if (need > 0)
            {
                var first = g.First();
                change.Add(new OrderItemDto(first.Id, g.Key, first.UnitPrice, need, first.Notes));
            }
        }
        return change;
    }

    // =====================================================================
    //  Rules (identical to the Windows till)
    // =====================================================================

    private static bool IsDineIn(OrderDto o) =>
        !string.IsNullOrEmpty(o.TableName)
        || (o.DeliveryAddress?.StartsWith("Dine-in", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// When the cashier bill is due: a dine-in or counter order once it is paid (the table
    /// closed / counter settled), and a delivery or pickup order once it is Ready or beyond.
    /// </summary>
    private static bool ReadyForReceipt(OrderDto o)
    {
        var dineIn = IsDineIn(o);
        var counter = !dineIn && string.Equals(o.DeliveryAddress, "Counter", StringComparison.OrdinalIgnoreCase);
        var ready = o.Status is OrderStatus.Ready or OrderStatus.PickedUp or OrderStatus.OnTheWay or OrderStatus.Delivered;
        return (dineIn || counter) ? (o.IsPaid || ready) : ready;
    }

    /// <summary>
    /// Which printers a line goes to. The ticks made on this terminal's Product printers
    /// screen win (product rule, else category rule — like the Windows till); otherwise the
    /// web portal's route ("bar" → bar printers, "both" → kitchen AND bar printers, anything
    /// else → kitchen printers). No printer of that role → the first kitchen/bar printer, so
    /// a dish is never silently unprinted. Cashier printers get work tickets only when ticked.
    /// </summary>
    private List<PrinterConfig> PrintersFor(OrderItemDto item)
    {
        var pid = _menuIdByName.GetValueOrDefault(item.Name, item.Id);
        var categoryId = _menuByName.TryGetValue(item.Name, out var menuItem) ? menuItem.CategoryId : 0;
        var local = Settings.LocalPrintersFor(pid, categoryId);
        if (local.Count > 0) return local;
        if (!Settings.UnroutedToKitchen) return [];   // the owner chose: no tick, no ticket

        var station = _routes.GetValueOrDefault(pid);
        var list = station switch
        {
            "bar" => Settings.WithRole(PrinterRole.Bar),
            "both" => [.. Settings.WithRole(PrinterRole.Kitchen), .. Settings.WithRole(PrinterRole.Bar)],
            _ => Settings.WithRole(PrinterRole.Kitchen),
        };
        if (list.Count == 0)
        {
            var any = Settings.Printers.FirstOrDefault(p => p.Enabled && p.Role is PrinterRole.Kitchen or PrinterRole.Bar);
            if (any is not null) list = [any];
        }
        return list;
    }

    /// <summary>Product name → its name in the store's kitchen language (empty = as ordered).</summary>
    private Dictionary<string, string> KitchenNames(IEnumerable<OrderItemDto> items)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_kitchenLang.Length == 0) return names;
        foreach (var it in items)
            if (_menuByName.TryGetValue(it.Name, out var m)) names[it.Name] = m.NameFor(_kitchenLang);
        return names;
    }

    // =====================================================================
    //  Paper
    // =====================================================================

    /// <summary>
    /// Prints the kitchen/bar work tickets for an order — one per physical printer, no bill.
    /// True when every printer got its paper (or there was nothing to print).
    /// </summary>
    private async Task<bool> PrintTicketsCore(OrderDto order, IReadOnlyDictionary<int, LineChange>? changes, string kind)
    {
        var byPrinter = new Dictionary<string, (PrinterConfig Printer, List<OrderItemDto> Items)>();
        foreach (var item in order.Items)
            foreach (var p in PrintersFor(item))
            {
                if (!byPrinter.TryGetValue(p.PhysicalKey, out var bucket))
                    byPrinter[p.PhysicalKey] = bucket = (p, []);
                if (!bucket.Items.Any(i => i.Id == item.Id)) bucket.Items.Add(item);
            }
        if (byPrinter.Count == 0)
        {
            TillLog.Write($"{kind} #{order.Number}: no kitchen/bar printer set up — nothing to print");
            return true;
        }

        var jobKey = $"{kind}:{order.Id}:{string.Join(",", order.Items.Select(i => $"{i.Id}x{i.Quantity}"))}";
        var done = _partial.GetValueOrDefault(jobKey) ?? [];
        var strings = TicketStrings.For(_kitchenLang);
        var printedNow = new List<(PrinterConfig, string, int)>();
        var allOk = true;

        foreach (var (printer, items) in byPrinter.Values)
        {
            if (done.Contains(printer.PhysicalKey)) continue;
            var html = KitchenTicket.Html(order, items, printer.Paper, _kitchenDesign, printer.Name,
                KitchenNames(items), changes, strings);
            var cssWidth = PrintService.TicketCssWidth(printer.Paper);
            var err = await PrintService.PrintHtmlAsync(printer, html, cssWidth);
            Record(kind, order.Number, printer, err);
            TillLog.Write($"{kind} #{order.Number} → {printer.Name} [{printer.Target}] {items.Count} line(s){(changes is { Count: > 0 } ? " EDITED" : "")}{(err is null ? "" : " FAILED: " + err)}");
            if (err is null) { done.Add(printer.PhysicalKey); printedNow.Add((printer, html, cssWidth)); }
            else allOk = false;
        }

        if (allOk) _partial.Remove(jobKey); else _partial[jobKey] = done;
        if (printedNow.Count > 0) _lastTickets = printedNow;
        return allOk;
    }

    /// <summary>The designed bill on the cashier printer, at that printer's paper width. Null on success.</summary>
    private async Task<string?> PrintReceiptAsync(OrderDto order, PrinterConfig printer)
    {
        var design = _design with { PaperWidth = printer.Paper };
        var html = HtmlReceipt.Build(order, _store, design);
        var cssWidth = HtmlReceipt.CssWidth(printer.Paper);
        var err = await PrintService.PrintHtmlAsync(printer, html, cssWidth);
        Record(L.T("receipt"), order.Number, printer, err);
        TillLog.Write($"bill #{order.Number} → {printer.Name} [{printer.Target}]{(err is null ? "" : " FAILED: " + err)}");
        if (err is null) _lastReceipt = (printer, html, cssWidth);
        return err;
    }

    private void Record(string kind, string orderNo, PrinterConfig printer, string? error)
    {
        lock (Jobs)
        {
            Jobs.Insert(0, new PrintJob(DateTime.Now, kind, orderNo, printer.Name, error is null, error));
            if (Jobs.Count > 20) Jobs.RemoveRange(20, Jobs.Count - 20);
        }
        if (error is not null)
        {
            _pollHadFailure = true;
            Banner = $"{printer.Name} ({printer.Target}): {error}";
        }
        Notify();
    }

    public async Task<string?> ReprintLastReceiptAsync()
    {
        if (_lastReceipt is not { } r) return L.T("nothingToReprint");
        var err = await PrintService.PrintHtmlAsync(r.Printer, r.Html, r.CssWidth);
        Record(L.T("receipt") + " ↻", "", r.Printer, err);
        return err;
    }

    public async Task<string?> ReprintLastTicketsAsync()
    {
        if (_lastTickets.Count == 0) return L.T("nothingToReprint");
        string? first = null;
        foreach (var (printer, html, css) in _lastTickets)
        {
            var err = await PrintService.PrintHtmlAsync(printer, html, css);
            Record(L.T("ticket") + " ↻", "", printer, err);
            first ??= err;
        }
        return first;
    }

    public async Task<string?> TestPrintAsync(PrinterConfig printer)
    {
        var html = PrintService.TestHtml(printer);
        var err = await PrintService.PrintHtmlAsync(printer, html, PrintService.TicketCssWidth(printer.Paper));
        Record(L.T("test"), "", printer, err);
        TillLog.Write($"test → {printer.Name} [{printer.Target}]{(err is null ? "" : " FAILED: " + err)}");
        return err;
    }
}
