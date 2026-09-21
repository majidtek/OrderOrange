using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// "Open as partner": an administrator mints a single-use code and the partner app trades
/// it for a session as that owner. The boundaries ARE the feature — only admins mint,
/// admins can never be the target, and a code dies on first use.
/// </summary>
public class ImpersonationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ImpersonationTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        await client.SignInAsync("admin@majidfood.com");
        return client;
    }

    private async Task<AdminRestaurantDto> AnyStoreAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<List<AdminRestaurantDto>>("api/admin/restaurants"))!.First();

    [Fact]
    public async Task AdminEntersThePartnerPortalAsTheOwner_NoPasswordInvolved()
    {
        var admin = await AdminAsync();
        var store = await AnyStoreAsync(admin);

        var minted = await admin.PostAsJsonAsync($"api/admin/impersonate/{store.OwnerUserId}", new { });
        minted.EnsureSuccessStatusCode();
        var code = await minted.Content.ReadFromJsonAsync<ImpersonationCodeDto>();

        // A completely fresh, anonymous client — exactly what the partner app's browser is.
        var partnerApp = _factory.CreateClient();
        var redeemed = await partnerApp.PostAsJsonAsync("api/auth/impersonate",
            new ImpersonationRedeemRequest(code!.Code));
        redeemed.EnsureSuccessStatusCode();

        var session = await redeemed.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(UserRole.RestaurantOwner, session!.Role);
        Assert.Equal(store.OwnerEmail, session.Email);
        Assert.Equal(store.Id, session.RestaurantId);   // lands scoped to THEIR store
    }

    [Fact]
    public async Task ACodeDiesOnFirstUse()
    {
        var admin = await AdminAsync();
        var store = await AnyStoreAsync(admin);
        var code = await (await admin.PostAsJsonAsync($"api/admin/impersonate/{store.OwnerUserId}", new { }))
            .Content.ReadFromJsonAsync<ImpersonationCodeDto>();

        var app = _factory.CreateClient();
        (await app.PostAsJsonAsync("api/auth/impersonate", new ImpersonationRedeemRequest(code!.Code)))
            .EnsureSuccessStatusCode();

        // Replaying the same code — a copied link, a stolen URL from a screenshot — fails.
        var again = await app.PostAsJsonAsync("api/auth/impersonate", new ImpersonationRedeemRequest(code.Code));
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
    }

    [Fact]
    public async Task OnlyAdministratorsCanMintCodes()
    {
        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");

        var response = await owner.PostAsJsonAsync("api/admin/impersonate/1", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnAdministratorCanNeverBeTheTarget()
    {
        var admin = await AdminAsync();
        var admins = await admin.GetFromJsonAsync<List<UserDto>>("api/admin/users?role=3");
        var otherAdmin = admins!.First();

        // Even an admin impersonating an admin is refused — this tool must never be a
        // way to act under a colleague's name.
        var response = await admin.PostAsJsonAsync($"api/admin/impersonate/{otherAdmin.Id}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GarbageCodesAreRefused()
    {
        var app = _factory.CreateClient();
        var response = await app.PostAsJsonAsync("api/auth/impersonate",
            new ImpersonationRedeemRequest("not-a-real-code"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
