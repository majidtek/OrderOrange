namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Where the shared pages are running. The partner portal's pages also run inside the native
/// OrderOrange Partner app (OrderOrange.PartnerApp, Android + Windows), which carries its own
/// printers; a page that differs there (Settings: "Printers" instead of "download the app")
/// asks here. Set once at start-up by the app; false in every browser.
/// </summary>
public static class HostInfo
{
    /// <summary>True inside the OrderOrange Partner app.</summary>
    public static bool IsNativeApp { get; set; }

    /// <summary>
    /// Inside the app: the public address of the portal this app mirrors (https://partner.orderorange.com),
    /// for pages that link to files the web host serves (the partner guide) — the app's own
    /// address is private to it. Null in a browser, where relative links already work.
    /// </summary>
    public static string? SiteBase { get; set; }
}
