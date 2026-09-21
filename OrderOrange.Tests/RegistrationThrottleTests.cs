using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// One network address may only create so many accounts per day. Without the cap, a
/// script mints fake partners and review-farm customers without limit. TestServer gives
/// every request the same (empty) address, so all requests share one bucket — which is
/// exactly what makes the cap observable here.
/// </summary>
public class RegistrationThrottleTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RegistrationThrottleTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TheDailyCapIsEnforcedAcrossAllRegistrationRoutes()
    {
        // A tiny limit so the test proves the mechanism, not endurance.
        var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:MaxRegistrationsPerIpPerDay", "2"));
        var client = factory.CreateClient();

        // Two go through…
        (await client.PostAsJsonAsync("api/auth/register",
            new RegisterRequest("Cap One", "cap.one@test.com", "", "secret1", FormToken.Create()))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("api/auth/register",
            new RegisterRequest("Cap Two", "cap.two@test.com", "", "secret1", FormToken.Create()))).EnsureSuccessStatusCode();

        // …the third is refused, and so is a PARTNER application from the same address —
        // the cap counts accounts, not endpoints, or a script would just rotate routes.
        var third = await client.PostAsJsonAsync("api/auth/register",
            new RegisterRequest("Cap Three", "cap.three@test.com", "", "secret1", FormToken.Create()));
        Assert.Equal((HttpStatusCode)429, third.StatusCode);

        var partner = await client.PostAsJsonAsync("api/auth/register-partner",
            new RegisterPartnerRequest("Cap Partner", "cap.partner@test.com", "99999999", "secret1", "Cap Store", "Qurum", Gate: FormToken.Create()));
        Assert.Equal((HttpStatusCode)429, partner.StatusCode);
    }

    [Fact]
    public async Task SigningInIsNeverThrottled_OnlyCreation()
    {
        var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:MaxRegistrationsPerIpPerDay", "1"));
        var client = factory.CreateClient();

        (await client.PostAsJsonAsync("api/auth/register",
            new RegisterRequest("Only One", "only.one@test.com", "", "secret1", FormToken.Create()))).EnsureSuccessStatusCode();

        // The cap is used up — but signing in to EXISTING accounts still works freely.
        for (var i = 0; i < 5; i++)
        {
            var login = await client.PostAsJsonAsync("api/auth/login",
                new LoginRequest("only.one@test.com", "secret1"));
            login.EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task PartnerAppliesWithPasswordAndSignsInWithIt()
    {
        var client = _factory.CreateClient();

        // The partner portal's application: password chosen up front…
        (await client.PostAsJsonAsync("api/auth/register-partner",
            new RegisterPartnerRequest("Pw Partner", "pw.partner@test.com", "91234567", "chosen-pw-9", "Pw Store", "Seeb", Gate: FormToken.Create())))
            .EnsureSuccessStatusCode();

        // …and it is the working credential immediately.
        var login = await client.PostAsJsonAsync("api/auth/login",
            new LoginRequest("pw.partner@test.com", "chosen-pw-9"));
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(UserRole.RestaurantOwner, session!.Role);
        Assert.Equal("Pw Store", session.RestaurantName);
    }
}
