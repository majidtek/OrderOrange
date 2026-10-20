using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
#if ANDROID
using Android.Content;
using Android.Net;
#endif
using Microsoft.Maui.ApplicationModel;
using OrderOrange.PartnerApp.Till.Localization;
using OrderOrange.PartnerApp.Till.Models;

namespace OrderOrange.PartnerApp.Till.Printing;

/// <summary>
/// Finds printers so nobody has to type an IP (Majed 2026-10-18 on the first terminal: "there
/// is no printer"). Two sweeps: the terminal itself (USB printer-class devices, an internal
/// print server on 127.0.0.1:9100, writable serial nodes) and the shop Wi-Fi (every host on
/// the terminal's own subnet that answers on TCP 9100 — the raw port every ESC/POS network
/// printer listens on). Each hit comes back as a ready-to-save <see cref="PrinterConfig"/>.
/// </summary>
public static class PrinterDiscovery
{
    public sealed record Found(string Title, string Detail, PrinterConfig Config);

    /// <summary>What is wired into the terminal. Quick (a second or two), no paper used.</summary>
    public static async Task<List<Found>> FindBuiltInAsync(CancellationToken ct = default)
    {
#if WINDOWS
        var list = new List<Found>();
        try
        {
            foreach (var name in WindowsPrinterTransport.Installed())
            {
                // The PDF/XPS/OneNote/Fax queues are not thermal printers.
                if (name.Contains("PDF", StringComparison.OrdinalIgnoreCase) || name.Contains("XPS", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("OneNote", StringComparison.OrdinalIgnoreCase) || name.Contains("Fax", StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(new Found(L.T("windowsPrinter"), name, new PrinterConfig
                {
                    Name = name, Connection = PrinterConnection.WindowsPrinter, PrinterName = name,
                    Role = PrinterRole.Cashier, Paper = 80, Cut = true,
                }));
            }
        }
        catch { /* no spooler */ }
        await Task.CompletedTask;
        return list;
#else
        var list = new List<Found>();

        // Paired Bluetooth printers — on Sunmi-style terminals the built-in printer itself shows up
        // here as "InnerPrinter" (Majed's E-POS, 2026-10-18). Portable Bluetooth printers too.
        try
        {
            if (await BluetoothPrinterTransport.EnsurePermissionAsync())
            {
                var bonded = BluetoothPrinterTransport.Bonded()
                    .OrderByDescending(d => (d.Name ?? "").Contains("InnerPrinter", StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(BluetoothPrinterTransport.LooksLikePrinter);
                foreach (var d in bonded)
                {
                    var inner = (d.Name ?? "").Contains("InnerPrinter", StringComparison.OrdinalIgnoreCase);
                    list.Add(new Found(inner ? L.T("builtIn") : L.T("bluetoothPrinter"), BluetoothPrinterTransport.Label(d), new PrinterConfig
                    {
                        Name = inner ? L.T("builtIn") : d.Name ?? L.T("bluetoothPrinter"), Connection = PrinterConnection.Bluetooth,
                        BluetoothAddress = d.Address ?? "", BluetoothName = d.Name ?? "",
                        Role = inner ? PrinterRole.Cashier : PrinterRole.Kitchen, Paper = inner ? 58 : 80, Cut = !inner,
                    }));
                }
            }
        }
        catch { /* no Bluetooth */ }

        // A built-in printer the vendor exposed as a USB printer-class device, or one on the host port.
        try
        {
            foreach (var d in UsbPrinterTransport.Devices().Where(UsbPrinterTransport.IsPrinterClass))
                list.Add(new Found(L.T("usbPrinter"), UsbPrinterTransport.Label(d), new PrinterConfig
                {
                    Name = d.ProductName ?? L.T("usbPrinter"), Connection = PrinterConnection.Usb,
                    UsbVendorId = d.VendorId, UsbProductId = d.ProductId,
                    Role = PrinterRole.Cashier, Paper = 58, Cut = false,
                }));
        }
        catch { /* no USB host */ }

        // An internal print server (some terminals bridge their printer to a loopback port).
        if (await TcpOpenAsync("127.0.0.1", 9100, 1200, ct))
            list.Add(new Found(L.T("builtIn"), "127.0.0.1:9100", new PrinterConfig
            {
                Name = L.T("builtIn"), Connection = PrinterConnection.BuiltIn, Role = PrinterRole.Cashier, Paper = 58, Cut = false,
            }));

        // Serial nodes the app may write to.
        foreach (var path in BuiltInPrinterTransport.SerialCandidates)
        {
            if (!File.Exists(path)) continue;
            var writable = await Task.Run(() =>
            {
                try { using var fs = new Java.IO.FileOutputStream(path); return true; } catch { return false; }
            }, ct);
            if (!writable) continue;
            list.Add(new Found(L.T("builtIn"), path, new PrinterConfig
            {
                Name = L.T("builtIn"), Connection = PrinterConnection.Serial, SerialPath = path,
                Role = PrinterRole.Cashier, Paper = 58, Cut = false,
            }));
        }
        return list;
#endif
    }

    /// <summary>
    /// Every host on the terminal's subnet that answers on <paramref name="port"/>. A /24 takes
    /// about two seconds with 64 connections in flight; progress is reported in percent.
    /// </summary>
    public static async Task<List<Found>> ScanNetworkAsync(IProgress<int>? progress, CancellationToken ct = default, int port = 9100)
    {
        var local = LocalIPv4() ?? throw new InvalidOperationException(L.T("noWifi"));
        var hosts = HostsOf(local.Address, local.Prefix).Where(h => !h.Equals(local.Address)).ToList();
        var found = new ConcurrentBag<IPAddress>();
        var done = 0;
        using var gate = new SemaphoreSlim(64);
        await Task.WhenAll(hosts.Select(async h =>
        {
            await gate.WaitAsync(ct);
            try { if (await TcpOpenAsync(h.ToString(), port, 450, ct)) found.Add(h); }
            finally
            {
                gate.Release();
                progress?.Report(Interlocked.Increment(ref done) * 100 / Math.Max(1, hosts.Count));
            }
        }));
        return found.OrderBy(Key).Select(h => new Found(L.T("networkPrinter"), $"{h}:{port}", new PrinterConfig
        {
            Name = $"{L.T("kitchen")} {h.ToString().Split('.').Last()}", Connection = PrinterConnection.WiFi,
            Host = h.ToString(), Port = port, Role = PrinterRole.Kitchen, Paper = 80, Cut = true,
        })).ToList();

        static uint Key(IPAddress a) { var b = a.GetAddressBytes(); return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3]; }
    }

    /// <summary>The terminal's own IPv4 address and prefix on the active network (Wi-Fi or Ethernet).</summary>
    public static (IPAddress Address, int Prefix)? LocalIPv4()
    {
#if ANDROID
        try
        {
            var cm = Platform.AppContext.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
            var network = cm?.ActiveNetwork;
            var props = network is null ? null : cm!.GetLinkProperties(network);
            if (props?.LinkAddresses is { } addresses)
            {
                foreach (var la in addresses)
                {
                    if (la.Address is not Java.Net.Inet4Address inet || inet.IsLoopbackAddress) continue;
                    var ip = IPAddress.Parse(inet.HostAddress ?? "");
                    return (ip, la.PrefixLength);
                }
            }
        }
        catch { /* fall through to the .NET view */ }
#endif

        // Fallback: any non-loopback IPv4 interface, assumed /24.
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                    return (ua.Address, ua.PrefixLength is > 0 and <= 32 ? ua.PrefixLength : 24);
        }
        return null;
    }

    /// <summary>The usable hosts of the subnet; anything wider than a /22 is clipped to the /24 around the terminal.</summary>
    public static IEnumerable<IPAddress> HostsOf(IPAddress address, int prefix)
    {
        if (prefix < 22 || prefix > 30) prefix = 24;
        var b = address.GetAddressBytes();
        var ip = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        var mask = prefix == 0 ? 0u : 0xFFFFFFFFu << (32 - prefix);
        var net = ip & mask;
        var size = ~mask;
        for (uint i = 1; i < size; i++)
        {
            var h = net + i;
            yield return new IPAddress(new[] { (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h });
        }
    }

    public static async Task<bool> TcpOpenAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var c = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            await c.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch { return false; }
    }
}
