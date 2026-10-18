using Android.App;
using Android.Runtime;

namespace OrderOrange.AndroidTill;

[Application(UsesCleartextTraffic = true)]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) { }

    public override void OnCreate()
    {
        // Must run before the first WebView exists: lets an off-screen WebView draw its WHOLE
        // document into a bitmap, not just the part that would be on screen.
        Android.Webkit.WebView.EnableSlowWholeDocumentDraw();
        base.OnCreate();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
