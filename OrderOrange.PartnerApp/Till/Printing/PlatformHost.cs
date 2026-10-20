namespace OrderOrange.PartnerApp.Till.Printing;

/// <summary>
/// What keeps the printers going, per platform. Android: a foreground service, battery
/// optimisation off and an overlay window for drawing tickets with the screen off.
/// Windows: the app window itself (nothing to ask for).
/// </summary>
public static class PlatformHost
{
    public static bool IsAndroid => OperatingSystem.IsAndroid();

    public static void StartService()
    {
#if ANDROID
        MainActivity.StartTillService();
#endif
    }

    public static void StopService()
    {
#if ANDROID
        MainActivity.StopTillService();
#endif
    }

    public static bool BackgroundAllowed()
    {
#if ANDROID
        return MainActivity.BackgroundAllowed();
#else
        return true;
#endif
    }

    public static void RequestBackgroundAllowed()
    {
#if ANDROID
        MainActivity.RequestBackgroundAllowed();
#endif
    }

    public static bool CanUseOverlay()
    {
#if ANDROID
        return HtmlRenderer.CanUseOverlay();
#else
        return true;
#endif
    }

    public static void RequestOverlay()
    {
#if ANDROID
        HtmlRenderer.RequestOverlay();
#endif
    }
}

/// <summary>The device lists the printer form picks from, per platform.</summary>
public static class DevicePickers
{
    public sealed record Device(string Key, string Name, string Label);

    /// <summary>Paired Bluetooth devices, the terminal's "InnerPrinter" first. Null + message when Bluetooth cannot be used.</summary>
    public static async Task<(List<Device> Devices, string? Problem)> BluetoothAsync()
    {
#if ANDROID
        if (!await BluetoothPrinterTransport.EnsurePermissionAsync()) return ([], "btNoPermission");
        if (BluetoothPrinterTransport.Adapter is null) return ([], "btNone");
        if (!BluetoothPrinterTransport.Adapter.IsEnabled) return ([], "btOff");
        var list = BluetoothPrinterTransport.Bonded()
            .OrderByDescending(d => (d.Name ?? "").Contains("InnerPrinter", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(BluetoothPrinterTransport.LooksLikePrinter)
            .Select(d => new Device(d.Address ?? "", d.Name ?? "", BluetoothPrinterTransport.Label(d)))
            .ToList();
        return (list, list.Count == 0 ? "btNotPaired" : null);
#else
        await Task.CompletedTask;
        return ([], "btNone");
#endif
    }

    /// <summary>Attached USB devices as "vendor:product".</summary>
    public static List<Device> Usb()
    {
#if ANDROID
        return UsbPrinterTransport.Devices()
            .Select(d => new Device($"{d.VendorId}:{d.ProductId}", d.ProductName ?? "", UsbPrinterTransport.Label(d)))
            .ToList();
#else
        return [];
#endif
    }

    /// <summary>The printers installed in Windows.</summary>
    public static List<Device> WindowsPrinters()
    {
#if WINDOWS
        try { return WindowsPrinterTransport.Installed().Select(n => new Device(n, n, n)).ToList(); }
        catch { return []; }
#else
        return [];
#endif
    }
}
