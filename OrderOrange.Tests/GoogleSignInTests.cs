using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// "Sign in with Google" hands the server a token the browser could have made up. These
/// pin the parts that must never be taken on trust: an unverified credential gets nobody
/// in, and the switch that keeps strangers out of the staff portals stays wired up.
///
/// Forging a genuinely Google-signed token is impossible by design, so the happy path is
/// not reachable from a test — what is testable, and what actually protects the accounts,
/// is that everything else is refused.
/// </summary>
public class GoogleSignInTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public GoogleSignInTests(ApiFactory factory) => _factory = factory;

    private const string ClientId = "test-client-id.apps.googleusercontent.com";

    /// <summary>The API as it runs once a client id has been configured.</summary>
    private HttpClient Configured() =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Google:ClientId", ClientId)).CreateClient();

    /// <summary>
    /// Google switched off. Stated explicitly rather than leaning on whatever appsettings
    /// happens to hold — the live client id lives there now, and a test that quietly
    /// changes meaning when configuration changes is worse than no test.
    /// </summary>
    private HttpClient NotConfigured() =>
        _factory.WithWebHostBuilder(b => b.UseSetting("Google:ClientId", "")).CreateClient();

    [Fact]
    public async Task WithNoClientId_TheAppsAreToldToHideTheButton()
    {
        var config = await NotConfigured().GetFromJsonAsync<GoogleAuthConfig>("api/auth/google/config");

        Assert.NotNull(config);
        Assert.Null(config!.ClientId);
    }

    [Fact]
    public async Task WithNoClientId_SignInIsRefusedRatherThanHalfAttempted()
    {
        var response = await NotConfigured()
            .PostAsJsonAsync("api/auth/google", new GoogleLoginRequest("anything"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task OnceConfigured_TheAppsAreGivenTheClientId()
    {
        var config = await Configured().GetFromJsonAsync<GoogleAuthConfig>("api/auth/google/config");

        Assert.Equal(ClientId, config!.ClientId);
    }

    /// <summary>The whole point: a token that Google did not sign gets nobody in.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("eyJhbGciOiJub25lIn0.eyJlbWFpbCI6ImFkbWluQG9yZGVyb3JhbmdlLmNvbSJ9.")]
    public async Task AForgedCredentialIsRejected(string idToken)
    {
        var response = await Configured().PostAsJsonAsync("api/auth/google", new GoogleLoginRequest(idToken));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The third case above is the one that matters most: it is a syntactically valid JWT
    /// claiming to be an administrator, signed with "alg: none". Accepting it would hand
    /// the admin panel to anyone who can type. It must fail like any other garbage.
    /// </summary>
    [Fact]
    public async Task AnUnsignedTokenClaimingAnAdminAddress_CreatesNothing()
    {
        var client = Configured();
        const string forged = "eyJhbGciOiJub25lIn0.eyJlbWFpbCI6ImFkbWluQG9yZGVyb3JhbmdlLmNvbSIsImVtYWlsX3ZlcmlmaWVkIjp0cnVlfQ.";

        var response = await client.PostAsJsonAsync("api/auth/google", new GoogleLoginRequest(forged));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // And no account was conjured up along the way.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheEndpointIsAnonymous_SoASignedOutVisitorCanReachIt()
    {
        // A 401 for the credential is right; a 401 for missing OUR token would mean the
        // endpoint was locked behind the very sign-in it is supposed to perform.
        var response = await Configured().PostAsJsonAsync("api/auth/google", new GoogleLoginRequest("not-a-token"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Google", body);
    }
}
