using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using OrderOrange.ApiServer.Data;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Signing in with a code emailed to you. A six-digit code is a small enough space that
/// the rules around it ARE the security: it must expire, it must die after a few wrong
/// guesses, it cannot be requested over and over, and asking for one must not reveal who
/// has an account here. These pin all four.
/// </summary>
public class EmailCodeSignInTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EmailCodeSignInTests(ApiFactory factory) => _factory = factory;

    /// <summary>The API with an SMTP host configured — sending fails, which is fine here:
    /// what matters is that the code was issued and the rules around it hold.</summary>
    private HttpClient WithEmail() =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Smtp:Host", "localhost.invalid")).CreateClient();

    /// <summary>
    /// Email switched off. Stated explicitly rather than leaning on whatever appsettings
    /// happens to hold — the live SMTP details live there now, and a test that quietly
    /// changes meaning when configuration changes is worse than no test.
    /// </summary>
    private HttpClient WithoutEmail() =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Smtp:Host", "")).CreateClient();

    [Fact]
    public async Task WithNoSmtp_TheAppsAreToldEmailSignInIsUnavailable()
    {
        var methods = await WithoutEmail().GetFromJsonAsync<AuthMethods>("api/auth/methods");

        Assert.NotNull(methods);
        Assert.False(methods!.EmailCode);
    }

    [Fact]
    public async Task WithNoSmtp_RequestingACodeSaysSoRatherThanPretending()
    {
        var response = await WithoutEmail()
            .PostAsJsonAsync("api/auth/otp/request", new OtpRequest("someone@example.com"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    /// <summary>
    /// The reply must look identical for an address that has an account and one that does
    /// not. Otherwise the sign-in box becomes a way to check who is a customer here.
    /// </summary>
    [Fact]
    public async Task TheReplyDoesNotRevealWhetherTheAccountExists()
    {
        var client = WithEmail();

        var known = await client.PostAsJsonAsync("api/auth/otp/request", new OtpRequest("demo@orderorange.com"));
        var unknown = await client.PostAsJsonAsync("api/auth/otp/request", new OtpRequest("nobody-here-at-all@example.com"));

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(
            await known.Content.ReadAsStringAsync(),
            await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AsecondRequestForTheSameAddressIsThrottled()
    {
        var client = WithEmail();
        const string address = "throttle-me@example.com";

        var first = await client.PostAsJsonAsync("api/auth/otp/request", new OtpRequest(address));
        var second = await client.PostAsJsonAsync("api/auth/otp/request", new OtpRequest(address));

        var firstResult = await first.Content.ReadFromJsonAsync<OtpRequestResult>();
        var secondResult = await second.Content.ReadFromJsonAsync<OtpRequestResult>();

        Assert.True(firstResult!.Sent);
        Assert.False(secondResult!.Sent);                    // "wait before asking again"
        Assert.InRange(secondResult.RetryAfterSeconds, 1, 60);
    }

    [Fact]
    public async Task AWrongCodeIsRefused()
    {
        var client = WithEmail();
        const string address = "wrong-code@example.com";
        await client.PostAsJsonAsync("api/auth/otp/request", new OtpRequest(address));

        var response = await client.PostAsJsonAsync("api/auth/otp/verify", new OtpVerifyRequest(address, "000000"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task VerifyingWithoutEverAskingForACodeIsRefused()
    {
        var response = await WithEmail()
            .PostAsJsonAsync("api/auth/otp/verify", new OtpVerifyRequest("never-asked@example.com", "123456"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvalidAddressesAreRejectedBeforeAnythingIsSent()
    {
        var client = WithEmail();

        foreach (var bad in new[] { "", "nope", "no-at-sign.com", "a@b" })
        {
            var response = await client.PostAsJsonAsync("api/auth/otp/request", new OtpRequest(bad));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    // ---------- The store's own rules, exercised directly ----------

    [Fact]
    public void CodesAreSixDigitsAndNotPredictable()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => LoginCodeStore.NewCode()).ToList();

        Assert.All(codes, c => Assert.Matches("^[0-9]{6}$", c));

        // A broken generator returning a constant, or a tight sequence, would show here.
        Assert.True(codes.Distinct().Count() > 150, "codes repeat far too often to be random");
    }

    [Fact]
    public void TheLifetimeAndAttemptLimitStayTight()
    {
        // These are the numbers that make a six-digit code safe. If someone widens them,
        // this test should be the thing that argues back.
        Assert.True(LoginCodeStore.Lifetime <= TimeSpan.FromMinutes(15));
        Assert.True(LoginCodeStore.MaxAttempts <= 6);
        Assert.True(LoginCodeStore.ResendInterval >= TimeSpan.FromSeconds(30));
    }
}
