using Android.App;
using Android.Runtime;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace OrderOrange.PartnerApp;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    public override void OnCreate()
    {
        // Must run before the first WebView exists: lets the printer's off-screen WebView draw a
        // WHOLE ticket into a bitmap, not just the part that would fit on screen.
        Android.Webkit.WebView.EnableSlowWholeDocumentDraw();
        base.OnCreate();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
