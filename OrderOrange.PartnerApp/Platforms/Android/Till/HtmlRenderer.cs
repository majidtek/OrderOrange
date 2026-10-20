using Android.Content;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using ABitmap = Android.Graphics.Bitmap;
using ACanvas = Android.Graphics.Canvas;
using AColor = Android.Graphics.Color;
using AWebView = Android.Webkit.WebView;

namespace OrderOrange.PartnerApp.Till.Printing;

/// <summary>
/// Rasterises ticket HTML with an off-screen Android WebView — the same Chromium engine
/// the web POS prints from, so Arabic/Persian shaping, the designer's CSS and the logo
/// all come out as designed. The page is zoomed so its CSS width fills the printer's
/// dot width (80 mm → 576, 58 mm → 384) and then drawn into a bitmap of that width.
/// </summary>
public static class HtmlRenderer
{
    private static bool? _hasArabicFont;
    private static bool _wholeDocumentDraw;

    /// <param name="widthPx">Printer dots across the paper.</param>
    /// <param name="cssWidth">The CSS pixel width of the ticket's root block (302/219 receipt, 270/196 kitchen).</param>
    public static Task<ABitmap> RenderAsync(string html, int widthPx, int cssWidth, int timeoutMs = 15000) =>
        MainThread.InvokeOnMainThreadAsync(() => RenderOnMainAsync(html, widthPx, cssWidth, timeoutMs));

    /// <summary>
    /// Chromium only paints a WebView that hangs in a VISIBLE window. With the till in the
    /// background (the web POS open in the terminal's browser, say) the activity's window is
    /// stopped and the ticket came out as a small blank strip (Majed 2026-10-18). An overlay
    /// window — "Display over other apps", granted once — is visible to Chromium even then.
    /// </summary>
    public static bool CanUseOverlay()
    {
        try { return !OperatingSystem.IsAndroidVersionAtLeast(23) || Android.Provider.Settings.CanDrawOverlays(Platform.AppContext); }
        catch { return false; }
    }

    private static bool AppInForeground()
    {
        try
        {
            var activity = Platform.CurrentActivity;
            var decor = activity?.Window?.DecorView;
            return activity is not null && !activity.IsFinishing && decor is not null
                   && decor.IsShown && decor.WindowVisibility == ViewStates.Visible;
        }
        catch { return false; }
    }

    private static async Task<ABitmap> RenderOnMainAsync(string html, int widthPx, int cssWidth, int timeoutMs)
    {
        if (!_wholeDocumentDraw)
        {
            // Must run before the first WebView exists: lets draw() paint the whole document, not just the viewport.
            try { AWebView.EnableSlowWholeDocumentDraw(); } catch { }
            _wholeDocumentDraw = true;
        }

        var activity = Platform.CurrentActivity;
        Context ctx = activity ?? Platform.AppContext;
        var density = ctx.Resources?.DisplayMetrics?.Density ?? 1f;
        // Zoom so that cssWidth CSS px span widthPx device px. Floor → the viewport is never
        // narrower than the ticket, so nothing wraps or clips.
        var zoom = widthPx / density / cssWidth;
        var percent = Math.Max(10, (int)Math.Floor(zoom * 100));

        var wv = new AWebView(ctx);
        wv.SetLayerType(LayerType.Software, null);
        var s = wv.Settings;
        s.JavaScriptEnabled = true;
        s.UseWideViewPort = false;
        s.LoadWithOverviewMode = false;
        s.TextZoom = 100;
        s.BuiltInZoomControls = false;
        s.DisplayZoomControls = false;
        s.DefaultTextEncodingName = "utf-8";
        s.AllowFileAccess = true;
        wv.SetBackgroundColor(AColor.White);
        wv.VerticalScrollBarEnabled = false;
        wv.HorizontalScrollBarEnabled = false;
        wv.SetInitialScale(percent);

        var client = new Client();
        wv.SetWebViewClient(client);

        // Where the WebView hangs while it paints:
        //   • app in front → off-screen inside the activity's window (the usual case);
        //   • app in the background and "Display over other apps" granted → an almost invisible
        //     overlay window of its own, which Chromium treats as visible;
        //   • otherwise detached with a manual layout (may paint blank — detected below).
        const int provisionalHeight = 4000;
        ViewGroup? parent = null;
        FrameLayout? overlay = null;
        IWindowManager? wm = null;
        var foreground = AppInForeground();
        if (foreground && activity?.Window?.DecorView is ViewGroup decor)
        {
            parent = decor;
            wv.TranslationX = -(widthPx + 100);
            parent.AddView(wv, new FrameLayout.LayoutParams(widthPx, provisionalHeight));
        }
        else if (CanUseOverlay() && Platform.AppContext.GetSystemService(Context.WindowService) is IWindowManager manager)
        {
            wm = manager;
            overlay = new FrameLayout(Platform.AppContext);
            var type = OperatingSystem.IsAndroidVersionAtLeast(26) ? WindowManagerTypes.ApplicationOverlay : WindowManagerTypes.Phone;
            var lp = new WindowManagerLayoutParams(widthPx, provisionalHeight, type,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchable | WindowManagerFlags.NotTouchModal | WindowManagerFlags.LayoutNoLimits,
                Android.Graphics.Format.Translucent)
            {
                Gravity = GravityFlags.Top | GravityFlags.Left,
                X = 0, Y = 0,
                Alpha = 0.02f,   // present for Chromium, invisible to the cashier
            };
            try
            {
                wm.AddView(overlay, lp);
                overlay.AddView(wv, new FrameLayout.LayoutParams(widthPx, provisionalHeight));
                parent = overlay;
            }
            catch (Exception ex)
            {
                Services.TillLog.Write($"overlay window refused: {ex.Message}");
                overlay = null; wm = null;
            }
        }
        if (parent is null) Services.TillLog.Write("rendering detached (app in background, no overlay permission) — may come out blank");
        LayoutTo(wv, widthPx, provisionalHeight);

        try
        {
            wv.LoadDataWithBaseURL("file:///android_asset/", await InjectAsync(html), "text/html", "utf-8", null);
            var finished = await Task.WhenAny(client.Finished.Task, Task.Delay(timeoutMs)) == client.Finished.Task;
            if (!finished) throw new InvalidOperationException("The ticket page did not render.");
            await Task.Delay(250);   // let the logo / QR images and fonts settle

            var (cssW, cssH) = await MeasureAsync(wv);
            var scale = client.Scale > 0 ? client.Scale : wv.Scale;
            if (scale <= 0) scale = density * zoom;

            var contentW = cssW * scale;
            var contentH = cssH * scale;
            // If the initial scale was ignored (older WebViews), correct with a canvas scale.
            var k = contentW > 1 ? widthPx / contentW : 1f;
            if (Math.Abs(k - 1f) < 0.02f) k = 1f;

            var layoutH = Math.Clamp((int)Math.Ceiling(contentH) + 8, 16, 12000);
            var bitmapH = Math.Clamp((int)Math.Ceiling(contentH * k) + 8, 16, 12000);

            if (parent is not null)
            {
                wv.LayoutParameters = new FrameLayout.LayoutParams(widthPx, layoutH);
                wv.RequestLayout();
                await Task.Delay(50);
            }
            LayoutTo(wv, widthPx, layoutH);

            // Draw; a blank result means Chromium has not painted yet (or cannot, detached) —
            // give it a few more chances before giving up, and never send white paper.
            ABitmap? bmp = null;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                bmp?.Recycle();
                bmp = ABitmap.CreateBitmap(widthPx, bitmapH, ABitmap.Config.Argb8888!)!;
                var canvas = new ACanvas(bmp);
                canvas.DrawColor(AColor.White);
                if (k != 1f) canvas.Scale(k, k);
                wv.Draw(canvas);
                if (!IsBlank(bmp)) return bmp;
                wv.Invalidate();
                await Task.Delay(400 + attempt * 300);
            }
            bmp?.Recycle();
            throw new InvalidOperationException(parent is null
                ? "The ticket rendered blank: the till is in the background and \"Display over other apps\" is not allowed — grant it in Settings → Running in the background."
                : "The ticket rendered blank — will retry on the next check.");
        }
        finally
        {
            try { parent?.RemoveView(wv); } catch { }
            try { if (overlay is not null) wm?.RemoveView(overlay); } catch { }
            try { wv.StopLoading(); wv.Destroy(); } catch { }
        }
    }

    /// <summary>True when nothing darker than near-white was painted (sampled every 4th pixel).</summary>
    private static bool IsBlank(ABitmap bmp)
    {
        var w = bmp.Width; var h = bmp.Height;
        var px = new int[w * h];
        bmp.GetPixels(px, 0, w, 0, 0, w, h);
        for (var i = 0; i < px.Length; i += 4)
        {
            var c = px[i];
            if (((c >> 24) & 0xFF) < 16) continue;   // transparent counts as white
            if (((c >> 16) & 0xFF) < 200 || ((c >> 8) & 0xFF) < 200 || (c & 0xFF) < 200) return false;
        }
        return true;
    }

    /// <summary>Opens Android's "Display over other apps" page for this app (no-op when already allowed).</summary>
    public static void RequestOverlay()
    {
        if (CanUseOverlay()) return;
        try
        {
            var ctx = Platform.AppContext;
            var intent = new Intent(Android.Provider.Settings.ActionManageOverlayPermission, Android.Net.Uri.Parse($"package:{ctx.PackageName}"))
                .AddFlags(ActivityFlags.NewTask);
            ctx.StartActivity(intent);
        }
        catch (Exception ex) { Services.TillLog.Write($"overlay settings: {ex.Message}"); }
    }

    private static void LayoutTo(AWebView wv, int w, int h)
    {
        wv.Measure(Android.Views.View.MeasureSpec.MakeMeasureSpec(w, MeasureSpecMode.Exactly),
                   Android.Views.View.MeasureSpec.MakeMeasureSpec(h, MeasureSpecMode.Exactly));
        wv.Layout(0, 0, w, h);
    }

    /// <summary>The root block's width and the document height, both in CSS px.</summary>
    private static async Task<(float W, float H)> MeasureAsync(AWebView wv)
    {
        const string js = "(function(){var b=document.body;var e=b.firstElementChild||b;var r=e.getBoundingClientRect();" +
                          "var h=Math.max(b.scrollHeight,document.documentElement.scrollHeight,r.bottom);return r.width+'x'+h;})()";
        var cb = new JsResult();
        wv.EvaluateJavascript(js, cb);
        var raw = await Task.WhenAny(cb.Task, Task.Delay(3000)) == cb.Task ? cb.Task.Result : null;
        if (raw is not null)
        {
            raw = raw.Trim('"');
            var parts = raw.Split('x');
            if (parts.Length == 2
                && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
                && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)
                && w > 0 && h > 0)
                return (w, h);
        }
        return (0, wv.ContentHeight);
    }

    /// <summary>
    /// Adds the few rules the paper needs that the designer's screen CSS leaves to print
    /// media (no frame unless the owner chose one) and, when the app ships an Arabic font,
    /// offers it for the Arabic ranges only so the designed Latin fonts stay as chosen.
    /// </summary>
    private static async Task<string> InjectAsync(string html)
    {
        var extra = "<style>.rcpt.fr-none{border:none!important;border-radius:0}.rcpt{margin:0!important}</style>";
        _hasArabicFont ??= await AssetExistsAsync("arabic.ttf");
        if (_hasArabicFont == true)
        {
            extra += "<style>@font-face{font-family:TillArabic;src:url('arabic.ttf');unicode-range:U+0600-06FF,U+0750-077F,U+08A0-08FF,U+FB50-FDFF,U+FE70-FEFF}" +
                     ".rcpt{font-family:TillArabic,Consolas,\"Courier New\",monospace}" +
                     ".rcpt.f-sans{font-family:TillArabic,\"Segoe UI\",system-ui,sans-serif}" +
                     ".rcpt.f-serif{font-family:TillArabic,Georgia,\"Times New Roman\",serif}" +
                     ".rcpt.f-cairo{font-family:TillArabic,\"Cairo\",\"Segoe UI\",sans-serif}" +
                     ".tk,.tt{font-family:TillArabic,\"Segoe UI\",Arial,sans-serif}</style>";
        }
        var at = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return at < 0 ? extra + html : html.Insert(at, extra);
    }

    private static async Task<bool> AssetExistsAsync(string name)
    {
        try { return await FileSystem.AppPackageFileExistsAsync(name); }
        catch { return false; }
    }

    private sealed class Client : WebViewClient
    {
        public readonly TaskCompletionSource<bool> Finished = new();
        public float Scale;

        public override void OnPageFinished(AWebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            Finished.TrySetResult(true);
        }

        public override void OnScaleChanged(AWebView? view, float oldScale, float newScale)
        {
            base.OnScaleChanged(view, oldScale, newScale);
            Scale = newScale;
        }
    }

    private sealed class JsResult : Java.Lang.Object, IValueCallback
    {
        private readonly TaskCompletionSource<string?> _tcs = new();
        public Task<string?> Task => _tcs.Task;
        public void OnReceiveValue(Java.Lang.Object? value) => _tcs.TrySetResult(value?.ToString());
    }
}
