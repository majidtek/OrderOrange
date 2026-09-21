using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// The receipt QR points at a public page, so the signed code is the only thing standing
/// between a stranger and someone else's bill. These pin both halves: the signature must
/// reject tampering, and the public payload must not carry personal details.
/// </summary>
public class BillVerificationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public BillVerificationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void BillCode_RoundTrips()
    {
        Assert.True(BillCode.TryRead(BillCode.For(4321), out var id));
        Assert.Equal(4321, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("4321")]              // no signature
    [InlineData("4321-")]             // empty signature
    [InlineData("4321-deadbeef00")]   // wrong signature
    [InlineData("-abc")]              // no id
    [InlineData("abc-abc")]           // non-numeric id
    [InlineData("0-abc")]             // ids start at 1
    public void BillCode_RejectsJunk(string code)
    {
        Assert.False(BillCode.TryRead(code, out _));
    }

    [Fact]
    public void BillCode_SignatureIsPerOrder()
    {
        // Guessing a neighbour's bill by nudging the id must not work.
        var mine = BillCode.For(100);
        var theirs = BillCode.For(101);
        Assert.NotEqual(mine[(mine.IndexOf('-') + 1)..], theirs[(theirs.IndexOf('-') + 1)..]);

        var forged = $"101-{mine[(mine.IndexOf('-') + 1)..]}";
        Assert.False(BillCode.TryRead(forged, out _));
    }

    [Fact]
    public void BillCode_LinkPointsAtThePublicPage()
    {
        Assert.Equal($"https://x.test/bill/{BillCode.For(7)}", BillCode.LinkFor(7, "https://x.test/"));
    }

    [Fact]
    public async Task VerifyBill_IsPublicAndHidesPersonalDetails()
    {
        var client = _factory.CreateClient();
        await client.SignInAsync("customer@majidfood.com");

        var restaurants = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants");
        var bella = restaurants!.Single(r => r.Name == "Bella Napoli");
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{bella.Id}");
        var dish = detail!.Categories.SelectMany(c => c.Items).First();
        var addresses = await client.GetFromJsonAsync<List<AddressDto>>("api/addresses");

        var placed = await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            bella.Id, addresses![0].Id, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(dish.Id, 2, null)]));
        placed.EnsureSuccessStatusCode();
        var order = (await placed.Content.ReadFromJsonAsync<OrderDto>())!;

        // A brand-new client with no token — exactly what a scanned QR produces.
        var scanner = _factory.CreateClient();
        var bill = await scanner.GetFromJsonAsync<PublicBillDto>($"api/orders/verify/{BillCode.For(order.Id)}");

        Assert.NotNull(bill);
        Assert.Equal(order.Number, bill!.Number);
        Assert.Equal(order.Total, bill.Total);
        Assert.Equal(bella.Name, bill.RestaurantName);
        Assert.Equal(order.Items.Sum(i => i.Quantity), bill.Items.Sum(i => i.Quantity));

        // Initials only — the full name, phone and address never leave the API.
        Assert.DoesNotContain(order.CustomerName, bill.CustomerInitials);
        var json = await scanner.GetStringAsync($"api/orders/verify/{BillCode.For(order.Id)}");
        Assert.DoesNotContain(order.CustomerPhone, json);
        Assert.DoesNotContain(order.DeliveryAddress, json);
    }

    [Fact]
    public async Task VerifyBill_RejectsAnUnsignedId()
    {
        var scanner = _factory.CreateClient();

        var guessed = await scanner.GetAsync("api/orders/verify/1");
        Assert.Equal(HttpStatusCode.NotFound, guessed.StatusCode);

        var forged = await scanner.GetAsync("api/orders/verify/1-0000000000");
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
    }
}
