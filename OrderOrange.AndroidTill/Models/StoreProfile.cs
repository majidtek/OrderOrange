using OrderOrange.Shared;

namespace OrderOrange.AndroidTill.Models;

/// <summary>
/// The handful of store facts a printed receipt needs, pulled from the partner API's
/// <c>MyRestaurantDto</c> plus the client host the QR should point at. Same shape as the
/// Windows till's, so HtmlReceipt is a straight copy.
/// </summary>
public sealed record StoreProfile(
    int Id, string Name, string Area, string Street, string Phone,
    string CrNumber, string VatNumber, string ClientUrl)
{
    public static StoreProfile From(MyRestaurantDto r, string clientUrl) =>
        new(r.Id, r.Name, r.Area, r.Street, r.Phone, r.CrNumber, r.VatNumber, clientUrl);

    public static StoreProfile Fallback(string name, string clientUrl) =>
        new(0, name, "", "", "", "", "", clientUrl);
}
