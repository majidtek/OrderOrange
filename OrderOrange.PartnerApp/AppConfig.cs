namespace OrderOrange.PartnerApp;

/// <summary>
/// Where the app reaches OrderOrange. The browser portal gets these from its host's /config;
/// a native app has no host, so they live here. Point all three at staging to test:
/// Api https://orderorange.com:8600/, ClientUrl https://orderorange.com:9443,
/// PartnerHost https://orderorange.com:9444.
/// </summary>
public static class AppConfig
{
    /// <summary>The public API origin (the same one the Android till and the browser use).</summary>
    public const string ApiBaseUrl = "https://api.orderorange.com/";

    /// <summary>The customer site — QR codes and "view my store" links point here.</summary>
    public const string ClientUrl = "https://orderorange.com";

    /// <summary>The partner web host: it answers the two map helpers (/placesearch, /maplink) with the Google key kept server-side.</summary>
    public const string PartnerHost = "https://partner.orderorange.com";
}
