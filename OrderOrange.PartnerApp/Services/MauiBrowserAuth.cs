using Microsoft.Maui.Authentication;
using OrderOrange.ClientCore.Services;

namespace OrderOrange.PartnerApp.Services;

/// <summary>
/// "Continue with Google" for the app: the portal opens in the device browser (a Chrome
/// custom tab on Android), the person signs in there, and the page comes back to the app
/// through orderorange-partner://auth (WebAuthCallbackActivity).
/// </summary>
public sealed class MauiBrowserAuth : IBrowserAuth
{
    public async Task<IReadOnlyDictionary<string, string>?> AuthenticateAsync(Uri start, Uri callback)
    {
        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions { Url = start, CallbackUrl = callback });
            return result.Properties.ToDictionary(p => p.Key, p => p.Value);
        }
        catch (TaskCanceledException)
        {
            return null;     // the browser was closed without signing in
        }
    }
}
