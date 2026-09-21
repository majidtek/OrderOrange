using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// A partner's new product goes into MongoDB as Pending and stays invisible to
/// customers until an administrator approves it. Editing an already approved product
/// must NOT send it back for review.
/// </summary>
public class ProductApprovalTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ProductApprovalTests(ApiFactory factory) => _factory = factory;

    private static async Task<(HttpClient Client, int RestaurantId, int CategoryId)> PartnerAsync(ApiFactory factory)
    {
        var client = factory.CreateClient();
        var login = await client.SignInAsync("marco@majidfood.com");
        var mine = await client.GetFromJsonAsync<MyRestaurantDto>("api/restaurants/mine");
        var menu = await client.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        return (client, mine!.Id, menu!.First().Id);
    }

    private static SaveMenuItemRequest NewItem(int categoryId, string name) =>
        new(categoryId, name, "a brand new dish", 2.500m, "🍕", false, true);

    [Fact]
    public async Task NewProduct_StartsPendingAndIsHiddenFromCustomers()
    {
        var (partner, restaurantId, categoryId) = await PartnerAsync(_factory);

        var created = await partner.PostAsJsonAsync("api/menu/items", NewItem(categoryId, "Pending Pizza"));
        created.EnsureSuccessStatusCode();
        var dto = await created.Content.ReadFromJsonAsync<MenuItemDto>();

        Assert.NotNull(dto);
        Assert.Equal(ProductStatus.Pending, dto!.Status);
        Assert.True(dto.IsPending);

        // The partner sees it in their own menu…
        var ownerMenu = await partner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        Assert.Contains(ownerMenu!.SelectMany(c => c.Items), i => i.Name == "Pending Pizza");

        // …but a customer browsing the store does not.
        var anonymous = _factory.CreateClient();
        var detail = await anonymous.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{restaurantId}");
        Assert.DoesNotContain(detail!.Categories.SelectMany(c => c.Items), i => i.Name == "Pending Pizza");
    }

    [Fact]
    public async Task PartnerCanSeeHowManyProductsAreWaiting()
    {
        var (partner, _, categoryId) = await PartnerAsync(_factory);
        var before = await partner.GetFromJsonAsync<int>("api/menu/pending-count");

        (await partner.PostAsJsonAsync("api/menu/items", NewItem(categoryId, "Waiting Wrap")))
            .EnsureSuccessStatusCode();

        var after = await partner.GetFromJsonAsync<int>("api/menu/pending-count");
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task AdminApproval_PublishesTheProductToCustomers()
    {
        var (partner, restaurantId, categoryId) = await PartnerAsync(_factory);
        (await partner.PostAsJsonAsync("api/menu/items", NewItem(categoryId, "Approve Me Pasta")))
            .EnsureSuccessStatusCode();

        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");

        var queue = await admin.GetFromJsonAsync<PendingProductPageDto>("api/admin/products/pending");
        var waiting = queue!.Items.Single(p => p.Name == "Approve Me Pasta");
        Assert.Equal(restaurantId, waiting.RestaurantId);
        Assert.False(string.IsNullOrWhiteSpace(waiting.RestaurantName));

        (await admin.PostAsJsonAsync($"api/admin/products/{waiting.Id}/approve", new { }))
            .EnsureSuccessStatusCode();

        var anonymous = _factory.CreateClient();
        var detail = await anonymous.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{restaurantId}");
        Assert.Contains(detail!.Categories.SelectMany(c => c.Items), i => i.Name == "Approve Me Pasta");
    }

    [Fact]
    public async Task Rejection_KeepsItHiddenAndTellsThePartnerWhy()
    {
        var (partner, restaurantId, categoryId) = await PartnerAsync(_factory);
        (await partner.PostAsJsonAsync("api/menu/items", NewItem(categoryId, "Reject Me Roll")))
            .EnsureSuccessStatusCode();

        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");
        var queue = await admin.GetFromJsonAsync<PendingProductPageDto>("api/admin/products/pending");
        var waiting = queue!.Items.Single(p => p.Name == "Reject Me Roll");

        (await admin.PostAsJsonAsync($"api/admin/products/{waiting.Id}/reject",
            new RejectProductRequest("The photo does not match the product."))).EnsureSuccessStatusCode();

        var ownerMenu = await partner.GetFromJsonAsync<List<MenuCategoryDto>>("api/menu");
        var rejected = ownerMenu!.SelectMany(c => c.Items).Single(i => i.Name == "Reject Me Roll");
        Assert.Equal(ProductStatus.Rejected, rejected.Status);
        Assert.Equal("The photo does not match the product.", rejected.RejectionReason);

        var anonymous = _factory.CreateClient();
        var detail = await anonymous.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{restaurantId}");
        Assert.DoesNotContain(detail!.Categories.SelectMany(c => c.Items), i => i.Name == "Reject Me Roll");
    }

    [Fact]
    public async Task EditingAnApprovedProduct_DoesNotSendItBackForReview()
    {
        var (partner, restaurantId, categoryId) = await PartnerAsync(_factory);
        (await partner.PostAsJsonAsync("api/menu/items", NewItem(categoryId, "Edit Me Burger")))
            .EnsureSuccessStatusCode();

        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");
        var queue = await admin.GetFromJsonAsync<PendingProductPageDto>("api/admin/products/pending");
        var waiting = queue!.Items.Single(p => p.Name == "Edit Me Burger");
        (await admin.PostAsJsonAsync($"api/admin/products/{waiting.Id}/approve", new { })).EnsureSuccessStatusCode();

        // Change the price — the partner asked for edits to apply straight away.
        (await partner.PutAsJsonAsync($"api/menu/items/{waiting.Id}",
            new SaveMenuItemRequest(categoryId, "Edit Me Burger", "now cheaper", 1.900m, "🍔", true, true)))
            .EnsureSuccessStatusCode();

        var anonymous = _factory.CreateClient();
        var detail = await anonymous.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{restaurantId}");
        var live = detail!.Categories.SelectMany(c => c.Items).Single(i => i.Name == "Edit Me Burger");
        Assert.Equal(ProductStatus.Approved, live.Status);
        Assert.Equal(1.900m, live.Price);
    }

    [Fact]
    public async Task ApprovingSomethingAlreadyReviewed_IsRefused()
    {
        var (partner, _, categoryId) = await PartnerAsync(_factory);
        (await partner.PostAsJsonAsync("api/menu/items", NewItem(categoryId, "Twice Approved Taco")))
            .EnsureSuccessStatusCode();

        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");
        var queue = await admin.GetFromJsonAsync<PendingProductPageDto>("api/admin/products/pending");
        var waiting = queue!.Items.Single(p => p.Name == "Twice Approved Taco");

        (await admin.PostAsJsonAsync($"api/admin/products/{waiting.Id}/approve", new { })).EnsureSuccessStatusCode();
        var again = await admin.PostAsJsonAsync($"api/admin/products/{waiting.Id}/approve", new { });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task OnlyAdministratorsCanReviewProducts()
    {
        var (partner, _, categoryId) = await PartnerAsync(_factory);
        var created = await partner.PostAsJsonAsync("api/menu/items", NewItem(categoryId, "Self Approve Soup"));
        created.EnsureSuccessStatusCode();
        var dto = (await created.Content.ReadFromJsonAsync<MenuItemDto>())!;

        // A partner must not be able to clear their own submission.
        var res = await partner.PostAsJsonAsync($"api/admin/products/{dto.Id}/approve", new { });
        Assert.True(res.StatusCode is System.Net.HttpStatusCode.Forbidden
                                   or System.Net.HttpStatusCode.Unauthorized,
            $"a partner should not be able to approve, got {res.StatusCode}");
    }
}
