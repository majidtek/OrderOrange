using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Turns inline base64 photos into small cacheable URLs on the customer-facing payloads.
/// A store page used to carry >1 MB of dish photos as data URIs through the circuit on
/// EVERY visit; as URLs the browser downloads each picture once and caches it for a week.
///
/// Gated on Media:PublicBase (the API's public origin, e.g. https://orderorange.com:8500):
/// when the key is absent everything keeps flowing inline exactly as before — a deploy
/// without the config can never produce image links that point nowhere. Owner/partner
/// endpoints are left inline on purpose: editors need the real bytes, and the WPF till's
/// handling of URLs is unverified.
/// </summary>
public static class MediaLinks
{
    public static bool Enabled(IConfiguration config) =>
        !string.IsNullOrEmpty(config["Media:PublicBase"]);

    private static string Base(IConfiguration config) =>
        (config["Media:PublicBase"] ?? "").TrimEnd('/');

    /// <summary>?v= rides the content length — a re-uploaded photo gets a fresh URL.</summary>
    public static string? Dish(IConfiguration config, int itemId, string? photo) =>
        photo is not { Length: > 0 } ? null
        : !photo.StartsWith("data:") || !Enabled(config) ? photo
        : $"{Base(config)}/api/media/dish/{itemId}?v={photo.Length}";

    public static string? Logo(IConfiguration config, int storeId, string? logo) =>
        logo is not { Length: > 0 } ? null
        : !logo.StartsWith("data:") || !Enabled(config) ? logo
        : $"{Base(config)}/api/media/logo/{storeId}?v={logo.Length}";

    public static string Banner(IConfiguration config, int photoId, string data) =>
        !data.StartsWith("data:") || !Enabled(config)
            ? data
            : $"{Base(config)}/api/media/banner/{photoId}?v={data.Length}";

    public static string DishSetPhoto(IConfiguration config, int photoId, string data) =>
        !data.StartsWith("data:") || !Enabled(config)
            ? data
            : $"{Base(config)}/api/media/dishphoto/{photoId}?v={data.Length}";

    public static MenuCategoryDto Lighten(IConfiguration config, MenuCategoryDto category) =>
        !Enabled(config)
            ? category
            : category with
            {
                Items = category.Items
                    .Select(i => i with { Photo = Dish(config, i.Id, i.Photo) })
                    .ToList()
            };
}
