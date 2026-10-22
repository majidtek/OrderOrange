using Android.App;
using Android.Content;
using Android.Content.PM;

namespace OrderOrange.PartnerApp;

/// <summary>Catches orderorange-partner://auth?code=… when the browser sign-in comes back to the app.</summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter([Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "orderorange-partner", DataHost = "auth")]
public class WebAuthCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
