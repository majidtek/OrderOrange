namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Opens an address in the device's own browser and waits for it to come back to the app
/// through a callback address (a custom scheme the app owns). Only the OrderOrange Partner
/// app registers one; in a browser there is nothing to register, and the Google button
/// signs in on the page itself.
/// </summary>
public interface IBrowserAuth
{
    /// <summary>The query parameters of the callback, or null when the person closed the browser instead.</summary>
    Task<IReadOnlyDictionary<string, string>?> AuthenticateAsync(Uri start, Uri callback);
}
