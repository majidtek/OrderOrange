using System.Net.Http.Headers;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

public static class TestClient
{
    /// <summary>Signs in as a seeded demo account and attaches the bearer token.</summary>
    public static async Task<LoginResponse> SignInAsync(this HttpClient client, string email, string password = "Pas_123")
    {
        var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return login;
    }

    public static void SignOut(this HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = null;
}
