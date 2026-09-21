using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// The store's key ring. What matters: the owner mints logins scoped to ONE store,
/// each key carries its role into the session, a plain cashier cannot manage keys,
/// and a withdrawn key stops opening the door entirely.
/// </summary>
public class TeamTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TeamTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TheOwnerHiresACashierWhoGetsARoleScopedSession()
    {
        var owner = _factory.CreateClient();
        var ownerLogin = await owner.SignInAsync("marco@majidfood.com");

        var created = await (await owner.PostAsJsonAsync("api/team",
            new SaveStoreMemberRequest("Nadia Till", "nadia.till@majidfood.com", StoreRole.Cashier, "Cash_123")))
            .Content.ReadFromJsonAsync<StoreMemberDto>();
        Assert.Equal(StoreRole.Cashier, created!.Role);

        // The list shows the owner (crowned) and the new key.
        var team = await owner.GetFromJsonAsync<List<StoreMemberDto>>("api/team");
        Assert.Contains(team!, m => m.IsOwner);
        Assert.Contains(team!, m => m.Email == "nadia.till@majidfood.com");

        // The cashier signs in and lands INSIDE the owner's store, as a cashier.
        var cashier = _factory.CreateClient();
        var session = await cashier.SignInAsync("nadia.till@majidfood.com", "Cash_123");
        Assert.Equal(ownerLogin.RestaurantId, session.RestaurantId);
        Assert.Equal("cashier", session.StoreRole);

        // A cashier holds no key ring of their own…
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/team")).StatusCode);

        // …and promotion to manager widens the ring without handing over the keys.
        (await owner.PutAsJsonAsync($"api/team/{created.Id}",
            new SaveStoreMemberRequest("Nadia Till", created.Email, StoreRole.Manager)))
            .EnsureSuccessStatusCode();
        var promoted = await cashier.SignInAsync("nadia.till@majidfood.com", "Cash_123");
        Assert.Equal("manager", promoted.StoreRole);

        // What a manager gains: the numbers, which a cashier could not see.
        Assert.Equal(HttpStatusCode.OK,
            (await cashier.GetAsync("api/restaurants/mine/report/sales?period=day")).StatusCode);

        // What stays with the owner: handing out keys, and rewriting the shop itself.
        // This test used to expect a promoted manager to open api/team — written before
        // the permission catalogue existed, and directly contrary to the rule it now
        // encodes (Manager = All except Team and Settings). Only the owner holds these.
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync("api/team")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("api/team")).StatusCode);
    }

    [Fact]
    public async Task TwoPartnersKeyRingsNeverTouch()
    {
        // Marco hires; Sara — a different partner — must never see or reach that key.
        var marco = _factory.CreateClient();
        await marco.SignInAsync("marco@majidfood.com");
        var hired = await (await marco.PostAsJsonAsync("api/team",
            new SaveStoreMemberRequest("Marco Only", "marco.only@majidfood.com", StoreRole.Kitchen, "Kitch_123")))
            .Content.ReadFromJsonAsync<StoreMemberDto>();

        var sara = _factory.CreateClient();
        await sara.SignInAsync("sara@majidfood.com");
        var sarasTeam = await sara.GetFromJsonAsync<List<StoreMemberDto>>("api/team");
        Assert.DoesNotContain(sarasTeam!, m => m.Email == "marco.only@majidfood.com");

        // Editing or removing across the fence bounces off.
        Assert.Equal(HttpStatusCode.NotFound, (await sara.PutAsJsonAsync($"api/team/{hired!.Id}",
            new SaveStoreMemberRequest("Stolen", hired.Email, StoreRole.Manager))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sara.DeleteAsync($"api/team/{hired.Id}")).StatusCode);

        // And the kitchen key opens Marco's store, not Sara's.
        var kitchen = _factory.CreateClient();
        var session = await kitchen.SignInAsync("marco.only@majidfood.com", "Kitch_123");
        var marcoStore = (await marco.GetFromJsonAsync<MyRestaurantDto>("api/restaurants/mine"))!.Id;
        Assert.Equal(marcoStore, session.RestaurantId);
    }

    [Fact]
    public async Task OneCashierHoldsKeysToTwoStoresOfTheSameOwner()
    {
        var owner = _factory.CreateClient();
        var first = await owner.SignInAsync("marco@majidfood.com");

        // The owner opens a second business and hires the SAME person there too.
        var second = await (await owner.PostAsJsonAsync("api/restaurants/my-stores",
            new CreateMyStoreRequest("Marco Second Branch", 1)))
            .Content.ReadFromJsonAsync<StoreSummaryDto>();

        (await owner.PostAsJsonAsync("api/team",
            new SaveStoreMemberRequest("Twin Keys", "twin.keys@majidfood.com", StoreRole.Cashier, "Twin_123")))
            .EnsureSuccessStatusCode();

        var switched = await (await owner.PostAsync($"api/auth/switch-store/{second!.Id}", null))
            .Content.ReadFromJsonAsync<LoginResponse>();
        owner.DefaultRequestHeaders.Authorization = new("Bearer", switched!.Token);

        // Same email, second store: attaches a membership instead of refusing.
        var attached = await (await owner.PostAsJsonAsync("api/team",
            new SaveStoreMemberRequest("Twin Keys", "twin.keys@majidfood.com", StoreRole.Waiter)))
            .Content.ReadFromJsonAsync<StoreMemberDto>();
        Assert.Equal(StoreRole.Waiter, attached!.Role);

        // The member sees both stores and walks between them, wearing a different
        // hat in each: cashier in the first, waiter in the second.
        var member = _factory.CreateClient();
        var session = await member.SignInAsync("twin.keys@majidfood.com", "Twin_123");
        var stores = await member.GetFromJsonAsync<List<StoreSummaryDto>>("api/restaurants/my-stores");
        Assert.Contains(stores!, s => s.Id == first.RestaurantId);
        Assert.Contains(stores!, s => s.Id == second.Id);

        var inSecond = await (await member.PostAsync($"api/auth/switch-store/{second.Id}", null))
            .Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("waiter", inSecond!.StoreRole);
        var inFirst = await (await member.PostAsync($"api/auth/switch-store/{first.RestaurantId}", null))
            .Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("cashier", inFirst!.StoreRole);

        // A stranger's store stays a locked door.
        var sara = _factory.CreateClient();
        await sara.SignInAsync("sara@majidfood.com");
        var sarasStore = (await sara.GetFromJsonAsync<MyRestaurantDto>("api/restaurants/mine"))!.Id;
        Assert.Equal(HttpStatusCode.NotFound,
            (await member.PostAsync($"api/auth/switch-store/{sarasStore}", null)).StatusCode);
    }

    [Fact]
    public async Task AWithdrawnKeyStopsOpeningTheDoor()
    {
        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");

        var created = await (await owner.PostAsJsonAsync("api/team",
            new SaveStoreMemberRequest("Temp Waiter", "temp.waiter@majidfood.com", StoreRole.Waiter, "Wait_123")))
            .Content.ReadFromJsonAsync<StoreMemberDto>();

        // The key works…
        var waiter = _factory.CreateClient();
        var session = await waiter.SignInAsync("temp.waiter@majidfood.com", "Wait_123");
        Assert.Equal("waiter", session.StoreRole);

        // …the owner takes it back — the account itself goes dark with it.
        (await owner.DeleteAsync($"api/team/{created!.Id}")).EnsureSuccessStatusCode();
        var again = _factory.CreateClient();
        var refused = await again.PostAsJsonAsync("api/auth/login",
            new LoginRequest("temp.waiter@majidfood.com", "Wait_123"));
        Assert.False(refused.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ARewrittenPresetChangesWhatItsHoldersCanDo()
    {
        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");

        await (await owner.PostAsJsonAsync("api/team",
            new SaveStoreMemberRequest("Preset Cashier", "preset.cashier@majidfood.com", StoreRole.Cashier, "Cash_123")))
            .Content.ReadFromJsonAsync<StoreMemberDto>();

        // Out of the box a cashier may write to the menu…
        var cashier = _factory.CreateClient();
        var before = await cashier.SignInAsync("preset.cashier@majidfood.com", "Cash_123");
        Assert.Null(before.Perms);

        // …then the owner REWRITES what "Cashier" means in this shop: POS only.
        var saved = await (await owner.PutAsJsonAsync("api/team/presets/cashier",
            new SavePresetPermsRequest([Perm.Pos, Perm.Orders])))
            .Content.ReadFromJsonAsync<PresetRolePermsDto>();
        Assert.Equal(StoreRole.Cashier, saved!.Role);
        Assert.Contains(Perm.Pos, saved.Perms);

        // The rewrite is listed, and rides into the cashier's NEXT session…
        var overrides = await owner.GetFromJsonAsync<List<PresetRolePermsDto>>("api/team/presets");
        Assert.Single(overrides!);
        var after = await _factory.CreateClient().SignInAsync("preset.cashier@majidfood.com", "Cash_123");
        Assert.NotNull(after.Perms);
        Assert.Contains(Perm.Pos, after.Perms!.Split(','));

        // …and the API judges by it: a menu write, open to a stock cashier, is refused.
        var narrowed = _factory.CreateClient();
        narrowed.DefaultRequestHeaders.Authorization = new("Bearer", after.Token);
        var refused = await narrowed.PostAsJsonAsync("api/menu/categories",
            new SaveCategoryRequest("Cashier shelf", 99));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // An empty rewrite is nonsense and refused outright.
        var empty = await owner.PutAsJsonAsync("api/team/presets/cashier", new SavePresetPermsRequest([]));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        // Reset: the built-in key returns, and the next session carries nothing extra.
        (await owner.DeleteAsync("api/team/presets/cashier")).EnsureSuccessStatusCode();
        var restored = await _factory.CreateClient().SignInAsync("preset.cashier@majidfood.com", "Cash_123");
        Assert.Null(restored.Perms);

        // The rewrite never leaks into the custom-roles list.
        var custom = await owner.GetFromJsonAsync<List<StoreRoleDefDto>>("api/team/roles");
        Assert.DoesNotContain(custom!, r => r.Name == "");
    }
}
