using System.Net.Sockets;
using System.Runtime.InteropServices;
#if ANDROID
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
#endif
using Microsoft.Maui.ApplicationModel;
using OrderOrange.PartnerApp.Till.Models;
using OrderOrange.PartnerApp.Till.Services;

namespace OrderOrange.PartnerApp.Till.Printing;

/// <summary>One way of getting bytes into a printer. Each physical route is one class.</summary>
public interface IPrinterTransport
{
    string Describe();
    Task SendAsync(byte[] data, CancellationToken ct);
}

public static class PrinterTransports
{
    public static IPrinterTransport For(PrinterConfig p) => p.Connection switch
    {
        PrinterConnection.WiFi => new TcpPrinterTransport(p.Host.Trim(), p.Port),
#if ANDROID
        PrinterConnection.Usb => new UsbPrinterTransport(p.UsbVendorId, p.UsbProductId),
        PrinterConnection.Serial => new SerialPrinterTransport(p.SerialPath.Trim()),
        PrinterConnection.Bluetooth => new BluetoothPrinterTransport(p.BluetoothAddress.Trim(), p.BluetoothName),
        PrinterConnection.BuiltIn => new BuiltInPrinterTransport(),
#elif WINDOWS
        PrinterConnection.WindowsPrinter => new WindowsPrinterTransport(p.PrinterName.Trim()),
#endif
        _ => new UnsupportedTransport(p.Connection),
    };

    /// <summary>A pause proportional to the job so the printer's buffer drains before the link is closed.</summary>
    internal static Task DrainAsync(int bytes, CancellationToken ct) =>
        Task.Delay(Math.Clamp(200 + bytes / 100, 200, 1500), ct);
}

/// <summary>Raw ESC/POS over TCP — every Wi-Fi/LAN thermal printer listens on 9100.</summary>
public sealed class TcpPrinterTransport(string host, int port) : IPrinterTransport
{
    public string Describe() => $"{host}:{port}";

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new InvalidOperationException("No printer IP address set.");
        using var client = new TcpClient { NoDelay = true, SendTimeout = 15000 };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(5000);
        try { await client.ConnectAsync(host, port, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new IOException($"Printer {host}:{port} not reachable."); }
        catch (SocketException ex)
        { throw new IOException($"Printer {host}:{port} not reachable ({ex.SocketErrorCode})."); }

        using var stream = client.GetStream();
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
        await PrinterTransports.DrainAsync(data.Length, ct);
    }
}

/// <summary>A connection this device cannot make (a Bluetooth printer saved on a phone, opened on a PC).</summary>
public sealed class UnsupportedTransport(PrinterConnection connection) : IPrinterTransport
{
    public string Describe() => connection.ToString();
    public Task SendAsync(byte[] data, CancellationToken ct) =>
        throw new NotSupportedException($"A {connection} printer cannot be reached from this device.");
}

#if WINDOWS
/// <summary>
/// A printer installed in Windows (USB, network or shared), fed raw ESC/POS through the print
/// spooler: the driver renders nothing, so there is no dialog and no page layout, just the
/// same bytes a network thermal printer gets on port 9100.
/// </summary>
public sealed class WindowsPrinterTransport(string printerName) : IPrinterTransport
{
    public string Describe() => printerName;

    public Task SendAsync(byte[] data, CancellationToken ct) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(printerName)) throw new InvalidOperationException("No Windows printer chosen.");
        if (!WinSpool.OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new IOException($"Printer \"{printerName}\" not found (error {Marshal.GetLastWin32Error()}).");
        try
        {
            var doc = new WinSpool.DocInfo1 { DocName = "OrderOrange", Datatype = "RAW" };
            if (WinSpool.StartDocPrinter(handle, 1, doc) == 0)
                throw new IOException($"Printer \"{printerName}\" refused the job (error {Marshal.GetLastWin32Error()}).");
            try
            {
                WinSpool.StartPagePrinter(handle);
                if (!WinSpool.WritePrinter(handle, data, data.Length, out var written) || written != data.Length)
                    throw new IOException($"Printer \"{printerName}\" took {written} of {data.Length} bytes.");
                WinSpool.EndPagePrinter(handle);
            }
            finally { WinSpool.EndDocPrinter(handle); }
        }
        finally { WinSpool.ClosePrinter(handle); }
    }, ct);

    /// <summary>Every printer Windows knows on this PC, local and connected.</summary>
    public static List<string> Installed()
    {
        const int Local = 0x2, Connections = 0x4;
        WinSpool.EnumPrinters(Local | Connections, null, 4, IntPtr.Zero, 0, out var needed, out _);
        if (needed <= 0) return [];
        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!WinSpool.EnumPrinters(Local | Connections, null, 4, buffer, needed, out _, out var count)) return [];
            var size = Marshal.SizeOf<WinSpool.PrinterInfo4>();
            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WinSpool.PrinterInfo4>(buffer + i * size);
                if (!string.IsNullOrEmpty(info.PrinterName)) names.Add(info.PrinterName);
            }
            return names;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static class WinSpool
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public sealed class DocInfo1
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string? DocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string? Datatype;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PrinterInfo4
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string PrinterName;
            [MarshalAs(UnmanagedType.LPWStr)] public string ServerName;
            public int Attributes;
        }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr handle);

        [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int StartDocPrinter(IntPtr handle, int level, [In] DocInfo1 doc);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr handle);

        [DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr handle, byte[] data, int count, out int written);

        [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool EnumPrinters(int flags, string? name, int level, IntPtr buffer, int size, out int needed, out int returned);
    }
}
#endif

#if ANDROID
/// <summary>A serial device node (e.g. /dev/ttyS1) — how some terminals expose their own printer.</summary>
public sealed class SerialPrinterTransport(string path) : IPrinterTransport
{
    public string Describe() => path;

    public Task SendAsync(byte[] data, CancellationToken ct) => Task.Run(() =>
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("No serial device path set.");
        if (!File.Exists(path)) throw new FileNotFoundException($"{path} does not exist.");
        using var fs = new Java.IO.FileOutputStream(path);
        const int chunk = 4096;
        for (var off = 0; off < data.Length; off += chunk)
        {
            ct.ThrowIfCancellationRequested();
            fs.Write(data, off, Math.Min(chunk, data.Length - off));
        }
        fs.Flush();
        Thread.Sleep(Math.Clamp(100 + data.Length / 50, 100, 2000));
    }, ct);
}

/// <summary>
/// A printer on the terminal's USB host port (or the built-in printer when the vendor
/// wired it as a USB printer-class device). Vendor/product 0 means "any printer-class device".
/// </summary>
public sealed class UsbPrinterTransport(int vendorId, int productId) : IPrinterTransport
{
    private const string ActionPermission = "com.orderorange.till.USB_PERMISSION";

    public string Describe() => vendorId == 0 ? "USB printer" : $"USB {vendorId:X4}:{productId:X4}";

    public static UsbManager? Manager => Platform.AppContext.GetSystemService(Context.UsbService) as UsbManager;

    /// <summary>Every attached USB device, printer-class ones first.</summary>
    public static List<UsbDevice> Devices()
    {
        var mgr = Manager;
        if (mgr?.DeviceList is null) return [];
        return mgr.DeviceList.Values.OrderByDescending(IsPrinterClass).ToList();
    }

    public static bool IsPrinterClass(UsbDevice d)
    {
        if (d.DeviceClass == UsbClass.Printer) return true;
        for (var i = 0; i < d.InterfaceCount; i++)
            if (d.GetInterface(i).InterfaceClass == UsbClass.Printer) return true;
        return false;
    }

    public static string Label(UsbDevice d)
    {
        var name = d.ProductName ?? d.DeviceName ?? "USB device";
        return $"{name} ({d.VendorId:X4}:{d.ProductId:X4}){(IsPrinterClass(d) ? " · printer" : "")}";
    }

    private UsbDevice? Find()
    {
        var all = Devices();
        if (vendorId != 0)
            return all.FirstOrDefault(d => d.VendorId == vendorId && d.ProductId == productId);
        return all.FirstOrDefault(IsPrinterClass) ?? all.FirstOrDefault(d => BulkOut(d) is not null);
    }

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        var mgr = Manager ?? throw new InvalidOperationException("This device has no USB host.");
        var dev = Find() ?? throw new IOException("No USB printer is connected.");
        if (!mgr.HasPermission(dev) && !await RequestPermissionAsync(mgr, dev, ct))
            throw new IOException("USB permission was not granted.");
        await Task.Run(() => Write(mgr, dev, data, ct), ct);
    }

    private static (UsbInterface Intf, UsbEndpoint Ep)? BulkOut(UsbDevice dev)
    {
        (UsbInterface, UsbEndpoint)? fallback = null;
        for (var i = 0; i < dev.InterfaceCount; i++)
        {
            var intf = dev.GetInterface(i);
            for (var e = 0; e < intf.EndpointCount; e++)
            {
                var ep = intf.GetEndpoint(e);
                if (ep is null || ep.Type != UsbAddressing.XferBulk || ep.Direction != UsbAddressing.Out) continue;
                if (intf.InterfaceClass == UsbClass.Printer) return (intf, ep);
                fallback ??= (intf, ep);
            }
        }
        return fallback;
    }

    private static void Write(UsbManager mgr, UsbDevice dev, byte[] data, CancellationToken ct)
    {
        var route = BulkOut(dev) ?? throw new IOException("The USB device has no bulk-out endpoint.");
        using var conn = mgr.OpenDevice(dev) ?? throw new IOException("Could not open the USB device.");
        if (!conn.ClaimInterface(route.Intf, true)) throw new IOException("Could not claim the USB printer interface.");
        try
        {
            const int chunk = 16384;
            var buffer = new byte[chunk];
            for (var off = 0; off < data.Length;)
            {
                ct.ThrowIfCancellationRequested();
                var len = Math.Min(chunk, data.Length - off);
                Array.Copy(data, off, buffer, 0, len);
                var sent = conn.BulkTransfer(route.Ep, buffer, len, 10000);
                if (sent < 0) throw new IOException("USB write failed (printer offline or out of paper?).");
                off += sent;
            }
        }
        finally
        {
            try { conn.ReleaseInterface(route.Intf); } catch { }
        }
    }

    private static async Task<bool> RequestPermissionAsync(UsbManager mgr, UsbDevice dev, CancellationToken ct)
    {
        var ctx = Platform.AppContext;
        var receiver = new PermissionReceiver();
        var filter = new IntentFilter(ActionPermission);
        if (OperatingSystem.IsAndroidVersionAtLeast(33)) ctx.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        else ctx.RegisterReceiver(receiver, filter);
        try
        {
            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(31)) flags |= PendingIntentFlags.Mutable;
            var intent = new Intent(ActionPermission).SetPackage(ctx.PackageName);
            var pi = PendingIntent.GetBroadcast(ctx, 0, intent, flags);
            mgr.RequestPermission(dev, pi);
            var done = await Task.WhenAny(receiver.Result.Task, Task.Delay(60000, ct));
            return done == receiver.Result.Task && receiver.Result.Task.Result;
        }
        finally
        {
            try { ctx.UnregisterReceiver(receiver); } catch { }
        }
    }

    private sealed class PermissionReceiver : BroadcastReceiver
    {
        public readonly TaskCompletionSource<bool> Result = new();
        public override void OnReceive(Context? context, Intent? intent) =>
            Result.TrySetResult(intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) ?? false);
    }
}

/// <summary>
/// Bluetooth SPP (RFCOMM): how Sunmi-style terminals expose their own printer ("InnerPrinter",
/// paired out of the box) and how portable Bluetooth receipt printers work. Needs the device
/// paired in Android Settings first; on Android 12+ the BLUETOOTH_CONNECT permission is asked
/// for on first use.
/// </summary>
public sealed class BluetoothPrinterTransport(string address, string name) : IPrinterTransport
{
    private static readonly Java.Util.UUID Spp = Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    public string Describe() => string.IsNullOrEmpty(name) ? address : $"{name} ({address})";

    public static Android.Bluetooth.BluetoothAdapter? Adapter =>
        (Platform.AppContext.GetSystemService(Context.BluetoothService) as Android.Bluetooth.BluetoothManager)?.Adapter;

    public static bool HasPermission() =>
        !OperatingSystem.IsAndroidVersionAtLeast(31) ||
        Platform.AppContext.CheckSelfPermission(Android.Manifest.Permission.BluetoothConnect) == Android.Content.PM.Permission.Granted;

    public static async Task<bool> EnsurePermissionAsync()
    {
        if (HasPermission()) return true;
        try { return await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<BluetoothConnectPermission>()) == PermissionStatus.Granted; }
        catch { return false; }
    }

    /// <summary>The devices paired in Android Settings (empty without the permission).</summary>
    public static List<Android.Bluetooth.BluetoothDevice> Bonded()
    {
        try
        {
            if (!HasPermission()) return [];
            return Adapter?.BondedDevices?.ToList() ?? [];
        }
        catch { return []; }
    }

    public static bool LooksLikePrinter(Android.Bluetooth.BluetoothDevice d)
    {
        var n = d.Name ?? "";
        if (n.Contains("print", StringComparison.OrdinalIgnoreCase) || n.Contains("pos", StringComparison.OrdinalIgnoreCase)) return true;
        try { return d.BluetoothClass?.MajorDeviceClass == Android.Bluetooth.MajorDeviceClass.Imaging; } catch { return false; }
    }

    public static string Label(Android.Bluetooth.BluetoothDevice d) => $"{d.Name ?? "?"} ({d.Address})";

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("No Bluetooth printer chosen.");
        if (!await EnsurePermissionAsync()) throw new IOException("Bluetooth permission was not granted.");
        var adapter = Adapter ?? throw new IOException("This device has no Bluetooth.");
        if (!adapter.IsEnabled) throw new IOException("Bluetooth is switched off.");
        var device = Bonded().FirstOrDefault(d => string.Equals(d.Address, address, StringComparison.OrdinalIgnoreCase))
                     ?? throw new IOException($"Bluetooth printer {Describe()} is not paired — pair it in Android Settings → Connected devices.");

        await Task.Run(() =>
        {
            try { adapter.CancelDiscovery(); } catch { }
            using var socket = device.CreateRfcommSocketToServiceRecord(Spp) ?? throw new IOException("Could not open a Bluetooth channel.");
            try { socket.Connect(); }
            catch (Java.IO.IOException ex) { throw new IOException($"Bluetooth printer {Describe()} did not answer ({ex.Message})."); }
            try
            {
                using var os = socket.OutputStream ?? throw new IOException("Bluetooth channel has no output.");
                const int chunk = 2048;   // RFCOMM likes small writes; a 30 KB raster in one go stalls some firmware
                for (var off = 0; off < data.Length; off += chunk)
                {
                    ct.ThrowIfCancellationRequested();
                    os.Write(data, off, Math.Min(chunk, data.Length - off));
                }
                os.Flush();
                Thread.Sleep(Math.Clamp(300 + data.Length / 40, 300, 3000));   // let the printer drain before the link drops
            }
            finally { try { socket.Close(); } catch { } }
        }, ct);
    }
}

/// <summary>Android 12+ runtime permission for talking to paired Bluetooth devices.</summary>
public sealed class BluetoothConnectPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [(Android.Manifest.Permission.BluetoothConnect, true)];
}

/// <summary>
/// The terminal's own printer, whose wiring is unknown until tried: a paired Bluetooth
/// "InnerPrinter", a USB printer-class device, a loopback TCP port, or a serial node. Tries each; the route that works is
/// remembered and used first next time. (A vendor SDK, once the model is known, slots in
/// here as one more route.)
/// </summary>
public sealed class BuiltInPrinterTransport : IPrinterTransport
{
    public static readonly string[] SerialCandidates =
    [
        "/dev/ttyS1", "/dev/ttyS0", "/dev/ttyS2", "/dev/ttyS3", "/dev/ttyS4",
        "/dev/ttyMT1", "/dev/ttyMT0", "/dev/ttyMT2", "/dev/ttyHSL1", "/dev/ttyHS1",
        "/dev/ttyUSB0", "/dev/ttyACM0", "/dev/ttyGS0", "/dev/usb/lp0", "/dev/lp0",
    ];

    public string Describe() => "built-in";

    /// <summary>Every route worth a try, the remembered one first.</summary>
    public static List<(string Key, IPrinterTransport Transport)> Routes()
    {
        var list = new List<(string, IPrinterTransport)>();
        // Sunmi-style terminals (Majed's E-POS included, 2026-10-18) expose their own printer as
        // a paired Bluetooth device called "InnerPrinter" — that route goes first.
        foreach (var d in BluetoothPrinterTransport.Bonded().Where(BluetoothPrinterTransport.LooksLikePrinter)
                     .OrderByDescending(d => (d.Name ?? "").Contains("InnerPrinter", StringComparison.OrdinalIgnoreCase)))
            list.Add(($"bt:{d.Address}", new BluetoothPrinterTransport(d.Address ?? "", d.Name ?? "")));
        list.Add(("usb", new UsbPrinterTransport(0, 0)));
        list.Add(("tcp:127.0.0.1:9100", new TcpPrinterTransport("127.0.0.1", 9100)));
        foreach (var path in SerialCandidates)
            if (File.Exists(path)) list.Add(($"serial:{path}", new SerialPrinterTransport(path)));

        var remembered = AppSettings.Current.BuiltInRoute;
        if (remembered.Length > 0)
        {
            var at = list.FindIndex(r => r.Item1 == remembered);
            if (at > 0) { var r = list[at]; list.RemoveAt(at); list.Insert(0, r); }
        }
        return list;
    }

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        var errors = new List<string>();
        foreach (var (key, transport) in Routes())
        {
            try
            {
                await transport.SendAsync(data, ct);
                if (AppSettings.Current.BuiltInRoute != key)
                {
                    AppSettings.Current.BuiltInRoute = key;
                    AppSettings.Current.Save();
                    TillLog.Write($"built-in printer reached via {key}");
                }
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { errors.Add($"{key}: {ex.Message}"); }
        }
        throw new IOException("Built-in printer not found. Tried " + string.Join("; ", errors));
    }
}
#endif
