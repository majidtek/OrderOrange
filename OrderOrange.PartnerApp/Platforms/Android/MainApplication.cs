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
        // No WebView.EnableSlowWholeDocumentDraw() here: it is process-wide and would also change
        // how the portal's own WebView paints and scrolls. The printer's off-screen WebView is
        // laid out to the full height of the ticket instead, so it draws the whole ticket anyway.
        base.OnCreate();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
