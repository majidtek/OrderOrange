using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Only the emails on Admin:AllowedEmails may sign in as an administrator — on every
/// route in, because a gate on the password door means nothing if Google and the email
/// code walk straight past it. Production lists exactly one address.
/// </summary>
public class AdminAllowlistTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AdminAllowlistTests(ApiFactory factory) => _factory = factory;

    private HttpClient With(string allowlist) =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Admin:AllowedEmails", allowlist)).CreateClient();

    [Fact]
    public async Task AnUnlistedAdministratorIsRefused()
    {
        var client = With("someone.else@orderorange.com");

        var response = await client.PostAsJsonAsync("api/auth/login",
            new LoginRequest("admin@majidfood.com", "Pas_123"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("not authorised", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheListedAdministratorGetsIn()
    {
        var client = With("admin@majidfood.com");

        var response = await client.PostAsJsonAsync("api/auth/login",
            new LoginRequest("admin@majidfood.com", "Pas_123"));

        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(UserRole.Administrator, session!.Role);
    }

    [Fact]
    public async Task TheListIsCaseInsensitive_LikeEveryOtherEmailPath()
    {
        var client = With("ADMIN@MajidFood.COM");

        var response = await client.PostAsJsonAsync("api/auth/login",
            new LoginRequest("admin@majidfood.com", "Pas_123"));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task OrdinaryAccountsAreUntouchedByTheList()
    {
        // The list restricts ADMIN sessions only — a customer, partner or rider signs in
        // exactly as before, whatever the list says.
        var client = With("only.this.admin@orderorange.com");

        foreach (var email in new[] { "ahmed@majidfood.com", "marco@majidfood.com", "salim.driver@majidfood.com" })
        {
            var response = await client.PostAsJsonAsync("api/auth/login", new LoginRequest(email, "Pas_123"));
            response.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task AnEmptyListMeansNoRestriction()
    {
        var client = With("");

        var response = await client.PostAsJsonAsync("api/auth/login",
            new LoginRequest("admin@majidfood.com", "Pas_123"));

        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The other doors: the email-code route must refuse an unlisted admin too. The code
    /// is planted straight into the store — a CORRECT code, so the only thing left that
    /// can refuse the sign-in is the gate itself.
    /// </summary>
    [Fact]
    public async Task TheEmailCodeRouteIsGatedAsWell()
    {
        var factory = _factory.WithWebHostBuilder(b =>
            b.UseSetting("Admin:AllowedEmails", "someone.else@orderorange.com"));
        var client = factory.CreateClient();

        var codes = factory.Services.GetRequiredService<OrderOrange.ApiServer.Data.LoginCodeStore>();
        await codes.IssueAsync("admin@majidfood.com", "123456");

        var response = await client.PostAsJsonAsync("api/auth/otp/verify",
            new OtpVerifyRequest("admin@majidfood.com", "123456"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("not authorised", await response.Content.ReadAsStringAsync());
    }
}
