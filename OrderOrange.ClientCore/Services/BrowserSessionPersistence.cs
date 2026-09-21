using System.Text.Json;
using OrderOrange.Shared;
using Microsoft.JSInterop;

namespace OrderOrange.ClientCore.Services;

/// <summary>
/// Stores the session in the browser's sessionStorage so a reload doesn't sign you out.
/// sessionStorage rather than localStorage on purpose: it survives F5 and in-tab navigation
/// but dies when the tab closes, so a signed-in session isn't left on disk on a shared machine.
/// </summary>
public sealed class BrowserSessionPersistence(IJSRuntime js) : ISessionPersistence
{
    private const string Key = "majidfood.session";
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// "Keep me signed in": the session goes to localStorage (survives closing the
    /// browser) instead of sessionStorage (this tab only). Set by the login screen;
    /// a session found in localStorage keeps it on so later mirrors stay there.
    /// </summary>
    public static bool Remember { get; set; }

    public async Task<LoginResponse?> LoadAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
        if (!string.IsNullOrWhiteSpace(json)) Remember = true;
        else json = await js.InvokeAsync<string?>("sessionStorage.getItem", Key);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<LoginResponse>(json, Opts);
        }
        catch (JsonException)
        {
            // Left over from an older shape of LoginResponse — drop it rather than loop.
            await ClearAsync();
            return null;
        }
    }

    public async Task SaveAsync(LoginResponse session)
    {
        var json = JsonSerializer.Serialize(session, Opts);
        if (Remember)
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key, json);
            await js.InvokeVoidAsync("sessionStorage.removeItem", Key);
        }
        else
        {
            await js.InvokeVoidAsync("sessionStorage.setItem", Key, json);
            await js.InvokeVoidAsync("localStorage.removeItem", Key);
        }
    }

    public async Task ClearAsync()
    {
        Remember = false;
        await js.InvokeVoidAsync("sessionStorage.removeItem", Key);
        await js.InvokeVoidAsync("localStorage.removeItem", Key);
    }
}
