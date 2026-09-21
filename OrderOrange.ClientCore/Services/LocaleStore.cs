using Microsoft.JSInterop;

namespace OrderOrange.ClientCore.Services;

/// <summary>Where the chosen UI language is remembered between visits.</summary>
public interface ILocaleStore
{
    Task<string?> GetAsync();
    Task SetAsync(string locale);
}

/// <summary>
/// Plain browser localStorage — what a WebAssembly app has. A server app may swap in
/// the data-protected store from OrderOrange.ClientCore.Server instead.
/// </summary>
public sealed class JsLocaleStore(IJSRuntime js) : ILocaleStore
{
    private const string Key = "mf.lang";

    public async Task<string?> GetAsync()
    {
        try { return await js.InvokeAsync<string?>("localStorage.getItem", Key); }
        catch { return null; }   // JS not reachable yet (prerender) — keep the default
    }

    public async Task SetAsync(string locale)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", Key, locale); }
        catch { /* best-effort persistence */ }
    }
}
