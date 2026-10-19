using OrderOrange.AndroidTill.Localization;
using OrderOrange.AndroidTill.Models;
using OrderOrange.AndroidTill.Printing;
using OrderOrange.AndroidTill.Services;

namespace OrderOrange.AndroidTill.Pages;

/// <summary>One printer: how to reach it, what it is for, its paper, copies and cutter.</summary>
public sealed class PrinterEditPage : ContentPage
{
    private readonly PrinterConfig? _existing;
    private readonly Entry _name = new();
    private readonly Picker _connection = new();
    private readonly Entry _host = new() { Keyboard = Keyboard.Numeric, Placeholder = "192.168.1.50" };
    private readonly Entry _port = new() { Keyboard = Keyboard.Numeric };
    private readonly Entry _serial = new() { Placeholder = "/dev/ttyS1" };
    private readonly Label _usbLabel = new() { TextColor = Colors.Gray, FontSize = 13 };
    private readonly Picker _role = new();
    private readonly Picker _paper = new();
    private readonly Stepper _copies = new() { Minimum = 1, Maximum = 5, Increment = 1 };
    private readonly Label _copiesLabel = new() { VerticalOptions = LayoutOptions.Center, WidthRequest = 28, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Switch _cut = new();
    private readonly Picker _cutMode = new();
    private readonly VerticalStackLayout _cutRows = new();
    private readonly Switch _enabled = new();
    private readonly VerticalStackLayout _wifiRows = new();
    private readonly VerticalStackLayout _serialRows = new();
    private readonly VerticalStackLayout _usbRows = new();
    private readonly VerticalStackLayout _btRows = new();
    private readonly Label _btLabel = new() { TextColor = Colors.Gray, FontSize = 13, FlowDirection = FlowDirection.LeftToRight };
    private readonly Label _builtInHint = new() { TextColor = Colors.Gray, FontSize = 13 };
    private int _usbVendor, _usbProduct;
    private string _btAddress = "", _btName = "";

    private static readonly PrinterConnection[] Connections =
        [PrinterConnection.BuiltIn, PrinterConnection.Bluetooth, PrinterConnection.WiFi, PrinterConnection.Usb, PrinterConnection.Serial];
    private static readonly PrinterRole[] Roles = [PrinterRole.Cashier, PrinterRole.Kitchen, PrinterRole.Bar];
    private static readonly int[] Papers = [58, 80];
    private static readonly CutMode[] CutModes = [CutMode.Partial, CutMode.Full, CutMode.PartialA, CutMode.FullA, CutMode.EscI, CutMode.EscM];

    /// <param name="existing">The printer being edited, or null for a new one.</param>
    /// <param name="template">For a new printer: a pre-filled form (what "Find printers" found).</param>
    public PrinterEditPage(PrinterConfig? existing, PrinterConfig? template = null)
    {
        _existing = existing;
        var p = existing ?? template ?? new PrinterConfig();
        Title = L.T("printer");
        FlowDirection = Ui.Flow;

        foreach (var c in Connections) _connection.Items.Add(L.T(c switch
        {
            PrinterConnection.BuiltIn => "builtIn", PrinterConnection.Bluetooth => "bluetooth", PrinterConnection.WiFi => "wifi",
            PrinterConnection.Usb => "usb", _ => "serial",
        }));
        foreach (var r in Roles) _role.Items.Add(L.T(r switch { PrinterRole.Cashier => "cashier", PrinterRole.Kitchen => "kitchen", _ => "bar" }));
        foreach (var w in Papers) _paper.Items.Add($"{w} mm");

        _name.Text = p.Name;
        _connection.SelectedIndex = Array.IndexOf(Connections, p.Connection);
        _host.Text = p.Host;
        _port.Text = p.Port.ToString();
        _serial.Text = p.SerialPath;
        _usbVendor = p.UsbVendorId; _usbProduct = p.UsbProductId;
        _btAddress = p.BluetoothAddress; _btName = p.BluetoothName;
        _role.SelectedIndex = Array.IndexOf(Roles, p.Role);
        _paper.SelectedIndex = Math.Max(0, Array.IndexOf(Papers, p.Paper));
        _copies.Value = Math.Clamp(p.Copies, 1, 5);
        _copiesLabel.Text = ((int)_copies.Value).ToString();
        _copies.ValueChanged += (_, e) => _copiesLabel.Text = ((int)e.NewValue).ToString();
        _cut.IsToggled = p.Cut;
        foreach (var m in CutModes) _cutMode.Items.Add(L.T(m switch
        {
            CutMode.Full => "cutFull", CutMode.PartialA => "cutPartialA", CutMode.FullA => "cutFullA",
            CutMode.EscI => "cutEscI", CutMode.EscM => "cutEscM", _ => "cutPartial",
        }));
        _cutMode.SelectedIndex = Math.Max(0, Array.IndexOf(CutModes, p.CutMode));
        _cutRows.Children.Add(Ui.Caption(L.T("cutMode")));
        _cutRows.Children.Add(_cutMode);
        _cutRows.Children.Add(new Label { Text = L.T("cutHint"), TextColor = Colors.Gray, FontSize = 13 });
        _cutRows.IsVisible = p.Cut;
        _cut.Toggled += (_, e) => _cutRows.IsVisible = e.Value;
        _enabled.IsToggled = p.Enabled;
        _builtInHint.Text = L.T("builtInHint");
        UsbLabel();
        BtLabel();

        _wifiRows.Children.Add(Ui.Caption(L.T("host")));
        _wifiRows.Children.Add(_host);
        _wifiRows.Children.Add(Ui.Secondary(L.T("findOnNetwork"), FindOnNetworkAsync));
        _wifiRows.Children.Add(Ui.Caption(L.T("port")));
        _wifiRows.Children.Add(_port);
        _serialRows.Children.Add(Ui.Caption(L.T("serialPath")));
        _serialRows.Children.Add(_serial);
        _usbRows.Children.Add(Ui.Caption(L.T("usbDevice")));
        _usbRows.Children.Add(_usbLabel);
        _usbRows.Children.Add(Ui.Secondary(L.T("usbDevice") + "…", PickUsbAsync));
        _btRows.Children.Add(Ui.Caption(L.T("btDevice")));
        _btRows.Children.Add(_btLabel);
        _btRows.Children.Add(Ui.Secondary(L.T("pickBt"), PickBluetoothAsync));
        _btRows.Children.Add(new Label { Text = L.T("btHint"), TextColor = Colors.Gray, FontSize = 13 });

        _connection.SelectedIndexChanged += (_, _) =>
        {
            ShowRows();
            // A NEW built-in printer is the cashier's in practice; the Kitchen default produced a
            // second "Built-in printer" entry that printed kitchen tickets on the receipt printer.
            if (existing is null && SelectedConnection == PrinterConnection.BuiltIn)
                _role.SelectedIndex = Array.IndexOf(Roles, PrinterRole.Cashier);
        };
        ShowRows();

        var copiesRow = new HorizontalStackLayout { Spacing = 8, Children = { _copiesLabel, _copies } };
        var actions = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Ui.Secondary(L.T("testPrint"), TestAsync),
                Ui.Primary(L.T("save"), SaveAsync),
            },
        };
        if (existing is not null) actions.Children.Add(Ui.Danger(L.T("delete"), DeleteAsync));

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 16,
                Spacing = 4,
                Children =
                {
                    Ui.Caption(L.T("name")), _name,
                    Ui.Caption(L.T("connection")), _connection,
                    _builtInHint, _btRows, _wifiRows, _serialRows, _usbRows,
                    Ui.Caption(L.T("role")), _role,
                    Ui.Caption(L.T("paper")), _paper,
                    Ui.Row(L.T("copies"), copiesRow),
                    Ui.Row(L.T("cut"), _cut),
                    _cutRows,
                    Ui.Row(L.T("enabled"), _enabled),
                    new BoxView { HeightRequest = 8, Color = Colors.Transparent },
                    actions,
                },
            },
        };
    }

    private PrinterConnection SelectedConnection => Connections[Math.Max(0, _connection.SelectedIndex)];

    private void ShowRows()
    {
        var c = SelectedConnection;
        _builtInHint.IsVisible = c == PrinterConnection.BuiltIn;
        _wifiRows.IsVisible = c == PrinterConnection.WiFi;
        _serialRows.IsVisible = c == PrinterConnection.Serial;
        _usbRows.IsVisible = c == PrinterConnection.Usb;
        _btRows.IsVisible = c == PrinterConnection.Bluetooth;
    }

    private void UsbLabel() =>
        _usbLabel.Text = _usbVendor == 0 ? L.T("anyUsbPrinter") : $"USB {_usbVendor:X4}:{_usbProduct:X4}";

    private void BtLabel() =>
        _btLabel.Text = _btAddress.Length == 0 ? L.T("noBtChosen") : (_btName.Length > 0 ? $"{_btName} ({_btAddress})" : _btAddress);

    /// <summary>Lists the devices paired in Android Settings; the terminal's own printer is "InnerPrinter".</summary>
    private async Task PickBluetoothAsync()
    {
        if (!await BluetoothPrinterTransport.EnsurePermissionAsync()) { await Ui.Alert(L.T("btNoPermission")); return; }
        if (BluetoothPrinterTransport.Adapter is null) { await Ui.Alert(L.T("btNone")); return; }
        if (!BluetoothPrinterTransport.Adapter.IsEnabled) { await Ui.Alert(L.T("btOff")); return; }
        var devices = BluetoothPrinterTransport.Bonded()
            .OrderByDescending(d => (d.Name ?? "").Contains("InnerPrinter", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(BluetoothPrinterTransport.LooksLikePrinter).ToList();
        if (devices.Count == 0) { await Ui.Alert(L.T("btNotPaired")); return; }
        var options = devices.Select(BluetoothPrinterTransport.Label).ToArray();
        var choice = await DisplayActionSheet(L.T("btDevice"), L.T("cancel"), null, options);
        var at = Array.IndexOf(options, choice ?? "");
        if (at < 0) return;
        _btAddress = devices[at].Address ?? "";
        _btName = devices[at].Name ?? "";
        BtLabel();
        var inner = _btName.Contains("InnerPrinter", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = inner ? L.T("builtIn") : _btName;
        if (inner && _existing is null) { _role.SelectedIndex = Array.IndexOf(Roles, PrinterRole.Cashier); _paper.SelectedIndex = Array.IndexOf(Papers, 58); _cut.IsToggled = false; }
    }

    private async Task PickUsbAsync()
    {
        var devices = UsbPrinterTransport.Devices();
        var options = new List<string> { L.T("anyUsbPrinter") };
        options.AddRange(devices.Select(UsbPrinterTransport.Label));
        var choice = await DisplayActionSheet(L.T("usbDevice"), L.T("cancel"), null, options.ToArray());
        var at = options.IndexOf(choice ?? "");
        if (at < 0) return;
        if (at == 0) { _usbVendor = 0; _usbProduct = 0; }
        else { _usbVendor = devices[at - 1].VendorId; _usbProduct = devices[at - 1].ProductId; }
        UsbLabel();
    }

    /// <summary>Sweeps the shop Wi-Fi for printers on the port typed above and fills the IP from the pick.</summary>
    private async Task FindOnNetworkAsync()
    {
        var port = int.TryParse(_port.Text, out var p) && p > 0 ? p : 9100;
        var wait = FlashTitleAsync(L.T("scanningNet0"));
        List<PrinterDiscovery.Found> found;
        try { found = await PrinterDiscovery.ScanNetworkAsync(null, CancellationToken.None, port); }
        catch (Exception ex) { await wait; await Ui.Alert(ex.Message); return; }
        await wait;
        if (found.Count == 0) { await Ui.Alert(L.T("foundNone")); return; }
        var options = found.Select(f => f.Detail).ToArray();
        var choice = await DisplayActionSheet(L.T("networkPrinter"), L.T("cancel"), null, options);
        var at = Array.IndexOf(options, choice ?? "");
        if (at < 0) return;
        _host.Text = found[at].Config.Host;
        _port.Text = found[at].Config.Port.ToString();
        if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = found[at].Config.Name;
    }

    /// <summary>Shows "Scanning…" in the title bar while the sweep runs; awaiting the result restores it.</summary>
    private Task FlashTitleAsync(string text)
    {
        var old = Title;
        Title = text;
        return Task.Run(async () => { await Task.Delay(2500); Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => Title = old); });
    }

    /// <summary>What the form says right now, keeping the id of the printer being edited.</summary>
    private PrinterConfig Collect() => new()
    {
        Id = _existing?.Id ?? Guid.NewGuid().ToString("N")[..8],
        Name = (_name.Text ?? "").Trim(),
        Connection = SelectedConnection,
        Host = (_host.Text ?? "").Trim(),
        Port = int.TryParse(_port.Text, out var port) && port > 0 ? port : 9100,
        SerialPath = (_serial.Text ?? "").Trim(),
        UsbVendorId = _usbVendor,
        UsbProductId = _usbProduct,
        BluetoothAddress = _btAddress,
        BluetoothName = _btName,
        Role = Roles[Math.Max(0, _role.SelectedIndex)],
        Paper = Papers[Math.Max(0, _paper.SelectedIndex)],
        Copies = (int)_copies.Value,
        Cut = _cut.IsToggled,
        CutMode = CutModes[Math.Max(0, _cutMode.SelectedIndex)],
        Enabled = _enabled.IsToggled,
    };

    private string? Validate(PrinterConfig p)
    {
        if (p.Name.Length == 0) return L.T("name") + "?";
        if (p.Connection == PrinterConnection.WiFi && p.Host.Length == 0) return L.T("host") + "?";
        if (p.Connection == PrinterConnection.Serial && p.SerialPath.Length == 0) return L.T("serialPath") + "?";
        if (p.Connection == PrinterConnection.Bluetooth && p.BluetoothAddress.Length == 0) return L.T("noBtChosen");
        return null;
    }

    private async Task TestAsync()
    {
        var p = Collect();
        if (p.Name.Length == 0) p.Name = L.T("test");
        if (Validate(p) is { } problem) { await Ui.Alert(problem); return; }
        var err = await TillEngine.Instance.TestPrintAsync(p);
        await Ui.Alert(err ?? L.T("printed"));
    }

    private async Task SaveAsync()
    {
        var p = Collect();
        if (Validate(p) is { } problem) { await Ui.Alert(problem); return; }
        var s = AppSettings.Current;
        var at = s.Printers.FindIndex(x => x.Id == p.Id);
        if (at >= 0) s.Printers[at] = p; else s.Printers.Add(p);
        s.Save();
        await Navigation.PopAsync();
    }

    private async Task DeleteAsync()
    {
        if (!await Ui.Confirm(L.T("confirmDelete"))) return;
        var s = AppSettings.Current;
        s.Printers.RemoveAll(x => x.Id == _existing!.Id);
        s.Save();
        await Navigation.PopAsync();
    }
}
