namespace OrderOrange.Shared;

/// <summary>One country a shop can say it trades in, with the money it most likely uses.</summary>
/// <param name="Code">ISO 3166-1 alpha-2, upper case ("OM"); "XX" = somewhere not listed.</param>
/// <param name="Flag">The flag emoji.</param>
/// <param name="Name">English name.</param>
/// <param name="Native">The name in the country's main language.</param>
/// <param name="Currency">The currency code the shop's prices will read in by default.</param>
/// <param name="TimeZones">IANA zones that point at this country, for a first guess from the browser.</param>
public record StoreRegion(string Code, string Flag, string Name, string Native, string Currency, string[] TimeZones)
{
    /// <summary>
    /// A small flag picture. Windows draws flag EMOJI as two plain letters ("IR"), so the
    /// pickers show an image instead; null for "Other country".
    /// </summary>
    public string? FlagUrl => Code == StoreRegions.Other ? null : $"https://flagcdn.com/24x18/{Code.ToLowerInvariant()}.png";
    public string? FlagUrl2x => Code == StoreRegions.Other ? null : $"https://flagcdn.com/48x36/{Code.ToLowerInvariant()}.png";
}

/// <summary>
/// The countries offered when a business is opened (Majed 2026-10-02: "after registration show a
/// dialog to select country and currency"). Gulf first — that is where the shops are today —
/// then the region, then the wider world. The currency is only a suggestion; the owner may
/// type any symbol, exactly as in Settings.
/// </summary>
public static class StoreRegions
{
    public const string Other = "XX";

    public static IReadOnlyList<StoreRegion> All { get; } =
    [
        new("OM", "🇴🇲", "Oman", "عُمان", "OMR", ["Asia/Muscat"]),
        new("AE", "🇦🇪", "United Arab Emirates", "الإمارات", "AED", ["Asia/Dubai"]),
        new("SA", "🇸🇦", "Saudi Arabia", "السعودية", "SAR", ["Asia/Riyadh"]),
        new("QA", "🇶🇦", "Qatar", "قطر", "QAR", ["Asia/Qatar"]),
        new("BH", "🇧🇭", "Bahrain", "البحرين", "BHD", ["Asia/Bahrain"]),
        new("KW", "🇰🇼", "Kuwait", "الكويت", "KWD", ["Asia/Kuwait"]),
        new("IR", "🇮🇷", "Iran", "ایران", "IRR", ["Asia/Tehran"]),
        new("IQ", "🇮🇶", "Iraq", "العراق", "IQD", ["Asia/Baghdad"]),
        new("JO", "🇯🇴", "Jordan", "الأردن", "JOD", ["Asia/Amman"]),
        new("LB", "🇱🇧", "Lebanon", "لبنان", "LBP", ["Asia/Beirut"]),
        new("SY", "🇸🇾", "Syria", "سوريا", "SYP", ["Asia/Damascus"]),
        new("YE", "🇾🇪", "Yemen", "اليمن", "YER", ["Asia/Aden"]),
        new("EG", "🇪🇬", "Egypt", "مصر", "EGP", ["Africa/Cairo"]),
        new("SD", "🇸🇩", "Sudan", "السودان", "SDG", ["Africa/Khartoum"]),
        new("LY", "🇱🇾", "Libya", "ليبيا", "LYD", ["Africa/Tripoli"]),
        new("TN", "🇹🇳", "Tunisia", "تونس", "TND", ["Africa/Tunis"]),
        new("DZ", "🇩🇿", "Algeria", "الجزائر", "DZD", ["Africa/Algiers"]),
        new("MA", "🇲🇦", "Morocco", "المغرب", "MAD", ["Africa/Casablanca"]),
        new("TR", "🇹🇷", "Türkiye", "Türkiye", "TRY", ["Europe/Istanbul"]),
        new("AZ", "🇦🇿", "Azerbaijan", "Azərbaycan", "AZN", ["Asia/Baku"]),
        new("AF", "🇦🇫", "Afghanistan", "افغانستان", "AFN", ["Asia/Kabul"]),
        new("PK", "🇵🇰", "Pakistan", "پاکستان", "PKR", ["Asia/Karachi"]),
        new("IN", "🇮🇳", "India", "भारत", "INR", ["Asia/Kolkata", "Asia/Calcutta"]),
        new("BD", "🇧🇩", "Bangladesh", "বাংলাদেশ", "BDT", ["Asia/Dhaka"]),
        new("MY", "🇲🇾", "Malaysia", "Malaysia", "MYR", ["Asia/Kuala_Lumpur"]),
        new("ID", "🇮🇩", "Indonesia", "Indonesia", "IDR", ["Asia/Jakarta"]),
        new("PH", "🇵🇭", "Philippines", "Pilipinas", "PHP", ["Asia/Manila"]),
        new("CN", "🇨🇳", "China", "中国", "CNY", ["Asia/Shanghai"]),
        new("JP", "🇯🇵", "Japan", "日本", "JPY", ["Asia/Tokyo"]),
        new("RU", "🇷🇺", "Russia", "Россия", "RUB", ["Europe/Moscow"]),
        new("GB", "🇬🇧", "United Kingdom", "United Kingdom", "GBP", ["Europe/London"]),
        new("DE", "🇩🇪", "Germany", "Deutschland", "EUR", ["Europe/Berlin"]),
        new("FR", "🇫🇷", "France", "France", "EUR", ["Europe/Paris"]),
        new("IT", "🇮🇹", "Italy", "Italia", "EUR", ["Europe/Rome"]),
        new("ES", "🇪🇸", "Spain", "España", "EUR", ["Europe/Madrid"]),
        new("NL", "🇳🇱", "Netherlands", "Nederland", "EUR", ["Europe/Amsterdam"]),
        new("PT", "🇵🇹", "Portugal", "Portugal", "EUR", ["Europe/Lisbon"]),
        new("US", "🇺🇸", "United States", "United States", "USD", ["America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles"]),
        new("CA", "🇨🇦", "Canada", "Canada", "CAD", ["America/Toronto", "America/Vancouver"]),
        new("AU", "🇦🇺", "Australia", "Australia", "AUD", ["Australia/Sydney", "Australia/Melbourne", "Australia/Perth"]),
        new(Other, "🌍", "Other country", "", "", []),
    ];

    public static StoreRegion? Find(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : All.FirstOrDefault(r => string.Equals(r.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A first guess from the browser's time zone; null when it says nothing useful.</summary>
    public static StoreRegion? FromTimeZone(string? zone) =>
        string.IsNullOrWhiteSpace(zone) ? null : All.FirstOrDefault(r => r.TimeZones.Contains(zone.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>Every currency code the list knows, for the currency picker.</summary>
    public static IReadOnlyList<string> Currencies { get; } =
        All.Select(r => r.Currency).Where(c => c.Length > 0).Distinct().ToList();
}

/// <summary>The answer to the "where is your business?" dialog.</summary>
/// <param name="TimeZone">IANA zone ("Asia/Muscat"); empty = the country's first zone.</param>
/// <param name="DateFormat">"dmy" | "mdy" | "ymd"; empty = unchanged.</param>
/// <param name="TimeFormat">"24" | "12"; empty = unchanged.</param>
public record RegionRequest(string Country, string Currency, string TimeZone = "", string DateFormat = "", string TimeFormat = "");

/// <summary>
/// The second welcome step (Majed 2026-10-09: "after select money type, in next dialog have
/// to enter restaurant name and phone number, based on country select"). Names follow the
/// English-first rule. Mobile and Phone (the shop's landline) are both OPTIONAL ("restaurant
/// phone number and mobile number, optional"), in international form ("+1 202 555 0100").
/// </summary>
public record StoreBasicsRequest(Dictionary<string, string> Names, string Mobile = "", string Phone = "", int CuisineId = 0);

/// <summary>One time zone the pickers offer, with its offset from UTC right now.</summary>
public record TimeZoneOptionDto(string Id, string Label, int OffsetMinutes);

public static class StoreClock
{
    /// <summary>Countries that read the clock as 2:05 PM rather than 14:05.</summary>
    private static readonly HashSet<string> TwelveHour = new(StringComparer.OrdinalIgnoreCase) { "US", "CA", "AU", "IN", "PK", "BD", "PH", "MY", "EG", "SA" };

    /// <summary>The date order a country usually writes.</summary>
    public static string DefaultDateFormat(string? country) => country?.ToUpperInvariant() switch
    {
        "US" or "PH" => "mdy",
        "CN" or "JP" or "IR" => "ymd",
        _ => "dmy",
    };

    public static string DefaultTimeFormat(string? country) => country is not null && TwelveHour.Contains(country) ? "12" : "24";

    /// <summary>Every zone the country list knows, plus UTC.</summary>
    public static IReadOnlyList<string> Zones { get; } =
        StoreRegions.All.SelectMany(r => r.TimeZones).Append("UTC").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
