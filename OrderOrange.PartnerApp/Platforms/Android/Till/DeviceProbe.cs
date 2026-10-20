using System.Net.Sockets;
using System.Text;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui.ApplicationModel;

namespace OrderOrange.PartnerApp.Till.Printing;

/// <summary>
/// A plain-text report of what this terminal is and how its printer might be reached —
/// what to send Majed when a new model turns up. Read-only; prints nothing.
/// </summary>
public static class DeviceProbe
{
    public static async Task<string> RunAsync()
    {
        var sb = new StringBuilder();
        var ctx = Platform.AppContext;

        sb.AppendLine("== Device ==");
        sb.AppendLine($"Manufacturer: {Build.Manufacturer}");
        sb.AppendLine($"Model: {Build.Model}  Device: {Build.Device}  Product: {Build.Product}");
        sb.AppendLine($"Board: {Build.Board}  Hardware: {Build.Hardware}  Brand: {Build.Brand}");
        sb.AppendLine($"Android: {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt})");
        var dm = ctx.Resources?.DisplayMetrics;
        if (dm is not null) sb.AppendLine($"Screen: {dm.WidthPixels}x{dm.HeightPixels} density {dm.Density}");

        sb.AppendLine();
        sb.AppendLine("== USB devices ==");
        try
        {
            var devices = UsbPrinterTransport.Devices();
            if (devices.Count == 0) sb.AppendLine("(none)");
            foreach (var d in devices)
            {
                sb.AppendLine($"{UsbPrinterTransport.Label(d)}  class {(int)d.DeviceClass}  {d.DeviceName}");
                sb.AppendLine($"   manufacturer: {d.ManufacturerName}  permission: {UsbPrinterTransport.Manager?.HasPermission(d)}");
                for (var i = 0; i < d.InterfaceCount; i++)
                {
                    var intf = d.GetInterface(i);
                    sb.AppendLine($"   interface {i}: class {(int)intf.InterfaceClass} sub {intf.InterfaceSubclass} proto {intf.InterfaceProtocol} endpoints {intf.EndpointCount}");
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"(error: {ex.Message})"); }

        sb.AppendLine();
        sb.AppendLine("== Serial candidates ==");
        foreach (var path in BuiltInPrinterTransport.SerialCandidates)
        {
            if (!File.Exists(path)) continue;
            var writable = await Task.Run(() =>
            {
                try { using var fs = new Java.IO.FileOutputStream(path); return "writable"; }
                catch (Exception ex) { return $"not writable ({ex.GetType().Name})"; }
            });
            sb.AppendLine($"{path}: {writable}");
        }
        try
        {
            var ttys = Directory.GetFiles("/dev", "tty*").Where(f => !f.StartsWith("/dev/tty", StringComparison.Ordinal) || f.Length > 8).Take(40).ToList();
            if (ttys.Count > 0) sb.AppendLine("/dev: " + string.Join(" ", ttys.Select(Path.GetFileName)));
        }
        catch { sb.AppendLine("/dev: not listable"); }

        sb.AppendLine();
        sb.AppendLine("== Paired Bluetooth devices ==");
        try
        {
            if (!await BluetoothPrinterTransport.EnsurePermissionAsync()) sb.AppendLine("(no Bluetooth permission)");
            else if (BluetoothPrinterTransport.Adapter is not { } bt) sb.AppendLine("(no Bluetooth)");
            else
            {
                sb.AppendLine(bt.IsEnabled ? "Bluetooth on" : "Bluetooth OFF");
                var bonded = BluetoothPrinterTransport.Bonded();
                sb.AppendLine(bonded.Count == 0 ? "(none paired)" : string.Join("\n", bonded.Select(d =>
                    $"{BluetoothPrinterTransport.Label(d)} class={d.BluetoothClass?.MajorDeviceClass}{(BluetoothPrinterTransport.LooksLikePrinter(d) ? " · printer" : "")}")));
            }
        }
        catch (Exception ex) { sb.AppendLine($"(error: {ex.Message})"); }

        sb.AppendLine();
        sb.AppendLine("== Loopback 127.0.0.1:9100 ==");
        sb.AppendLine(await TcpOpenAsync("127.0.0.1", 9100) ? "open (an internal print server answers)" : "closed");

        sb.AppendLine();
        sb.AppendLine("== Printer-related packages ==");
        try
        {
            var pm = ctx.PackageManager!;
            var apps = pm.GetInstalledApplications(PackageInfoFlags.MatchAll)
                .Select(a => a.PackageName ?? "")
                .Where(n => n.Contains("print", StringComparison.OrdinalIgnoreCase) || n.Contains("pos", StringComparison.OrdinalIgnoreCase)
                         || n.Contains("sunmi", StringComparison.OrdinalIgnoreCase) || n.Contains("iposprinter", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n).ToList();
            sb.AppendLine(apps.Count == 0 ? "(none)" : string.Join("\n", apps));
        }
        catch (Exception ex) { sb.AppendLine($"(error: {ex.Message})"); }

        sb.AppendLine();
        sb.AppendLine($"Remembered built-in route: {Models.AppSettings.Current.BuiltInRoute}");
        return sb.ToString();
    }

    private static async Task<bool> TcpOpenAsync(string host, int port)
    {
        try
        {
            using var c = new TcpClient();
            using var cts = new CancellationTokenSource(1500);
            await c.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch { return false; }
    }
}
