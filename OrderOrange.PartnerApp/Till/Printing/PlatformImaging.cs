namespace OrderOrange.PartnerApp.Till.Printing;

/// <summary>
/// Ticket HTML → pixels, and a picture → pixels at the printer's width. The ESC/POS side only
/// ever sees 0xAARRGGBB ints, so the same encoder and transports serve every platform.
/// Android draws with an off-screen WebView (works with the app in the background);
/// Windows draws inside the app's own WebView2 (html2canvas) and decodes with WinRT imaging.
/// </summary>
public static partial class PlatformImaging
{
    public readonly record struct Pixels(int[] Argb, int Width, int Height);

#if ANDROID
    public static async Task<Pixels> RenderHtmlAsync(string html, int widthDots, int cssWidth)
    {
        using var bitmap = await HtmlRenderer.RenderAsync(html, widthDots, cssWidth);
        return FromBitmap(bitmap);
    }

    public static Task<Pixels> DecodePngAsync(byte[] png, int widthDots)
    {
        using var decoded = Android.Graphics.BitmapFactory.DecodeByteArray(png, 0, png.Length)
            ?? throw new InvalidOperationException("The receipt picture could not be read.");
        var height = Math.Max(1, (int)Math.Round(decoded.Height * (double)widthDots / decoded.Width));
        using var scaled = decoded.Width == widthDots
            ? decoded.Copy(Android.Graphics.Bitmap.Config.Argb8888!, false)!
            : Android.Graphics.Bitmap.CreateScaledBitmap(decoded, widthDots, height, true)!;
        return Task.FromResult(FromBitmap(scaled));
    }

    private static Pixels FromBitmap(Android.Graphics.Bitmap bitmap)
    {
        var px = new int[bitmap.Width * bitmap.Height];
        bitmap.GetPixels(px, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
        return new Pixels(px, bitmap.Width, bitmap.Height);
    }
#elif WINDOWS
    /// <summary>
    /// Set by the app's root component: draws HTML inside the running WebView2 and returns a PNG
    /// data URI. Null while no window is open.
    /// </summary>
    public static Func<string, int, int, Task<string?>>? JsRenderer { get; set; }

    public static async Task<Pixels> RenderHtmlAsync(string html, int widthDots, int cssWidth)
    {
        var render = JsRenderer ?? throw new InvalidOperationException("The app window is not open, so the ticket cannot be drawn.");
        var dataUri = await render(html, widthDots, cssWidth)
            ?? throw new InvalidOperationException("The ticket page did not render.");
        var comma = dataUri.IndexOf(',');
        return await DecodePngAsync(Convert.FromBase64String(comma >= 0 ? dataUri[(comma + 1)..] : dataUri), widthDots);
    }

    public static async Task<Pixels> DecodePngAsync(byte[] png, int widthDots)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var width = (uint)widthDots;
        var height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * (double)widthDots / decoder.PixelWidth));
        var transform = new Windows.Graphics.Imaging.BitmapTransform
        {
            ScaledWidth = width,
            ScaledHeight = height,
            InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant,
        };
        var data = await decoder.GetPixelDataAsync(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Straight, transform,
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        var bgra = data.DetachPixelData();
        var px = new int[width * height];
        for (int i = 0, j = 0; i < px.Length; i++, j += 4)
            px[i] = (bgra[j + 3] << 24) | (bgra[j + 2] << 16) | (bgra[j + 1] << 8) | bgra[j];
        return new Pixels(px, (int)width, (int)height);
    }
#endif
}
