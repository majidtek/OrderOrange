using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LocalHandler.Models;
using LocalHandler.Services;
using MahApps.Metro.Controls;
using OrderOrange.Shared;

namespace LocalHandler;

/// <summary>
/// The shop's till-side helper: it watches the store's live orders, prints the bill
/// on the cashier's printer and the work tickets on the kitchen's, and settles
/// payment. Everything it knows about printers lives on this machine; everything it
/// knows about orders comes from the ordinary partner API.
/// </summary>
public partial class MainWindow : MetroWindow
{
    private readonly Setup _setup;
    private readonly ApiService _api;
    private readonly DispatcherTimer _poll = new();

    private readonly ObservableCollection<Station> _stations = [];

    /// <summary>The stations, for XAML templates that cannot see the private field.</summary>
    public ObservableCollection<Station> StationList => _stations;

    /// <summary>Every enabled printer a product can be routed to — the cashier too, if the shop wants a ticket there.</summary>
    public ObservableCollection<Station> TicketStations { get; } = [];

    private void RefreshTicketStations()
    {
        TicketStations.Clear();
        foreach (var s in _stations.Where(s => s.Enabled)) TicketStations.Add(s);
    }
    private readonly ObservableCollection<OrderRow> _orders = [];
    private readonly ObservableCollection<RoutingRow> _routing = [];
    private readonly ObservableCollection<StoreSummaryDto> _stores = [];

    /// <summary>True while we set the switcher's selection in code, so it doesn't switch.</summary>
    private bool _switching;

    /// <summary>Orders already sent to a printer, so a refresh never prints twice.</summary>
    private readonly HashSet<int> _printed = [];

    /// <summary>The receipt the owner designed in the partner panel — refreshed on sign-in.</summary>
    private ReceiptDesignDto _design = ReceiptDesignDto.Default;

    /// <summary>The store facts printed on the bill (name, address, CR/VAT) + the QR host.</summary>
    private StoreProfile _store = StoreProfile.Fallback("", "https://orderorange.com");

    /// <summary>Prints the bill as the same HTML the web POS renders; created on first use.</summary>
    private WebReceiptPrinter? _webPrinter;

    public MainWindow() : this("") { }

    /// <param name="profile">Which partner this window is for; "" = the default profile.</param>
    public MainWindow(string profile)
    {
        _setup = Setup.Load(profile);
        InitializeComponent();
        DataContext = this;   // lets templates (the category headers) reach StationList
        _api = new ApiService(_setup);
        BillCode.UseSecret(_setup.BillSecret);   // so the receipt's QR verifies against the same signature the server uses

        var label = string.IsNullOrEmpty(_setup.Profile) ? "default" : _setup.Profile;
        Title = $"OrderOrange · Local Handler · {label} · v{UpdateService.Current}";
        ProfileLine.Text = $"This window's profile: {label}";

        foreach (var station in _setup.Stations) _stations.Add(station);
        StationsGrid.ItemsSource = _stations;
        OrdersList.ItemsSource = _orders;
        RoutingGrid.ItemsSource = _routing;
        StoreSwitcher.ItemsSource = _stores;
        RefreshTicketStations();

        // The routing list is grouped by category and filtered by the search box.
        RoutingRow.Changed = _ => { RefreshRoutingStats(); RefreshMassTicks(); };
        var view = CollectionViewSource.GetDefaultView(_routing);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RoutingRow.CategoryName)));
        view.Filter = o => o is RoutingRow r && (string.IsNullOrWhiteSpace(RoutingSearch.Text)
            || r.ProductName.Contains(RoutingSearch.Text, StringComparison.OrdinalIgnoreCase)
            || r.CategoryName.Contains(RoutingSearch.Text, StringComparison.OrdinalIgnoreCase));

        StationPrinter.ItemsSource = PrintService.InstalledPrinters();
        StationRole.ItemsSource = Enum.GetValues<StationRole>();
        StationRole.SelectedIndex = 1;             // Kitchen: the common case

        ApiUrl.Text = _setup.ApiBaseUrl;
        Username.Text = _setup.Username;
        Password.Password = _setup.Password;
        RememberPassword.IsOn = !string.IsNullOrEmpty(_setup.Password);
        AutoReceipt.IsOn = _setup.AutoPrintReceipt;
        AutoKitchen.IsOn = _setup.AutoPrintKitchen;
        AutoStart.IsOn = _setup.StartWithWindows;
        Autostart.Apply(_setup.Profile, _setup.StartWithWindows);   // keeps the Run entry pointing at THIS exe
        KitchenLang.ItemsSource = KitchenLanguages;
        KitchenLang.SelectedValue = KitchenLanguages.Any(l => l.Code == _setup.KitchenLanguage) ? _setup.KitchenLanguage : "";
        PollSeconds.Text = _setup.PollSeconds.ToString();

        _poll.Tick += async (_, _) => await RefreshOrdersAsync(auto: true);
        Loaded += async (_, _) =>
        {
            // First thing on every start: is there a newer till? If so we download it and
            // come back as the new version — nothing below runs in that case.
            if (await UpdateService.TryUpdateAsync(_setup, profile, t => Title = t)) return;
            _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            _updateTimer.Tick += async (_, _) => await UpdateService.TryUpdateAsync(_setup, profile, t => Title = t);
            _updateTimer.Start();
            if (!string.IsNullOrWhiteSpace(_setup.Username) && !string.IsNullOrWhiteSpace(_setup.Password))
                await ConnectAsync();
        };

        NavList.SelectedIndex = 0;   // opens on Orders, once every page panel exists
    }

    /// <summary>Sidebar navigation: show the chosen page, hide the rest.</summary>
    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageOrders is null) return;   // fires once during load before pages are built
        var i = NavList.SelectedIndex;
        PageOrders.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        PagePrinters.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        PageRouting.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
        PageConnection.Visibility = i == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- connection ----------------

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        _setup.ApiBaseUrl = string.IsNullOrWhiteSpace(ApiUrl.Text) ? _setup.ApiBaseUrl : ApiUrl.Text.Trim();
        var username = Username.Text.Trim();
        var password = Password.Password;

        ConnectMessage.Text = "Connecting…";
        var error = await _api.SignInAsync(username, password);
        if (error is not null)
        {
            SetStatus(false, "Offline");
            ConnectMessage.Text = error;
            return;
        }

        _setup.Username = username;
        _setup.Password = RememberPassword.IsOn ? password : "";
        _setup.StoreName = _api.StoreName ?? "";
        _setup.Save();

        StoreLine.Text = _api.StoreName ?? "Signed in";
        SideStore.Text = _api.StoreName ?? "Signed in";
        ConnectMessage.Text = "Connected.";
        SetStatus(true, "Live");

        _poll.Interval = TimeSpan.FromSeconds(Math.Clamp(_setup.PollSeconds, 5, 300));
        _poll.Start();
        await LoadStoresAsync();
        _serverRoutes = await _api.PrintRoutesAsync();
        await LoadDesignAsync();
        await RefreshOrdersAsync(auto: false);
        await LoadMenuAsync();
    }

    /// <summary>Product → station role ("kitchen"/"bar") drawn by the owner on the web portal.</summary>
    private Dictionary<int, string> _serverRoutes = [];

    /// <summary>Product NAME → menu description, printed under each line of a kitchen ticket.</summary>
    private readonly Dictionary<string, string> _descriptions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Product NAME → menu product id. An order line carries its own row id, NOT the
    /// menu product id, so routing (which is keyed by product id) is matched back through
    /// the name — otherwise every dish falls to the default printer and the printers the
    /// owner ticked are ignored.
    /// </summary>
    private readonly Dictionary<string, int> _menuIdByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The menu product id behind an order line (via its name), or the line id if unknown.</summary>
    private int ProductIdOf(OrderItemDto item) => _menuIdByName.GetValueOrDefault(item.Name, item.Id);

    /// <summary>Product NAME → the full menu item, for translating kitchen-ticket names/descriptions.</summary>
    private readonly Dictionary<string, MenuItemDto> _menuByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The languages the kitchen-ticket picker offers.</summary>
    public sealed record TicketLang(string Code, string Display);
    private static readonly TicketLang[] KitchenLanguages =
    [
        new("", "Default (partner Settings, else as ordered)"),
        new("en", "English"),
        new("ar", "العربية · Arabic"),
        new("fa", "فارسی · Farsi"),
        new("tr", "Türkçe · Turkish"),
        new("ur", "اردو · Urdu"),
        new("hi", "हिन्दी · Hindi"),
    ];

    /// <summary>
    /// Pulls the owner's invoice design and store profile so every bill this till prints
    /// matches, line for line, the receipt the owner laid out in the partner panel.
    /// </summary>
    private async Task LoadDesignAsync()
    {
        _design = await _api.ReceiptDesignAsync();
        var clientUrl = string.IsNullOrWhiteSpace(_setup.ClientUrl) ? "https://orderorange.com" : _setup.ClientUrl;
        var profile = await _api.ProfileAsync();
        _serverKitchenLang = profile?.KitchenLanguage ?? "";
        _store = profile is not null
            ? StoreProfile.From(profile, clientUrl)
            : StoreProfile.Fallback(_api.StoreName ?? _setup.StoreName, clientUrl);
    }

    /// <summary>
    /// Fills the sidebar business switcher. It only appears when the account can work
    /// in more than one business — a single-shop owner never sees it.
    /// </summary>
    private async Task LoadStoresAsync()
    {
        var stores = await _api.MyStoresAsync();
        _switching = true;
        _stores.Clear();
        foreach (var store in stores) _stores.Add(store);
        StoreSwitcher.SelectedItem = _stores.FirstOrDefault(s => s.Id == _api.RestaurantId);
        _switching = false;
        SwitcherPanel.Visibility = _stores.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The owner picked a different business: re-scope the session and reload.</summary>
    private async void StoreSwitcher_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switching) return;
        if (StoreSwitcher.SelectedItem is not StoreSummaryDto store) return;
        if (store.Id == _api.RestaurantId) return;

        var error = await _api.SwitchStoreAsync(store.Id);
        if (error is not null)
        {
            MessageBox.Show(error, "Local Handler");
            _switching = true;   // put the selection back on the store we're still in
            StoreSwitcher.SelectedItem = _stores.FirstOrDefault(s => s.Id == _api.RestaurantId);
            _switching = false;
            return;
        }

        StoreLine.Text = _api.StoreName ?? store.Name;
        SideStore.Text = _api.StoreName ?? store.Name;
        _setup.StoreName = _api.StoreName ?? store.Name;
        _setup.Save();

        _printed.Clear();   // a different business — its orders are unrelated to the last one's
        _receipted.Clear();
        _seenTabLines.Clear(); _tabMemos.Clear();
        _printedLoaded = false;
        _serverRoutes = await _api.PrintRoutesAsync();
        await LoadDesignAsync();
        await RefreshOrdersAsync(auto: false);
        await LoadMenuAsync();
    }

    /// <summary>Starts a second copy of this program on a fresh profile, for another partner.</summary>
    private void OpenAnother_Click(object sender, RoutedEventArgs e)
    {
        var profile = Setup.NextFreeProfile();
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) { MessageBox.Show("Could not find the program file to start.", "Local Handler"); return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"--profile {profile}") { UseShellExecute = false });
            ConnectMessage.Text = $"Opened a new window on profile “{profile}”. Sign it in with the other partner's account.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Local Handler"); }
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _poll.Stop();
        _api.SignOut();
        _orders.Clear();
        _switching = true;
        _stores.Clear();
        _switching = false;
        SwitcherPanel.Visibility = Visibility.Collapsed;
        StoreLine.Text = "Not signed in";
        SideStore.Text = "Not signed in";
        SetStatus(false, "Offline");
    }

    private void SaveConnection_Click(object sender, RoutedEventArgs e)
    {
        _setup.AutoPrintReceipt = AutoReceipt.IsOn;
        _setup.AutoPrintKitchen = AutoKitchen.IsOn;
        _setup.StartWithWindows = AutoStart.IsOn;
        Autostart.Apply(_setup.Profile, _setup.StartWithWindows);
        _setup.KitchenLanguage = KitchenLang.SelectedValue as string ?? "";
        _setup.PollSeconds = int.TryParse(PollSeconds.Text, out var seconds) ? Math.Clamp(seconds, 5, 300) : 15;
        _setup.ApiBaseUrl = ApiUrl.Text.Trim();
        _setup.Save();
        _poll.Interval = TimeSpan.FromSeconds(_setup.PollSeconds);
        ConnectMessage.Text = "Saved.";
    }

    private void SetStatus(bool live, string text)
    {
        StatusDot.Fill = new SolidColorBrush(live ? Color.FromRgb(0x4A, 0xDE, 0x80) : Color.FromRgb(0x8A, 0x7F, 0x70));
        StatusText.Text = text;
    }

    // ---------------- orders ----------------

    private async void RefreshOrders_Click(object sender, RoutedEventArgs e) => await RefreshOrdersAsync(auto: false);

    private async Task RefreshOrdersAsync(bool auto)
    {
        if (!_api.SignedIn) { OrdersHint.Text = "Sign in on the Connection tab first."; return; }

        // The web POS may have handed us an amount for the card terminal — show it big.
        if (await _api.TakeTillChargeAsync() is { } charge)
            new ChargeWindow(charge.Amount, charge.Reference) { Owner = this }.Show();

        var board = await _api.BoardAsync();
        var selectedId = (OrdersList.SelectedItem as OrderRow)?.Order.Id;

        _orders.Clear();
        foreach (var order in board) _orders.Add(new OrderRow(order));
        OrdersHint.Text = $"{board.Count} live · checked {DateTime.Now:HH:mm:ss}";

        if (selectedId is { } id)
        {
            var again = _orders.FirstOrDefault(r => r.Order.Id == id);
            if (again is not null) OrdersList.SelectedItem = again;
        }

        // What this till already printed for this store, remembered across restarts.
        var storeId = _api.RestaurantId ?? 0;
        if (!_printedLoaded)
        {
            _printed.Clear(); _printed.UnionWith(_setup.PrintedFor(storeId));
            _receipted.Clear(); _receipted.UnionWith(_setup.ReceiptedFor(storeId));
            // A till that has never remembered anything for this store treats what is on
            // the board right now as history — it must not reprint the day's orders.
            if (!_setup.PrintedOrders.ContainsKey(storeId))
            {
                var seen = board.Select(o => o.Id).ToList();
                _printed.UnionWith(seen); _receipted.UnionWith(seen);
                _setup.RememberPrinted(storeId, seen);
                _setup.RememberReceipted(storeId, seen);
                _setup.Save();
            }
            var tabsNow = await _api.OpenTabsAsync();
            _seenTabLines.Clear(); _seenTabLines.UnionWith(_setup.TabLinesFor(storeId));
            _tabMemos.Clear(); foreach (var (lid, m) in _setup.TabMemosFor(storeId)) _tabMemos[lid] = m;
            if (!_setup.TicketedTabLines.ContainsKey(storeId))
            {
                var lineIds = tabsNow.SelectMany(t => t.Lines.Select(l => l.Id)).ToList();
                _seenTabLines.UnionWith(lineIds);
                _setup.RememberTabLines(storeId, lineIds);
                _setup.Save();
            }
            _printedLoaded = true;
        }

        // Open tables: a round rung onto a table has no order yet, so print its NEW lines
        // to the kitchen straight away (never the bill — that waits for the table to close).
        if (_setup.AutoPrintKitchen) await PrintTabRoundsAsync(storeId);

        // Two-stage printing:
        //   • the KITCHEN ticket prints the moment an order appears, and
        //   • the CASHIER bill waits until the bill is due — a dine-in/counter order is
        //     paid (table closed), or a delivery/pickup order is Ready or beyond.
        var newTickets = new List<int>();
        var newReceipts = new List<int>();
        foreach (var order in board)
        {
            // A closed table's bill lands on the board as a paid dine-in order — but the
            // kitchen already got every round while the table was open, so it must NOT be
            // ticketed again; only the cashier bill prints for it. Table-QR guest orders
            // (dine-in but unpaid) and delivery/counter orders still ticket on arrival.
            var fromClosedTable = IsDineIn(order)
                && (order.IsPaid || (order.PaymentRef?.StartsWith("TAB-", StringComparison.OrdinalIgnoreCase) ?? false));

            if (fromClosedTable)
            {
                if (_printed.Add(order.Id))   // handle this closed table once
                {
                    // Only the items the kitchen has NOT already had (something rung up at
                    // the close, faster than a round could print) — nothing if all was sent.
                    if (_setup.AutoPrintKitchen && ChangeItems(order) is { Count: > 0 } change)
                    {
                        var extra = order with { Items = change };
                        await PrintTicketsCore(extra, silent: true);
                        newTickets.Add(order.Id);
                    }
                    _kitchenTally.Remove(order.TableName ?? "");   // table is done
                }
            }
            else if (_setup.AutoPrintKitchen && _printed.Add(order.Id)) { await PrintTicketsCore(order, silent: true); newTickets.Add(order.Id); }
            else _printed.Add(order.Id);   // mark seen even when kitchen auto-print is off

            if (_setup.AutoPrintReceipt && !_receipted.Contains(order.Id) && ReadyForReceipt(order) && _setup.Cashier is { } cashier)
            {
                await PrintReceiptPaperAsync(order, cashier);
                _receipted.Add(order.Id);
                newReceipts.Add(order.Id);
            }
        }
        if (newTickets.Count > 0) _setup.RememberPrinted(storeId, newTickets);
        if (newReceipts.Count > 0) _setup.RememberReceipted(storeId, newReceipts);
        if (newTickets.Count > 0 || newReceipts.Count > 0) _setup.Save();
    }

    /// <summary>Orders whose cashier bill has already been printed (persisted per store).</summary>
    private readonly HashSet<int> _receipted = [];

    /// <summary>Open-table line ids already sent to the kitchen (persisted per store).</summary>
    private readonly HashSet<int> _seenTabLines = [];
    private readonly Dictionary<int, TabLineMemo> _tabMemos = [];
    // The kitchen language the PARTNER chose in Settings; it beats the till's own combo.
    private string _serverKitchenLang = "";
    private string EffectiveKitchenLang => _serverKitchenLang.Length > 0 ? _serverKitchenLang : _setup.KitchenLanguage;

    /// <summary>
    /// Table name → (product name → quantity already sent to the kitchen for that table).
    /// Lets a table CLOSE print only the items the kitchen has not seen yet (the "change"),
    /// or nothing when every item was already sent while the table was open.
    /// </summary>
    private readonly Dictionary<string, Dictionary<string, int>> _kitchenTally = new(StringComparer.OrdinalIgnoreCase);

    private void TallyKitchen(string? table, string name, int qty)
    {
        var t = table ?? "";
        if (!_kitchenTally.TryGetValue(t, out var m)) _kitchenTally[t] = m = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// Prints a kitchen ticket for every NEW line on an open table — the round just rung up —
    /// routed to the same station printers as an order. The bill is never printed here.
    /// </summary>
    private async Task PrintTabRoundsAsync(int storeId)
    {
        var tabs = await _api.OpenTabsAsync();
        if (tabs is null) return;   // server unreachable this time round — keep what we know
        var rounds = TabRounds.Diff(tabs, _seenTabLines, _tabMemos, out var memosChanged);
        var freshlySeen = new List<int>();
        foreach (var round in rounds)
        {
            TillLog.Write($"open invoice {round.Tab.Id} ({round.Tab.TableName}): {(round.Edited ? "edit" : "new")} round, " +
                string.Join(", ", round.Items.Select(i => round.Changes.TryGetValue(i.Id, out var c) ? $"{i.Name} {c.From}→{c.To}" : $"{i.Name} ×{i.Quantity}")));
            var pseudo = TabToOrder(round.Tab, round.Items);
            await PrintTicketsCore(pseudo, silent: true, round.Edited ? round.Changes : null);
            foreach (var it in round.Items)
            {
                if (_seenTabLines.Add(it.Id)) freshlySeen.Add(it.Id);
                // The close-time delta ticket counts what the kitchen has really been given.
                var delta = round.Changes.TryGetValue(it.Id, out var c) ? c.Delta : it.Quantity;
                TallyKitchen(round.Tab.TableName, it.Name, delta);
            }
        }
        if (freshlySeen.Count > 0) _setup.RememberTabLines(storeId, freshlySeen);
        if (memosChanged) _setup.ReplaceTabMemos(storeId, _tabMemos);
        if (freshlySeen.Count > 0 || memosChanged) _setup.Save();
    }

    /// <summary>Wraps an open table's new lines as an order, only so the ticket printer can route them.</summary>
    private OrderDto TabToOrder(StoreTabDto tab, List<OrderItemDto> items)
    {
        return new OrderDto(
            Id: tab.Id,
            Number: tab.Id.ToString(),   // the open table's invoice number, as the POS shows it
            Status: OrderStatus.Pending, PlacedAt: DateTime.Now,
            CustomerId: 0, CustomerName: tab.CustomerName ?? tab.GuestName ?? "", CustomerPhone: "",
            RestaurantId: _api.RestaurantId ?? 0, RestaurantName: "", RestaurantLogoEmoji: "",
            RestaurantArea: "", RestaurantPhone: "",
            DriverId: null, DriverName: null, DriverPhone: null,
            DeliveryAddress: $"Dine-in — {tab.TableName}", DeliveryLat: null, DeliveryLng: null,
            PaymentMethod: PaymentMethod.CashOnDelivery,
            Subtotal: 0, DeliveryFee: 0, ServiceFee: 0, Discount: 0, Total: 0,
            Notes: null, CancelReason: null, EstimatedMinutes: 0, IsPaid: false, PaymentRef: null,
            HasReview: false, Items: items, History: [],
            TableName: tab.TableName);
    }

    /// <summary>True once the remembered printed-lists for the current store have been read.</summary>
    private bool _printedLoaded;
    private System.Windows.Threading.DispatcherTimer? _updateTimer;

    /// <summary>
    /// When the cashier bill is due: a dine-in or counter order once it is paid (the table
    /// closed / counter settled), and a delivery or pickup order once it is Ready or beyond.
    /// </summary>
    private static bool IsDineIn(OrderDto o) =>
        !string.IsNullOrEmpty(o.TableName)
        || (o.DeliveryAddress?.StartsWith("Dine-in", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool ReadyForReceipt(OrderDto o)
    {
        var dineIn = IsDineIn(o);
        var counter = !dineIn && string.Equals(o.DeliveryAddress, "Counter", StringComparison.OrdinalIgnoreCase);
        var ready = o.Status is OrderStatus.Ready or OrderStatus.PickedUp or OrderStatus.OnTheWay or OrderStatus.Delivered;
        return (dineIn || counter) ? (o.IsPaid || ready) : ready;
    }

    private void OrdersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OrdersList.SelectedItem is not OrderRow row) { Preview.Text = ""; return; }
        var station = _setup.Cashier ?? new Station { Columns = 42 };
        Preview.Text = PrintService.BuildReceipt(row.Order, station, _store, _design).ToPreview();
    }

    private OrderDto? Selected => (OrdersList.SelectedItem as OrderRow)?.Order;

    private void PrintReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } order) PrintReceipt(order, silent: false);
    }

    private async void PrintReceipt(OrderDto order, bool silent)
    {
        var station = _setup.Cashier;
        if (station is null)
        {
            if (!silent) MessageBox.Show("No cashier station is set up yet.", "Local Handler");
            return;
        }
        var error = await PrintReceiptPaperAsync(order, station);
        if (error is not null && !silent) MessageBox.Show(error, "Printing failed");
    }

    /// <summary>
    /// Prints the customer's bill as the SAME HTML the web designer shows (via WebView2),
    /// so the paper matches the receipt design exactly. Falls back to the built-in text
    /// renderer only if WebView2 cannot print. Null on success, else the reason.
    /// </summary>
    private async Task<string?> PrintReceiptPaperAsync(OrderDto order, Station station)
    {
        // The kitchen language is for the kitchen only: the bill prints the names exactly as ordered.
        // The designed bill only. A failed print is reported, never replaced by the plain
        // text layout — one paper per job, exactly as designed, or nothing.
        if (string.IsNullOrWhiteSpace(station.PrinterName)) return "No printer chosen for the cashier station.";
        try
        {
            _webPrinter ??= new WebReceiptPrinter(this);
            var html = HtmlReceipt.Build(order, _store, _design);
            var err = await _webPrinter.PrintAsync(html, station.PrinterName, _design.PaperWidth == 58 ? 58 : 80);
            if (err is not null) TillLog.Write($"bill #{order.Number} on [{station.PrinterName}] failed: {err}");
            return err;
        }
        catch (Exception ex)
        {
            TillLog.Write($"bill #{order.Number} on [{station.PrinterName}] threw: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>Prints a kitchen/bar ticket as large, legible HTML (WebView2), text renderer as fallback.</summary>
    private async Task<string?> PrintTicketPaperAsync(OrderDto order, Station station, List<OrderItemDto> items,
        IReadOnlyDictionary<int, LineChange>? changes = null)
    {
        var (names, descs) = TicketText(items);
        var strings = TicketStrings.For(EffectiveKitchenLang);
        if (string.IsNullOrWhiteSpace(station.PrinterName)) return $"No printer chosen for {station.Name}.";
        try
        {
            _webPrinter ??= new WebReceiptPrinter(this);
            var html = HtmlReceipt.BuildTicket(order, items, descs, _design.PaperWidth == 58 ? 58 : 80, names, changes, strings);
            var err = await _webPrinter.PrintAsync(html, station.PrinterName, _design.PaperWidth == 58 ? 58 : 80);
            if (err is not null)
            {
                StatusText.Text = $"{station.Name}: ticket failed ({err}).";
                TillLog.Write($"ticket #{order.Number} on [{station.PrinterName}] failed: {err}");
            }
            return err;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"{station.Name}: ticket failed ({ex.Message}).";
            TillLog.Write($"ticket #{order.Number} on [{station.PrinterName}] threw: {ex.Message}");
            return ex.Message;
        }
    }

    /// <summary>
    /// Resolves each line's kitchen-ticket name and description in the shop's chosen kitchen
    /// language (falls back to the name the order carries when there is no translation).
    /// </summary>
    private (Dictionary<string, string> Names, Dictionary<string, string> Descs) TicketText(IEnumerable<OrderItemDto> items)
    {
        var lang = EffectiveKitchenLang;
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var descs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (_menuByName.TryGetValue(it.Name, out var m))
            {
                names[it.Name] = string.IsNullOrEmpty(lang) ? it.Name : m.NameFor(lang);
                var d = string.IsNullOrEmpty(lang) ? m.Description : (m.Descriptions?.GetValueOrDefault(lang) ?? m.Description);
                if (!string.IsNullOrWhiteSpace(d)) descs[it.Name] = d;
            }
            else if (_descriptions.TryGetValue(it.Name, out var d0) && !string.IsNullOrWhiteSpace(d0))
            {
                descs[it.Name] = d0;
            }
        }
        return (names, descs);
    }

    private void PrintTickets_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } order) PrintTickets(order, silent: false);
    }

    /// <summary>Every printer a product is routed to: local ticks win, then the web role map, then category/default.</summary>
    private List<Station> ResolveStations(OrderItemDto item)
    {
        var pid = ProductIdOf(item);
        var stations = _setup.ProductStations.TryGetValue(pid, out var own) && own.Count > 0
            ? _setup.StationsFor(pid, CategoryOf(item.Name))
            : [];
        if (stations.Count == 0 && _serverRoutes.TryGetValue(pid, out var role))
        {
            var wanted = role == "bar" ? Models.StationRole.Bar : Models.StationRole.Kitchen;
            var s = _setup.Stations.FirstOrDefault(x => x.Enabled && x.Role == wanted)
                 ?? _setup.Stations.FirstOrDefault(x => x.Enabled && x.Role is Models.StationRole.Kitchen or Models.StationRole.Bar);
            if (s is not null) stations = [s];
        }
        if (stations.Count == 0) stations = _setup.StationsFor(pid, CategoryOf(item.Name));
        return stations;
    }

    /// <summary>
    /// Prints an order to exactly the printers its products are routed to — ONE paper per
    /// printer. A cashier printer gets the full designed bill; a kitchen/bar printer gets a
    /// work ticket of its own lines. If the bill is wanted but no cashier printer is ticked,
    /// it falls back to the default cashier — so a printer is never doubled up.
    /// </summary>
    private async Task PrintForOrder(OrderDto order, bool silent, bool receipt, bool tickets)
    {
        var byStation = new Dictionary<string, (Station Station, List<OrderItemDto> Items)>();
        foreach (var item in order.Items)
            foreach (var station in ResolveStations(item))
            {
                if (!byStation.TryGetValue(station.Id, out var bucket))
                    byStation[station.Id] = bucket = (station, []);
                bucket.Items.Add(item);
            }

        var anyCashierTicked = byStation.Values.Any(b => b.Station.Role == Models.StationRole.Cashier);
        var errors = new List<string>();

        foreach (var (station, items) in byStation.Values)
        {
            if (station.Role == Models.StationRole.Cashier)
            {
                if (!receipt) continue;   // the bill is off
                var e = await PrintReceiptPaperAsync(order, station);
                if (e is not null) errors.Add($"{station.Name}: {e}");
            }
            else
            {
                if (!tickets) continue;   // kitchen tickets are off
                var e = await PrintTicketPaperAsync(order, station, items);
                if (e is not null) errors.Add($"{station.Name}: {e}");
            }
        }

        // The bill still prints even when nothing was routed to a cashier printer.
        if (receipt && !anyCashierTicked && _setup.Cashier is { } fallback)
        {
            var e = await PrintReceiptPaperAsync(order, fallback);
            if (e is not null) errors.Add($"{fallback.Name}: {e}");
        }

        if (errors.Count > 0 && !silent) MessageBox.Show(string.Join("\n", errors), "Printing failed");
    }

    /// <summary>Manual "Print tickets": the kitchen/bar work tickets only (the bill has its own button).</summary>
    private async void PrintTickets(OrderDto order, bool silent) => await PrintTicketsCore(order, silent);

    /// <summary>Prints the kitchen/bar work tickets for an order — one per station, no bill.</summary>
    private async Task PrintTicketsCore(OrderDto order, bool silent, IReadOnlyDictionary<int, LineChange>? changes = null)
    {
        var byStation = new Dictionary<string, (Station Station, List<OrderItemDto> Items)>();
        foreach (var item in order.Items)
            foreach (var station in ResolveStations(item).Where(s => s.Role != Models.StationRole.Cashier))
            {
                if (!byStation.TryGetValue(station.Id, out var bucket))
                    byStation[station.Id] = bucket = (station, []);
                bucket.Items.Add(item);
            }

        if (byStation.Count == 0)
        {
            if (!silent) MessageBox.Show("No kitchen or bar station matched these products.", "Local Handler");
            return;
        }
        // One physical printer, one ticket: two stations pointed at the same printer
        // (or "select all" on a product) merge instead of printing the same paper twice.
        var byPrinter = byStation.Values
            .GroupBy(v => string.IsNullOrWhiteSpace(v.Station.PrinterName) ? $"#{v.Station.Id}" : v.Station.PrinterName.Trim(),
                     StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First().Station, Items: g.SelectMany(v => v.Items).GroupBy(i => i.Id).Select(x => x.First()).ToList()))
            .ToList();
        foreach (var (station, items) in byPrinter)
        {
            var error = await PrintTicketPaperAsync(order, station, items, changes);
            TillLog.Write($"ticket #{order.Number} → {station.Name} [{station.PrinterName}] {items.Count} line(s){(changes is { Count: > 0 } ? " EDITED" : "")}{(error is null ? "" : " FAILED: " + error)}");
            if (error is not null && !silent) MessageBox.Show($"{station.Name}: {error}", "Printing failed");
        }
    }

    /// <summary>
    /// The board's order lines carry the product NAME, not its category, so routing by
    /// category needs the menu that was loaded on the routing tab.
    /// </summary>
    private int CategoryOf(string productName) =>
        _routing.FirstOrDefault(r => string.Equals(r.ProductName, productName, StringComparison.OrdinalIgnoreCase))?.CategoryId ?? 0;

    private async void MarkPaid_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } order) return;
        var error = await _api.PaidAsync(order.Id);
        if (error is not null) { MessageBox.Show(error, "Local Handler"); return; }
        await RefreshOrdersAsync(auto: false);
    }

    private async void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } order) return;
        var error = await _api.AcceptAsync(order.Id);
        if (error is not null) MessageBox.Show(error, "Local Handler");
        await RefreshOrdersAsync(auto: false);
    }

    private async void Ready_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } order) return;
        // A kitchen that never pressed "preparing" still expects "ready" to work.
        await _api.PreparingAsync(order.Id);
        var error = await _api.ReadyAsync(order.Id);
        if (error is not null) MessageBox.Show(error, "Local Handler");
        await RefreshOrdersAsync(auto: false);
    }

    // ---------------- printers ----------------

    private void AddStation_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(StationName.Text) || StationPrinter.SelectedItem is null)
        {
            MessageBox.Show("Name the station and choose a printer.", "Local Handler");
            return;
        }

        _stations.Add(new Station
        {
            Name = StationName.Text.Trim(),
            PrinterName = StationPrinter.SelectedItem.ToString()!,
            Role = (StationRole)(StationRole.SelectedItem ?? Models.StationRole.Kitchen),
            Columns = int.TryParse(StationColumns.Text, out var columns) ? columns : 42,
            Copies = int.TryParse(StationCopies.Text, out var copies) ? copies : 1
        });
        StationName.Text = "";
        SaveStations();
    }

    private void RemoveStation_Click(object sender, RoutedEventArgs e)
    {
        if (StationsGrid.SelectedItem is Station station) _stations.Remove(station);
        SaveStations();
    }

    private void SaveStations_Click(object sender, RoutedEventArgs e) => SaveStations();

    private void SaveStations()
    {
        _setup.Stations = [.. _stations];
        _setup.Save();
        RefreshTicketStations();
        if (_routing.Count > 0) _ = LoadMenuAsync();   // rows carry one tick per station — rebuild them
    }

    private void TestPrint_Click(object sender, RoutedEventArgs e)
    {
        if (StationsGrid.SelectedItem is not Station station)
        {
            MessageBox.Show("Pick a station to test.", "Local Handler");
            return;
        }
        var error = PrintService.Print(station, PrintService.Sample(station));
        MessageBox.Show(error ?? "Sent to the printer.", "Local Handler");
    }

    // ---------------- routing ----------------

    private async void LoadMenu_Click(object sender, RoutedEventArgs e) => await LoadMenuAsync();

    private void RoutingBusyShow(string text)
    {
        RoutingBusyText.Text = text;
        RoutingBusy.Visibility = Visibility.Visible;
    }

    private async Task LoadMenuAsync()
    {
        if (!_api.SignedIn) return;
        RoutingBusyShow("Loading products…");
        try { await LoadMenuCoreAsync(); }
        finally { RoutingBusy.Visibility = Visibility.Collapsed; }
    }

    private async Task LoadMenuCoreAsync()
    {
        var menu = await _api.MenuAsync();
        _routing.Clear();
        _descriptions.Clear();
        _menuIdByName.Clear();
        _menuByName.Clear();
        foreach (var category in menu)
        {
            foreach (var item in category.Items)
            {
                _descriptions[item.Name] = item.Description ?? "";
                _menuIdByName[item.Name] = item.Id;
                _menuByName[item.Name] = item;
                // The product's own rule wins; otherwise it inherits its category's set.
                var ticked = _setup.ProductStations.TryGetValue(item.Id, out var own) && own.Count > 0 ? own
                           : _setup.CategoryStations.GetValueOrDefault(category.Id) ?? [];
                _routing.Add(new RoutingRow(TicketStations, ticked)
                {
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    ProductId = item.Id,
                    ProductName = item.Name,
                });
            }
        }
        RefreshRoutingStats();
        RefreshMassTicks();
    }

    // ---- bulk assignment: the printer picked in the bar goes to every row, or the highlighted ones ----

    private void RefreshRoutingStats()
    {
        var missing = _routing.Count(r => r.IsUnassigned);
        StatLine.Text = _routing.Count == 0 ? ""
            : missing == 0 ? $"{_routing.Count} products · all have a printer"
            : $"{_routing.Count} products · {missing} without a printer";
    }

    private void RoutingSearch_TextChanged(object sender, TextChangedEventArgs e) =>
        CollectionViewSource.GetDefaultView(_routing).Refresh();

    // ---- the "mass" ticks: one per printer on the All-products bar (Tag "*") and in each category header (Tag = name).
    //      Ticked = every product in scope already prints there. Clicking ticks/unticks the whole scope.

    private readonly List<CheckBox> _massTicks = [];

    private IEnumerable<RoutingRow> Scope(string tag) =>
        tag == "*" ? _routing : _routing.Where(r => r.CategoryName == tag);

    private void MassTick_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box) return;
        if (!_massTicks.Contains(box)) _massTicks.Add(box);
        SyncMassTick(box);
    }

    private void SyncMassTick(CheckBox box)
    {
        if (box.Tag is not string tag || box.DataContext is not Station station) return;
        var rows = Scope(tag).ToList();
        box.IsChecked = rows.Count > 0 && rows.All(r => r.Has(station.Id));
    }

    private void RefreshMassTicks()
    {
        _massTicks.RemoveAll(b => !b.IsLoaded);
        foreach (var box in _massTicks.ToList()) SyncMassTick(box);
    }

    private void MassTick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string tag, DataContext: Station station } box) return;
        if (_routing.Count == 0) { box.IsChecked = false; MessageBox.Show("Load the menu first.", "Local Handler"); return; }
        var on = box.IsChecked == true;
        foreach (var row in Scope(tag)) row.Set(station.Id, on);
        RefreshRoutingStats();
        RefreshMassTicks();
    }

    private void SaveRouting_Click(object sender, RoutedEventArgs e)
    {
        _setup.ProductStations.Clear();
        _setup.ProductStation.Clear();          // the old single-printer file format is retired
        foreach (var row in _routing)
        {
            var ids = row.StationIds.ToList();
            if (ids.Count > 0) _setup.ProductStations[row.ProductId] = ids;
        }

        // The printers EVERY product of a category shares become the category rule, so
        // a NEW dish added later on the web lands on those printers without being touched here.
        _setup.CategoryStations.Clear();
        _setup.CategoryStation.Clear();
        foreach (var group in _routing.GroupBy(r => r.CategoryId))
        {
            var common = group.Select(r => r.StationIds.ToHashSet())
                              .Aggregate((a, b) => { a.IntersectWith(b); return a; });
            if (common.Count > 0) _setup.CategoryStations[group.Key] = [.. common];
        }

        _setup.Save();
        MessageBox.Show("Routing saved.", "Local Handler");
    }

    // ---------------- tray: the till lives next to the clock ----------------

    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exiting;

    /// <summary>Start hidden: only the tray icon shows, the window opens on a click.</summary>
    public void ShowInTray()
    {
        EnsureTray();
        WindowState = WindowState.Minimized;
        ShowInTaskbar = false;
        Show();
        Hide();
    }

    private void EnsureTray()
    {
        if (_tray is not null) return;
        var label = string.IsNullOrEmpty(_setup.Profile) ? "OrderOrange Till" : $"OrderOrange Till · {_setup.Profile}";
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => { _exiting = true; Close(); });
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = label,
            Visible = true,
            ContextMenuStrip = menu,
        };
        try
        {
            var ico = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (ico is not null) _tray.Icon = new System.Drawing.Icon(ico.Stream);
        }
        catch { _tray.Icon = System.Drawing.SystemIcons.Application; }
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        _tray.Click += (_, e) => { if (e is System.Windows.Forms.MouseEventArgs m && m.Button == System.Windows.Forms.MouseButtons.Left) RestoreFromTray(); };
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        // Minimise = go to the tray; the till keeps polling and printing there.
        if (WindowState == WindowState.Minimized && _setup.StartWithWindows)
        {
            EnsureTray();
            Hide();
            ShowInTaskbar = false;
        }
        base.OnStateChanged(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The X hides the till instead of stopping it — a closed till prints nothing.
        // Exit is on the tray menu.
        if (!_exiting && _setup.StartWithWindows)
        {
            e.Cancel = true;
            EnsureTray();
            Hide();
            ShowInTaskbar = false;
            _tray?.ShowBalloonTip(3000, "OrderOrange Till", "Still running here — orders keep printing. Right-click → Exit to stop.", System.Windows.Forms.ToolTipIcon.Info);
            return;
        }
        _poll.Stop();
        _setup.Save();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        base.OnClosing(e);
    }

    // ---------------- view rows ----------------

    /// <summary>One card in the live order list.</summary>
    public sealed class OrderRow(OrderDto order)
    {
        public OrderDto Order { get; } = order;

        public string Number => $"#{Order.Number}";
        public string Customer => string.IsNullOrWhiteSpace(Order.CustomerName) ? "Guest" : Order.CustomerName;
        public string Amount => $"{Order.Total:0.###} OMR";
        public string Meta => $"{Order.PlacedAt:HH:mm} · {Order.Status} · {Order.Items.Sum(i => i.Quantity)} items";
        public bool Paid => Order.IsPaid;

        /// <summary>Up to two initials for the avatar bubble.</summary>
        public string Initials
        {
            get
            {
                var parts = Customer.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "?";
                var letters = parts.Take(2).Select(p => char.ToUpperInvariant(p[0]));
                return string.Concat(letters);
            }
        }

        // Kept so any older binding still resolves.
        public string Headline => $"{Number} · {Customer} · {Amount}";
        public string Detail => Meta + (Paid ? " · PAID" : "");
    }

    /// <summary>One product and the set of stations it prints at — one tick per station.</summary>
    public sealed class RoutingRow : INotifyPropertyChanged
    {
        /// <summary>Raised by any row when a tick flips, so the window can refresh counts and header ticks.</summary>
        public static Action<RoutingRow>? Changed { get; set; }

        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";

        public List<StationChoice> Choices { get; }

        public RoutingRow(IEnumerable<Station> stations, IEnumerable<string> ticked)
        {
            var on = ticked.ToHashSet();
            Choices = stations.Select(s => new StationChoice(s, on.Contains(s.Id), this)).ToList();
        }

        public IEnumerable<string> StationIds => Choices.Where(c => c.IsChecked).Select(c => c.Station.Id);
        public bool Has(string stationId) => Choices.Any(c => c.IsChecked && c.Station.Id == stationId);
        public bool IsUnassigned => !Choices.Any(c => c.IsChecked);

        public void Set(string stationId, bool on)
        {
            var choice = Choices.FirstOrDefault(c => c.Station.Id == stationId);
            if (choice is not null) choice.IsChecked = on;
        }

        internal void Flipped()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUnassigned)));
            Changed?.Invoke(this);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>One tick box on a routing row: this product prints at this station, or not.</summary>
    public sealed class StationChoice(Station station, bool isChecked, RoutingRow owner) : INotifyPropertyChanged
    {
        public Station Station { get; } = station;

        private bool _isChecked = isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                owner.Flipped();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
