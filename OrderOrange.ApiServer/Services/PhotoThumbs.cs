using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Small copies of product photos for the POS tiles (Majed 2026-10-22: "make small for all
/// product images and show it in pos; show big image when user click"). A product's main
/// photo is stored as a data URI that can run to hundreds of KB; a tile is about 130×74 px,
/// so the till gets a WebP of at most <see cref="MaxSide"/> px (sharp on a 2× screen) that is
/// a few KB. The full photo is still there for the big view.
/// </summary>
public static class PhotoThumbs
{
    /// <summary>The longest side of a thumbnail: twice the largest tile photo, for sharp phone screens.</summary>
    public const int MaxSide = 240;

    private const int Quality = 72;

    /// <summary>
    /// Identifies the photo a thumbnail was made from, so a changed photo (an edit, a new main
    /// photo) gets a new thumbnail without every code path that writes photos having to know.
    /// </summary>
    public static string KeyOf(string photo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(photo)))[..20];

    /// <summary>
    /// A WebP data URI of the photo scaled to fit <see cref="MaxSide"/>, upright, or null when
    /// the photo cannot be read (not a base64 image data URI, or a format the decoder lacks).
    /// </summary>
    public static string? Make(string? photo)
    {
        if (string.IsNullOrWhiteSpace(photo) || !photo.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return null;
        var comma = photo.IndexOf(',');
        if (comma < 0 || !photo.AsSpan(0, comma).Contains(";base64", StringComparison.OrdinalIgnoreCase)) return null;

        byte[] bytes;
        try { bytes = Convert.FromBase64String(photo[(comma + 1)..]); }
        catch (FormatException) { return null; }

        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(bytes));
            if (codec is null) return null;
            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null || decoded.Width == 0 || decoded.Height == 0) return null;
            using var upright = Upright(decoded, codec.EncodedOrigin);
            var source = upright ?? decoded;

            var scale = Math.Min(1.0, (double)MaxSide / Math.Max(source.Width, source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var small = source.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
            if (small is null) return null;
            using var image = SKImage.FromBitmap(small);
            using var data = image.Encode(SKEncodedImageFormat.Webp, Quality);
            if (data is null) return null;
            return "data:image/webp;base64," + Convert.ToBase64String(data.Span);
        }
        catch (Exception)
        {
            // A damaged picture must never break the till: the tile shows its emoji instead.
            return null;
        }
    }

    /// <summary>
    /// The small photo for a product, making (and keeping) it when it is missing or was made
    /// from an older photo. Null when the product has no photo; the original when it cannot
    /// be shrunk. The doc needs only Id, PhotoData, PhotoThumb and PhotoThumbKey.
    /// </summary>
    public static async Task<string?> SmallFor(Data.MenuItemDoc doc, Data.CatalogStore catalog)
    {
        if (string.IsNullOrEmpty(doc.PhotoData)) return null;
        var key = KeyOf(doc.PhotoData);
        if (doc.PhotoThumbKey != key || doc.PhotoThumb is null)
        {
            // "" records "could not be shrunk", so a picture the decoder cannot read is not retried on every view.
            doc.PhotoThumb = Make(doc.PhotoData) ?? "";
            doc.PhotoThumbKey = key;
            await catalog.SetPhotoThumbAsync(doc.Id, doc.PhotoThumb, key);
        }
        return doc.PhotoThumb.Length > 0 ? doc.PhotoThumb : doc.PhotoData;
    }

    /// <summary>Phone photos often carry a rotation flag instead of rotated pixels; bake it in.</summary>
    private static SKBitmap? Upright(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        var (degrees, swap) = origin switch
        {
            SKEncodedOrigin.BottomRight => (180, false),
            SKEncodedOrigin.RightTop => (90, true),
            SKEncodedOrigin.LeftBottom => (270, true),
            _ => (0, false),
        };
        if (degrees == 0) return null;

        var rotated = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
        using var canvas = new SKCanvas(rotated);
        canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
        canvas.DrawBitmap(bitmap, 0, 0);
        return rotated;
    }
}
