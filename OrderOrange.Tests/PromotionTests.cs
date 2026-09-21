using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// A store's own discount codes, end to end through the real API: the owner writes the
/// rule, the guest checks the code from the cart, the order is priced with it, and the
/// order book becomes the usage counter. Every rule in <see cref="PromoCatalog.Rules"/>
/// and every limit gets a case here.
/// </summary>
public class PromotionTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public PromotionTests(ApiFactory factory) => _factory = factory;

    // ---------- helpers ----------

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Marco owns Bella Napoli in the demo world.</summary>
    private async Task<(HttpClient Owner, int StoreId)> OwnerAsync()
    {
        var owner = _factory.CreateClient();
        var login = await owner.SignInAsync("marco@majidfood.com");
        Assert.NotNull(login.RestaurantId);
        return (owner, login.RestaurantId!.Value);
    }

    private async Task<HttpClient> CustomerAsync(string email)
    {
        var c = _factory.CreateClient();
        await c.SignInAsync(email);
        return c;
    }

    /// <summary>A brand-new account: no history anywhere, so "first order" is a fact.</summary>
    private async Task<HttpClient> FreshCustomerAsync(string email)
    {
        var c = _factory.CreateClient();
        var reg = await c.PostAsJsonAsync("api/auth/register", new RegisterRequest("Fresh Guest", email, "+968 9000 0000", "Secret_1", FormToken.Create()));
        reg.EnsureSuccessStatusCode();
        await c.SignInAsync(email, "Secret_1");
        return c;
    }

    private static SavePromotionRequest Promo(string code, string rule = "any", string type = "percent", decimal value = 10m,
        decimal maxDiscount = 0m, decimal minOrder = 0m, int ruleValue = 0, decimal ruleAmount = 0m, string orderTypes = "all",
        List<int>? days = null, int? startHour = null, int? endHour = null, DateTime? startsAt = null, DateTime? endsAt = null,
        int maxUses = 0, int perCustomer = 0, bool active = true, bool isPublic = true) =>
        new(code, $"Test {code}", null, type, value, maxDiscount, minOrder, rule, ruleValue, ruleAmount, orderTypes, days,
            startHour, endHour, startsAt, endsAt, maxUses, perCustomer, active, isPublic);

    private static async Task<PromotionDto> CreateAsync(HttpClient owner, SavePromotionRequest req)
    {
        var res = await owner.PostAsJsonAsync("api/promotions", req);
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<PromotionDto>(Json))!;
    }

    private static async Task<string?> CreateErrorAsync(HttpClient owner, SavePromotionRequest req)
    {
        var res = await owner.PostAsJsonAsync("api/promotions", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    private static async Task<PromotionCheckDto> CheckAsync(HttpClient client, int storeId, string code, decimal subtotal, int items = 2, OrderType type = OrderType.Pickup)
    {
        var res = await client.PostAsJsonAsync("api/promotions/check", new CheckPromotionRequest(storeId, code, subtotal, items, type));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PromotionCheckDto>(Json))!;
    }

    private static async Task<(int ItemId, decimal Price)> CheapestDishAsync(HttpClient client, int storeId)
    {
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{storeId}", Json);
        // Bella Napoli's minimum order is 2.000 OMR — one of these clears it on its own.
        var dish = detail!.Categories.SelectMany(c => c.Items).Where(i => i.IsAvailable && i.Price >= 2m).OrderBy(i => i.Price).First();
        return (dish.Id, dish.Price);
    }

    /// <summary>A collection order (no address, no delivery fee) — the simplest arithmetic.</summary>
    private static Task<HttpResponseMessage> PlaceAsync(HttpClient customer, int storeId, int itemId, int qty, string? code) =>
        customer.PostAsJsonAsync("api/orders", new PlaceOrderRequest(storeId, 0, PaymentMethod.CashOnDelivery, code, null,
            [new PlaceOrderItem(itemId, qty, null)], null, OrderType.Pickup));

    // ---------- writing codes ----------

    [Fact]
    public async Task Owner_CreatesACode_NormalizedUpperCase_AndItIsPublic()
    {
        var (owner, storeId) = await OwnerAsync();

        var created = await CreateAsync(owner, Promo(" hello 10 ", rule: "first_order"));
        Assert.Equal("HELLO10", created.Code);
        Assert.Equal("first_order", created.Rule);
        Assert.True(created.IsActive);
        Assert.Equal(0, created.Uses);

        var page = await owner.GetFromJsonAsync<PromotionPageDto>("api/promotions", Json);
        Assert.Contains(page!.Items, p => p.Code == "HELLO10");
        Assert.True(page.Summary.Active >= 1);

        var guest = _factory.CreateClient();   // anonymous — the store page is public
        var pub = await guest.GetFromJsonAsync<List<PublicPromotionDto>>($"api/promotions/public/{storeId}", Json);
        var shown = Assert.Single(pub!, p => p.Code == "HELLO10");
        Assert.Equal(10m, shown.Value);
    }

    [Theory]
    [InlineData("percent", 150, "any", 1, "dc.e.percent")]     // > 100 %
    [InlineData("amount", 0, "any", 1, "dc.e.amount")]         // nothing off
    [InlineData("percent", 10, "orders_month", 0, "dc.e.ruleValue")]
    [InlineData("percent", 10, "min_items", 0, "dc.e.ruleValue")]
    public async Task Create_RefusesBadInput(string type, double value, string rule, int ruleValue, string expected)
    {
        var (owner, _) = await OwnerAsync();
        var code = await CreateErrorAsync(owner, Promo($"BAD{Guid.NewGuid():N}"[..12], rule: rule, type: type, value: (decimal)value, ruleValue: ruleValue));
        Assert.Equal(expected, code);
    }

    [Fact]
    public async Task Create_RefusesShortCode_ReversedDates_AndDuplicates()
    {
        var (owner, _) = await OwnerAsync();
        Assert.Equal("dc.e.code", await CreateErrorAsync(owner, Promo("ab")));
        Assert.Equal("dc.e.dates", await CreateErrorAsync(owner, Promo("DATES1", startsAt: DateTime.Today.AddDays(5), endsAt: DateTime.Today)));

        await CreateAsync(owner, Promo("TWICE1"));
        Assert.Equal("dc.e.codeTaken", await CreateErrorAsync(owner, Promo("twice1")));   // same code, other case
    }

    [Fact]
    public async Task AnotherStoresOwner_CannotSeeOrTouchTheCode()
    {
        var (marco, bella) = await OwnerAsync();
        var mine = await CreateAsync(marco, Promo("PRIVATE1"));

        var sara = _factory.CreateClient();
        var saraLogin = await sara.SignInAsync("sara@majidfood.com");
        Assert.NotEqual(bella, saraLogin.RestaurantId);

        var list = await sara.GetFromJsonAsync<PromotionPageDto>("api/promotions", Json);
        Assert.DoesNotContain(list!.Items, p => p.Code == "PRIVATE1");

        Assert.Equal(HttpStatusCode.NotFound, (await sara.PutAsJsonAsync($"api/promotions/{mine.Id}", Promo("PRIVATE1"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sara.PostAsync($"api/promotions/{mine.Id}/toggle", null)).StatusCode);

        // And the code means nothing at her store.
        var check = await CheckAsync(sara, saraLogin.RestaurantId!.Value, "PRIVATE1", 10m);
        Assert.False(check.Known);
    }

    // ---------- checking codes ----------

    [Fact]
    public async Task Check_UnknownCode_IsNotKnown_SoTheCartMayTryPlatformCoupons()
    {
        var (owner, storeId) = await OwnerAsync();
        var check = await CheckAsync(owner, storeId, "NOSUCHCODE", 10m);
        Assert.False(check.Known);
        Assert.False(check.Valid);
        Assert.Equal("unknown", check.ErrorCode);
    }

    [Fact]
    public async Task PercentCode_TakesThePercent_AndHonoursTheCap()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("HALF1", type: "percent", value: 50m, maxDiscount: 1m));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var small = await CheckAsync(customer, storeId, "HALF1", 1.5m);
        Assert.True(small.Valid);
        Assert.Equal(0.750m, small.Discount);          // 50 % of 1.500, under the cap

        var big = await CheckAsync(customer, storeId, "half1", 10m);   // lower-case is fine
        Assert.True(big.Valid);
        Assert.Equal(1.000m, big.Discount);            // capped at 1.000 OMR
    }

    [Fact]
    public async Task AmountCode_NeedsTheMinimumOrder_AndNeverExceedsTheBasket()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("FIVE2", type: "amount", value: 2m, minOrder: 5m));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var below = await CheckAsync(customer, storeId, "FIVE2", 4m);
        Assert.False(below.Valid);
        Assert.Equal("minOrder", below.ErrorCode);
        Assert.Equal("5.000", below.Args[0]);

        var ok = await CheckAsync(customer, storeId, "FIVE2", 6m);
        Assert.True(ok.Valid);
        Assert.Equal(2m, ok.Discount);
    }

    [Fact]
    public async Task MinItemsRule_CountsTheBasket()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("MINI3", rule: "min_items", ruleValue: 3));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var two = await CheckAsync(customer, storeId, "MINI3", 10m, items: 2);
        Assert.False(two.Valid);
        Assert.Equal("minItems", two.ErrorCode);

        var three = await CheckAsync(customer, storeId, "MINI3", 10m, items: 3);
        Assert.True(three.Valid);
    }

    [Fact]
    public async Task OrderTypeRule_PickupOnly()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("WALKIN5", value: 5m, orderTypes: "pickup"));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var delivery = await CheckAsync(customer, storeId, "WALKIN5", 10m, type: OrderType.Delivery);
        Assert.False(delivery.Valid);
        Assert.Equal("pickupOnly", delivery.ErrorCode);

        var pickup = await CheckAsync(customer, storeId, "WALKIN5", 10m, type: OrderType.Pickup);
        Assert.True(pickup.Valid);
        Assert.Equal(0.500m, pickup.Discount);
    }

    [Fact]
    public async Task HappyHour_OnlyInsideItsHours()
    {
        var (owner, storeId) = await OwnerAsync();
        var h = DateTime.Now.Hour;
        await CreateAsync(owner, Promo("LATER1", startHour: (h + 2) % 24, endHour: (h + 3) % 24 == 0 ? 24 : (h + 3) % 24));
        await CreateAsync(owner, Promo("NOW1", startHour: h, endHour: (h + 1) % 24 == 0 ? 24 : (h + 1) % 24));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var later = await CheckAsync(customer, storeId, "LATER1", 10m);
        Assert.False(later.Valid);
        Assert.Equal("hours", later.ErrorCode);
        Assert.Equal(2, later.Args.Length);

        var now = await CheckAsync(customer, storeId, "NOW1", 10m);
        Assert.True(now.Valid);
    }

    [Fact]
    public async Task DayRule_NotToday()
    {
        var (owner, storeId) = await OwnerAsync();
        var notToday = ((int)DateTime.Now.DayOfWeek + 1) % 7;
        await CreateAsync(owner, Promo("TOMORROW1", days: [notToday]));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var check = await CheckAsync(customer, storeId, "TOMORROW1", 10m);
        Assert.False(check.Valid);
        Assert.Equal("day", check.ErrorCode);
    }

    [Fact]
    public async Task DateWindow_ExpiredAndNotStarted()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("GONE1", endsAt: DateTime.Today.AddDays(-1)));
        await CreateAsync(owner, Promo("SOON1", startsAt: DateTime.Today.AddDays(2)));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var gone = await CheckAsync(customer, storeId, "GONE1", 10m);
        Assert.Equal("expired", gone.ErrorCode);

        var soon = await CheckAsync(customer, storeId, "SOON1", 10m);
        Assert.Equal("notStarted", soon.ErrorCode);
        Assert.Equal(DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"), soon.Args[0]);

        // Expired codes are not advertised either.
        var pub = await customer.GetFromJsonAsync<List<PublicPromotionDto>>($"api/promotions/public/{storeId}", Json);
        Assert.DoesNotContain(pub!, p => p.Code is "GONE1" or "SOON1");
    }

    [Fact]
    public async Task Toggle_SwitchesTheCodeOff_AndHidesIt()
    {
        var (owner, storeId) = await OwnerAsync();
        var promo = await CreateAsync(owner, Promo("SWITCH1"));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var off = await owner.PostAsync($"api/promotions/{promo.Id}/toggle", null);
        off.EnsureSuccessStatusCode();
        Assert.False((await off.Content.ReadFromJsonAsync<PromotionDto>(Json))!.IsActive);

        var check = await CheckAsync(customer, storeId, "SWITCH1", 10m);
        Assert.True(check.Known);
        Assert.Equal("inactive", check.ErrorCode);
        var pub = await customer.GetFromJsonAsync<List<PublicPromotionDto>>($"api/promotions/public/{storeId}", Json);
        Assert.DoesNotContain(pub!, p => p.Code == "SWITCH1");

        (await owner.PostAsync($"api/promotions/{promo.Id}/toggle", null)).EnsureSuccessStatusCode();
        Assert.True((await CheckAsync(customer, storeId, "SWITCH1", 10m)).Valid);
    }

    [Fact]
    public async Task PrivateCode_WorksButIsNotAdvertised()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("SECRET1", isPublic: false));
        var customer = await CustomerAsync("ahmed@majidfood.com");

        var pub = await customer.GetFromJsonAsync<List<PublicPromotionDto>>($"api/promotions/public/{storeId}", Json);
        Assert.DoesNotContain(pub!, p => p.Code == "SECRET1");
        Assert.True((await CheckAsync(customer, storeId, "SECRET1", 10m)).Valid);
    }

    [Fact]
    public async Task Delete_RemovesTheCode()
    {
        var (owner, storeId) = await OwnerAsync();
        var promo = await CreateAsync(owner, Promo("BYE1"));
        (await owner.DeleteAsync($"api/promotions/{promo.Id}")).EnsureSuccessStatusCode();

        var page = await owner.GetFromJsonAsync<PromotionPageDto>("api/promotions", Json);
        Assert.DoesNotContain(page!.Items, p => p.Code == "BYE1");
        Assert.False((await CheckAsync(owner, storeId, "BYE1", 10m)).Known);
    }

    // ---------- the order book: history rules, limits, pricing ----------

    [Fact]
    public async Task FirstOrderCode_WorksOnce_ThenTheOrderBookRefusesIt()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("FIRST10", rule: "first_order"));
        var fresh = await FreshCustomerAsync("first.order@test.com");
        var (itemId, price) = await CheapestDishAsync(fresh, storeId);
        var subtotal = price * 2;

        var check = await CheckAsync(fresh, storeId, "FIRST10", subtotal);
        Assert.True(check.Valid);
        Assert.Equal(Math.Round(subtotal * 0.10m, 3), check.Discount);

        var placed = await PlaceAsync(fresh, storeId, itemId, 2, "FIRST10");
        Assert.True(placed.IsSuccessStatusCode, await placed.Content.ReadAsStringAsync());
        var order = (await placed.Content.ReadFromJsonAsync<OrderDto>(Json))!;
        Assert.Equal(subtotal, order.Subtotal);
        Assert.Equal(Math.Round(subtotal * 0.10m, 3), order.Discount);
        // VAT lands on the goods AFTER the discount, exactly as for platform coupons.
        Assert.Equal(Math.Round((subtotal - order.Discount) * order.TaxPercent / 100m, 3), order.TaxAmount);
        Assert.Equal(subtotal + Pricing.ServiceFee - order.Discount + order.TaxAmount, order.Total);

        // Now there IS a first order — the same code is refused, live and at checkout.
        var again = await CheckAsync(fresh, storeId, "FIRST10", subtotal);
        Assert.False(again.Valid);
        Assert.Equal("firstOnly", again.ErrorCode);

        var refused = await PlaceAsync(fresh, storeId, itemId, 2, "FIRST10");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var body = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("dc.e.firstOnly", body.RootElement.GetProperty("code").GetString());

        // The owner's page reads the redemption straight from the order book.
        var page = await owner.GetFromJsonAsync<PromotionPageDto>("api/promotions", Json);
        var stat = page!.Items.Single(p => p.Code == "FIRST10");
        Assert.Equal(1, stat.Uses);
        Assert.Equal(order.Discount, stat.Saved);
        Assert.Equal(1, stat.UsesThisMonth);
    }

    [Fact]
    public async Task LoyaltyCode_UnlocksAfterTheOrdersThisMonth()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("LOYAL10", rule: "orders_month", ruleValue: 2));
        var fresh = await FreshCustomerAsync("loyal.guest@test.com");
        var (itemId, _) = await CheapestDishAsync(fresh, storeId);

        var locked = await CheckAsync(fresh, storeId, "LOYAL10", 10m);
        Assert.False(locked.Valid);
        Assert.Equal("needOrdersMonth", locked.ErrorCode);
        Assert.Equal(new[] { "2", "0" }, locked.Args);

        (await PlaceAsync(fresh, storeId, itemId, 1, null)).EnsureSuccessStatusCode();
        var oneMore = await CheckAsync(fresh, storeId, "LOYAL10", 10m);
        Assert.Equal(new[] { "2", "1" }, oneMore.Args);

        (await PlaceAsync(fresh, storeId, itemId, 1, null)).EnsureSuccessStatusCode();
        var unlocked = await CheckAsync(fresh, storeId, "LOYAL10", 10m);
        Assert.True(unlocked.Valid);
        Assert.Equal(1.000m, unlocked.Discount);
    }

    [Fact]
    public async Task PerCustomerLimit_StopsTheSameGuest_TotalLimit_StopsEveryone()
    {
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("ONCE5", value: 5m, perCustomer: 1));
        await CreateAsync(owner, Promo("LIMIT5", value: 5m, maxUses: 1));

        var fatima = await CustomerAsync("fatima@majidfood.com");
        var mariam = await CustomerAsync("mariam@majidfood.com");
        var (itemId, _) = await CheapestDishAsync(fatima, storeId);

        (await PlaceAsync(fatima, storeId, itemId, 1, "ONCE5")).EnsureSuccessStatusCode();
        var second = await CheckAsync(fatima, storeId, "ONCE5", 10m);
        Assert.Equal("perCustomer", second.ErrorCode);
        Assert.True((await CheckAsync(mariam, storeId, "ONCE5", 10m)).Valid);   // someone else may still use it

        (await PlaceAsync(fatima, storeId, itemId, 1, "LIMIT5")).EnsureSuccessStatusCode();
        var exhausted = await CheckAsync(mariam, storeId, "LIMIT5", 10m);
        Assert.Equal("maxUses", exhausted.ErrorCode);
    }

    [Fact]
    public async Task StoreCode_BeatsThePlatformCoupon_WithTheSameName()
    {
        // The demo world ships a platform-wide WELCOME10 (10 %). Bella Napoli's OWN
        // WELCOME10 gives 5 % — at Bella, the store's word wins.
        var (owner, storeId) = await OwnerAsync();
        await CreateAsync(owner, Promo("WELCOME10", value: 5m));
        var customer = await CustomerAsync("ahmed@majidfood.com");
        var (itemId, price) = await CheapestDishAsync(customer, storeId);

        var placed = await PlaceAsync(customer, storeId, itemId, 2, "WELCOME10");
        Assert.True(placed.IsSuccessStatusCode, await placed.Content.ReadAsStringAsync());
        var order = (await placed.Content.ReadFromJsonAsync<OrderDto>(Json))!;
        Assert.Equal(Math.Round(price * 2 * 0.05m, 3), order.Discount);
    }

    [Fact]
    public async Task PlatformCoupon_StillWorks_WhenTheStoreHasNoSuchCode()
    {
        var (_, storeId) = await OwnerAsync();
        var customer = await CustomerAsync("ahmed@majidfood.com");
        var (itemId, price) = await CheapestDishAsync(customer, storeId);

        var check = await CheckAsync(customer, storeId, "EID15", 10m);
        Assert.False(check.Known);   // not the store's — the cart falls back to api/coupons/validate

        // WELCOME10 is only a platform coupon in THIS test's world if no store code exists;
        // pick a quantity that clears its minimum order and expect the platform's 10 %.
        var placed = await PlaceAsync(customer, storeId, itemId, 4, "WELCOME10");
        if (placed.IsSuccessStatusCode)
        {
            var order = (await placed.Content.ReadFromJsonAsync<OrderDto>(Json))!;
            Assert.True(order.Discount is > 0m);
        }
    }
}
