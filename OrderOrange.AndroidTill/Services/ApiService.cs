using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderOrange.Shared;

namespace OrderOrange.AndroidTill.Services;

/// <summary>
/// The till's line to the server — the ordinary partner API, nothing else (never SQL or
/// Mongo). A port of the Windows till's ApiService with one addition: a 401 is answered by
/// one silent re-login with the saved credentials, so an expired token never stops printing.
/// </summary>
public sealed class ApiService
{
    // A server addressed by IP (a staging box with no host name) presents a certificate for
    // orderorange.com; that mismatch is accepted ONLY for IP addresses — a named host must validate.
    private static readonly HttpClientHandler Handler = new()
    {
        ServerCertificateCustomValidationCallback = (req, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || (req.RequestUri is { } u && IPAddress.TryParse(u.Host, out _)),
    };
    private readonly HttpClient _http = new(Handler) { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string BaseUrl { get; set; } = "https://api.orderorange.com/";
    public string? Token { get; private set; }
    public string? StoreName { get; private set; }
    public int? RestaurantId { get; private set; }
    public bool SignedIn => Token is not null;
    /// <summary>The last failure's words, for the status line.</summary>
    public string? LastError { get; private set; }
    /// <summary>Asked to get a fresh token after a 401; returns true when signed in again.</summary>
    public Func<Task<bool>>? Relogin { get; set; }

    private Uri Endpoint(string path) =>
        new(new Uri(BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    /// <summary>Signs in with the shop's own credentials. Returns null on success, else why not.</summary>
    public async Task<string?> SignInAsync(string username, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(Endpoint("api/auth/login"),
                new LoginRequest(username, password), Json);
            if (!response.IsSuccessStatusCode) return await ReadErrorAsync(response);

            var session = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
            if (session is null) return "The server sent no session.";
            if (session.Role != UserRole.RestaurantOwner)
                return "This is a shop till — sign in with the business account.";

            Token = session.Token;
            StoreName = session.RestaurantName;
            RestaurantId = session.RestaurantId;
            LastError = null;
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Resumes a session saved from an earlier run.</summary>
    public void UseSession(string token, int restaurantId, string storeName)
    {
        Token = token;
        RestaurantId = restaurantId;
        StoreName = storeName;
    }

    public void SignOut()
    {
        Token = null;
        StoreName = null;
        RestaurantId = null;
    }

    /// <summary>Every business this account may work in — for the switcher.</summary>
    public async Task<List<StoreSummaryDto>> MyStoresAsync() =>
        await GetAsync<List<StoreSummaryDto>>("api/restaurants/my-stores") ?? [];

    /// <summary>
    /// Moves the session into another store the same account owns. The server hands back
    /// a fresh token scoped to that store. Returns null on success, else why not.
    /// </summary>
    public async Task<string?> SwitchStoreAsync(int restaurantId)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint($"api/auth/switch-store/{restaurantId}"));
            Authorize(req);
            using var response = await _http.SendAsync(req);
            if (!response.IsSuccessStatusCode) return await ReadErrorAsync(response);

            var session = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
            if (session is null) return "The server sent no session.";

            Token = session.Token;
            StoreName = session.RestaurantName;
            RestaurantId = session.RestaurantId;
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Everything still in play: new, cooking, ready, on the way. Null = server unreachable.</summary>
    public Task<List<OrderDto>?> BoardAsync() => GetAsync<List<OrderDto>>("api/orders/restaurant/board");

    /// <summary>
    /// The newest closed orders (delivered / cancelled / rejected). A counter sale that is
    /// placed AND finished between two polls never shows on the board — this is where it is
    /// caught so its bill and kitchen ticket still print. Null = server unreachable.
    /// </summary>
    public Task<List<OrderDto>?> HistoryAsync(int take = 15) => GetAsync<List<OrderDto>>($"api/orders/restaurant/history?take={take}");

    /// <summary>The tables with an open, unpaid invoice. Null = server unreachable.</summary>
    public Task<List<StoreTabDto>?> OpenTabsAsync() => GetAsync<List<StoreTabDto>>("api/storetabs");

    /// <summary>The shop's menu — order lines carry product NAMES, so this maps them back to ids.</summary>
    public async Task<List<MenuCategoryDto>> MenuAsync() =>
        await GetAsync<List<MenuCategoryDto>>("api/menu") ?? [];

    /// <summary>The receipt the owner laid out in the partner designer; the stock design when never set.</summary>
    public async Task<ReceiptDesignDto> ReceiptDesignAsync() =>
        await GetAsync<ReceiptDesignDto>("api/restaurants/mine/receipt") ?? ReceiptDesignDto.Default;

    /// <summary>The kitchen ticket the owner laid out in the designer; the stock look when never set.</summary>
    public async Task<KitchenDesignDto> KitchenDesignAsync() =>
        await GetAsync<KitchenDesignDto>("api/restaurants/mine/kitchen-design") ?? KitchenDesignDto.Default;

    /// <summary>The store's own profile — name, address, phone, CR/VAT, kitchen language.</summary>
    public Task<MyRestaurantDto?> ProfileAsync() => GetAsync<MyRestaurantDto>("api/restaurants/mine");

    /// <summary>Product id → "kitchen" | "bar", as drawn on the web portal. Unrouted products go to the kitchen.</summary>
    public async Task<Dictionary<int, string>> PrintRoutesAsync()
    {
        var routes = await GetAsync<List<PrintRouteDto>>("api/restaurants/mine/print-routes") ?? [];
        var map = new Dictionary<int, string>();
        foreach (var r in routes) map[r.MenuItemId] = r.Station;
        return map;
    }

    // ---------- plumbing ----------

    private void Authorize(HttpRequestMessage req)
    {
        if (Token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }

    /// <summary>GET and deserialise; one retry after a silent re-login when the token has expired.</summary>
    private async Task<T?> GetAsync<T>(string path) where T : class
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, Endpoint(path));
                Authorize(req);
                using var response = await _http.SendAsync(req);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && Relogin is not null)
                {
                    if (await Relogin()) continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    LastError = $"{path}: {await ReadErrorAsync(response)}";
                    return null;
                }
                var value = await response.Content.ReadFromJsonAsync<T>(Json);
                LastError = null;
                return value;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }
        return null;
    }

    /// <summary>Pulls the server's own words out of a failure, rather than inventing some.</summary>
    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
                return message.GetString()!;
        }
        catch { /* not json, or empty */ }
        return $"Request failed ({(int)response.StatusCode}).";
    }
}
