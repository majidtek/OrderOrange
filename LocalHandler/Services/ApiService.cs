using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LocalHandler.Models;
using OrderOrange.Shared;

namespace LocalHandler.Services;

/// <summary>
/// The till's line to the server. Signs in as the shop's partner account and speaks
/// the ordinary partner API — this machine is simply another client, so nothing here
/// needs special server support.
/// </summary>
public sealed class ApiService(Setup setup)
{
    // A server addressed by IP (the staging box, which has no public host name of its own)
    // presents a certificate for orderorange.com; that mismatch is accepted ONLY for IP
    // addresses — a named host must still validate.
    private static readonly HttpClientHandler Handler = new()
    {
        ServerCertificateCustomValidationCallback = (req, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || (req.RequestUri is { } u && System.Net.IPAddress.TryParse(u.Host, out _)),
    };
    private readonly HttpClient _http = new(Handler) { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string? StoreName { get; private set; }
    public int? RestaurantId { get; private set; }
    public bool SignedIn => _http.DefaultRequestHeaders.Authorization is not null;

    private Uri Endpoint(string path) =>
        new(new Uri(setup.ApiBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    /// <summary>Signs in with the shop's own credentials. Returns null on success, else why not.</summary>
    public async Task<string?> SignInAsync(string username, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(Endpoint("api/auth/login"),
                new LoginRequest(username, password), Json);

            if (!response.IsSuccessStatusCode)
                return await ReadErrorAsync(response);

            var session = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
            if (session is null) return "The server sent no session.";
            if (session.Role != UserRole.RestaurantOwner)
                return "This is a shop till — sign in with the business account.";

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            StoreName = session.RestaurantName;
            RestaurantId = session.RestaurantId;
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public void SignOut()
    {
        _http.DefaultRequestHeaders.Authorization = null;
        StoreName = null;
        RestaurantId = null;
    }

    /// <summary>Every business this account may work in — for the switcher.</summary>
    public async Task<List<StoreSummaryDto>> MyStoresAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<StoreSummaryDto>>(Endpoint("api/restaurants/my-stores"), Json) ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Moves the session into another store the same account owns. The server hands back
    /// a fresh token scoped to that store; we swap it in so every later call is scoped
    /// to the new business. Returns null on success, else why not.
    /// </summary>
    public async Task<string?> SwitchStoreAsync(int restaurantId)
    {
        try
        {
            var response = await _http.PostAsync(Endpoint($"api/auth/switch-store/{restaurantId}"), null);
            if (!response.IsSuccessStatusCode) return await ReadErrorAsync(response);

            var session = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
            if (session is null) return "The server sent no session.";

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            StoreName = session.RestaurantName;
            RestaurantId = session.RestaurantId;
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Everything still in play: new, cooking, ready, on the way.</summary>
    public async Task<List<OrderDto>> BoardAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<OrderDto>>(Endpoint("api/orders/restaurant/board"), Json) ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// The tables with an open, unpaid invoice. The till watches these so a round rung
    /// onto a table prints in the kitchen straight away — the order itself is only created
    /// when the table is closed, so without this the kitchen would wait until the bill.
    /// </summary>
    public async Task<List<StoreTabDto>> OpenTabsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<StoreTabDto>>(Endpoint("api/storetabs"), Json) ?? [];
        }
        catch { return []; }
    }

    /// <summary>The shop's menu, used to route each product to a printer.</summary>
    public async Task<List<MenuCategoryDto>> MenuAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<MenuCategoryDto>>(Endpoint("api/menu"), Json) ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// The receipt the owner laid out in the partner panel's invoice designer — the same
    /// design the web POS prints from, so this till prints the very same slip. Falls back
    /// to the stock design if the shop never opened the designer.
    /// </summary>
    public async Task<ReceiptDesignDto> ReceiptDesignAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<ReceiptDesignDto>(Endpoint("api/restaurants/mine/receipt"), Json)
                   ?? ReceiptDesignDto.Default;
        }
        catch { return ReceiptDesignDto.Default; }
    }

    /// <summary>The store's own profile — name, address, phone, CR/VAT — printed on the bill.</summary>
    public async Task<MyRestaurantDto?> ProfileAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<MyRestaurantDto>(Endpoint("api/restaurants/mine"), Json);
        }
        catch { return null; }
    }

    /// <summary>The product → station map the owner drew on the web portal.</summary>
    public async Task<Dictionary<int, string>> PrintRoutesAsync()
    {
        try
        {
            var routes = await _http.GetFromJsonAsync<List<PrintRouteDto>>(
                Endpoint("api/restaurants/mine/print-routes"), Json) ?? [];
            return routes.ToDictionary(r => r.MenuItemId, r => r.Station);
        }
        catch { return []; }
    }

    /// <summary>
    /// A charge the web POS asked this till to run on the card terminal. Consuming —
    /// the server hands it out exactly once.
    /// </summary>
    public async Task<TillChargeDto?> TakeTillChargeAsync()
    {
        try
        {
            var response = await _http.GetAsync(Endpoint("api/orders/till-charge"));
            if (response.StatusCode != System.Net.HttpStatusCode.OK) return null;
            return await response.Content.ReadFromJsonAsync<TillChargeDto>(Json);
        }
        catch { return null; }
    }

    public Task<string?> AcceptAsync(int orderId) => PostAsync($"api/orders/{orderId}/accept");
    public Task<string?> PreparingAsync(int orderId) => PostAsync($"api/orders/{orderId}/preparing");
    public Task<string?> ReadyAsync(int orderId) => PostAsync($"api/orders/{orderId}/ready");
    public Task<string?> PaidAsync(int orderId) => PostAsync($"api/orders/{orderId}/paid");
    public Task<string?> DeliveredAsync(int orderId) => PostAsync($"api/orders/{orderId}/self-delivered");

    private async Task<string?> PostAsync(string path)
    {
        try
        {
            var response = await _http.PostAsync(Endpoint(path), null);
            return response.IsSuccessStatusCode ? null : await ReadErrorAsync(response);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
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
