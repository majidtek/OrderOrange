using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// One partner email, several businesses. The session still works in exactly one store —
/// switching re-issues the token — and the boundary that matters is that a token can
/// never be minted for a store the account does not own.
/// </summary>
public class MultiStoreTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public MultiStoreTests(ApiFactory factory) => _factory = factory;

    /// <summary>Gives marco a second business (idempotent-ish: a fresh factory per class).</summary>
    private async Task<HttpClient> MarcoWithTwoStoresAsync()
    {
        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");
        var response = await admin.PostAsJsonAsync("api/admin/users", new CreateUserRequest(
            "Marco Rossi", "marco@majidfood.com", "+968 9000 1234", "irrelevant1",
            UserRole.RestaurantOwner, null, "Marco's Second Kitchen", 1, StoreType.Grocery));
        response.EnsureSuccessStatusCode();

        var marco = _factory.CreateClient();
        await marco.SignInAsync("marco@majidfood.com");
        return marco;
    }

    [Fact]
    public async Task AdminAddingAPartnerOnAnExistingPartnerEmail_AddsAnotherBusiness()
    {
        var marco = await MarcoWithTwoStoresAsync();

        var stores = await marco.GetFromJsonAsync<List<StoreSummaryDto>>("api/restaurants/my-stores");

        Assert.True(stores!.Count >= 2, $"expected at least 2 businesses, found {stores.Count}");
        Assert.Contains(stores, s => s.Name == "Marco's Second Kitchen" && s.StoreType == StoreType.Grocery);

        // The new business waits for approval like any other.
        Assert.False(stores.First(s => s.Name == "Marco's Second Kitchen").IsApproved);
    }

    [Fact]
    public async Task SwitchingReissuesTheSessionForTheChosenStore()
    {
        var marco = await MarcoWithTwoStoresAsync();
        var stores = await marco.GetFromJsonAsync<List<StoreSummaryDto>>("api/restaurants/my-stores");
        var second = stores!.First(s => s.Name == "Marco's Second Kitchen");

        var response = await marco.PostAsJsonAsync($"api/auth/switch-store/{second.Id}", new { });
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.Equal(second.Id, session!.RestaurantId);
        Assert.Equal("Marco's Second Kitchen", session.RestaurantName);

        // And the new token really scopes the owner endpoints to that store.
        marco.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);
        var mine = await marco.GetFromJsonAsync<MyRestaurantDto>("api/restaurants/mine");
        Assert.Equal(second.Id, mine!.Id);
    }

    [Fact]
    public async Task NobodyCanSwitchIntoSomeoneElsesStore()
    {
        var sara = _factory.CreateClient();
        var login = await sara.SignInAsync("sara@majidfood.com");

        var marco = _factory.CreateClient();
        await marco.SignInAsync("marco@majidfood.com");
        var marcosStores = await marco.GetFromJsonAsync<List<StoreSummaryDto>>("api/restaurants/my-stores");

        var response = await sara.PostAsJsonAsync($"api/auth/switch-store/{marcosStores![0].Id}", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CustomersCannotUseTheSwitchAtAll()
    {
        var customer = _factory.CreateClient();
        await customer.SignInAsync("ahmed@majidfood.com");

        var response = await customer.PostAsJsonAsync("api/auth/switch-store/1", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- The partner opening a business for themselves ----------

    [Fact]
    public async Task APartnerCanOpenAnotherBusinessThemselves()
    {
        var sara = _factory.CreateClient();
        await sara.SignInAsync("sara@majidfood.com");
        var before = (await sara.GetFromJsonAsync<List<StoreSummaryDto>>("api/restaurants/my-stores"))!.Count;

        var response = await sara.PostAsJsonAsync("api/restaurants/my-stores",
            new CreateMyStoreRequest("Sara's Sweets", 1, StoreType.Shop, "Qurum"));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<StoreSummaryDto>();

        // Unapproved and closed: self-service creation, admin-controlled visibility.
        Assert.False(created!.IsApproved);
        Assert.False(created.IsOpen);
        Assert.Equal(StoreType.Shop, created.StoreType);

        var after = await sara.GetFromJsonAsync<List<StoreSummaryDto>>("api/restaurants/my-stores");
        Assert.Equal(before + 1, after!.Count);

        // And the session can switch straight into it.
        var switched = await sara.PostAsJsonAsync($"api/auth/switch-store/{created.Id}", new { });
        switched.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CustomersCannotOpenBusinesses()
    {
        var customer = _factory.CreateClient();
        await customer.SignInAsync("ahmed@majidfood.com");

        var response = await customer.PostAsJsonAsync("api/restaurants/my-stores",
            new CreateMyStoreRequest("Nope", 1));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ANamelessBusinessIsRefused()
    {
        var marco = _factory.CreateClient();
        await marco.SignInAsync("marco@majidfood.com");

        var response = await marco.PostAsJsonAsync("api/restaurants/my-stores",
            new CreateMyStoreRequest("  ", 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ADuplicateEmailWithANonPartnerRoleIsStillRejected()
    {
        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");

        // Same email, but asking for a CUSTOMER — that is a mistake, not a second store.
        var response = await admin.PostAsJsonAsync("api/admin/users", new CreateUserRequest(
            "Duplicate", "marco@majidfood.com", "", "irrelevant1", UserRole.Customer, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
