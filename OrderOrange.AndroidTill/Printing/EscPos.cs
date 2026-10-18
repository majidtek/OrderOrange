using Android.Graphics;

namespace OrderOrange.AndroidTill.Printing;

/// <summary>
/// Turns a rendered ticket bitmap into ESC/POS bytes. Everything is sent as a raster
/// image (<c>GS v 0</c>) — the terminal's PC437/GB18030 code pages have no Arabic or
/// Persian glyphs, so text mode is never used. Works for every ESC/POS thermal printer.
/// </summary>
public static class EscPos
{
    /// <summary>Rows per raster block; some printers choke on one huge image.</summary>
    private const int BandRows = 256;

    public static readonly byte[] Init = [0x1B, 0x40];

    /// <summary>
    /// Initialise, print the image in bands, feed, and optionally cut.
    /// </summary>
    /// <param name="threshold">Grey level (0–255) below which a pixel prints black. ~160 suits text.</param>
    /// <param name="dither">Floyd–Steinberg instead of a plain threshold — smoother photo logos, speckled text.</param>
    public static byte[] Encode(Bitmap bitmap, bool dither, int threshold, int feedLines, bool cut)
    {
        var (rows, widthBytes, height) = ToOneBit(bitmap, dither, threshold);

        using var ms = new MemoryStream(rows.Length + 64);
        ms.Write(Init);
        for (var y = 0; y < height; y += BandRows)
        {
            var n = Math.Min(BandRows, height - y);
            ms.Write([0x1D, 0x76, 0x30, 0x00,
                (byte)(widthBytes & 0xFF), (byte)((widthBytes >> 8) & 0xFF),
                (byte)(n & 0xFF), (byte)((n >> 8) & 0xFF)]);
            ms.Write(rows, y * widthBytes, n * widthBytes);
        }
        ms.Write([0x1B, 0x64, (byte)Math.Clamp(feedLines, 0, 12)]);   // print and feed n lines
        if (cut) ms.Write([0x1D, 0x56, 0x42, 0x00]);                 // feed and partial cut
        return ms.ToArray();
    }

    /// <summary>
    /// 1 bit per pixel, MSB first, one byte per 8 pixels, rows top to bottom; 1 = black.
    /// Trailing white rows are dropped so the paper is not fed for nothing.
    /// </summary>
    public static (byte[] Rows, int WidthBytes, int Height) ToOneBit(Bitmap bitmap, bool dither, int threshold)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var px = new int[w * h];
        bitmap.GetPixels(px, 0, w, 0, 0, w, h);

        // Composite over white and reduce to grey.
        var grey = new int[w * h];
        for (var i = 0; i < px.Length; i++)
        {
            var c = px[i];
            var a = (c >> 24) & 0xFF;
            var r = (c >> 16) & 0xFF;
            var g = (c >> 8) & 0xFF;
            var b = c & 0xFF;
            var lum = (r * 299 + g * 587 + b * 114) / 1000;
            if (a < 255) lum = (lum * a + 255 * (255 - a)) / 255;
            grey[i] = lum;
        }

        var widthBytes = (w + 7) / 8;
        var rows = new byte[widthBytes * h];
        threshold = Math.Clamp(threshold, 1, 254);

        if (!dither)
        {
            for (var y = 0; y < h; y++)
            {
                var ro = y * widthBytes;
                var go = y * w;
                for (var x = 0; x < w; x++)
                    if (grey[go + x] < threshold) rows[ro + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }
        else
        {
            // Floyd–Steinberg: push each pixel's rounding error onto its unvisited neighbours.
            for (var y = 0; y < h; y++)
            {
                var ro = y * widthBytes;
                var go = y * w;
                for (var x = 0; x < w; x++)
                {
                    var old = grey[go + x];
                    var black = old < 128;
                    if (black) rows[ro + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    var err = old - (black ? 0 : 255);
                    if (x + 1 < w) grey[go + x + 1] += err * 7 / 16;
                    if (y + 1 < h)
                    {
                        if (x > 0) grey[go + w + x - 1] += err * 3 / 16;
                        grey[go + w + x] += err * 5 / 16;
                        if (x + 1 < w) grey[go + w + x + 1] += err * 1 / 16;
                    }
                }
            }
        }

        // Drop blank rows at the bottom (keep a few for breathing room).
        var last = h - 1;
        while (last > 0 && RowIsBlank(rows, last * widthBytes, widthBytes)) last--;
        var height = Math.Min(h, last + 1 + 8);
        if (height < h) Array.Resize(ref rows, height * widthBytes);
        return (rows, widthBytes, height);
    }

    private static bool RowIsBlank(byte[] rows, int offset, int count)
    {
        for (var i = 0; i < count; i++) if (rows[offset + i] != 0) return false;
        return true;
    }
}
