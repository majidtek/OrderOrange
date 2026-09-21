using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Contracts — the store's standing supply agreements. What matters: the owner can
/// write, rewrite, list and tear one up; another store never sees it; a customer
/// account can't touch the endpoint at all; and emailing without an address or a
/// mailbox says so instead of pretending.
/// </summary>
public class ContractTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ContractTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> OwnerAsync(string email = "marco@majidfood.com")
    {
        var client = _factory.CreateClient();
        await client.SignInAsync(email);
        return client;
    }

    private static SaveContractRequest Sample(string name = "Al Amal Trading", string email = "buyer@example.com") =>
        new(name, email, "+968 9111 2222",
            [new ContractLineDto("Kabsa tray", 10, 4.500m), new ContractLineDto("Salad box", 5, 1.200m)],
            TimesPerMonth: 10, Months: 6, MonthlyPrice: 45m,
            StartDate: DateTime.Today.AddDays(7), Note: "Deliver before noon.");

    [Fact]
    public async Task TheOwnerWritesListsRewritesAndTearsUp()
    {
        var owner = await OwnerAsync();

        var created = await (await owner.PostAsJsonAsync("api/contracts", Sample()))
            .Content.ReadFromJsonAsync<ContractDto>();
        Assert.Equal("Al Amal Trading", created!.CustomerName);
        Assert.Equal(2, created.Lines.Count);
        Assert.Equal(10, created.TimesPerMonth);
        Assert.Equal(45m, created.MonthlyPrice);
        Assert.Null(created.SentAt);

        var list = await owner.GetFromJsonAsync<List<ContractDto>>("api/contracts");
        Assert.Contains(list!, c => c.Id == created.Id);

        // Rewriting keeps the id and takes the new terms.
        var rewritten = await (await owner.PutAsJsonAsync($"api/contracts/{created.Id}",
            Sample() with { MonthlyPrice = 50m, TimesPerMonth = 12 }))
            .Content.ReadFromJsonAsync<ContractDto>();
        Assert.Equal(50m, rewritten!.MonthlyPrice);
        Assert.Equal(12, rewritten.TimesPerMonth);

        (await owner.DeleteAsync($"api/contracts/{created.Id}")).EnsureSuccessStatusCode();
        list = await owner.GetFromJsonAsync<List<ContractDto>>("api/contracts");
        Assert.DoesNotContain(list!, c => c.Id == created.Id);
    }

    [Fact]
    public async Task NonsenseTermsAreRefused()
    {
        var owner = await OwnerAsync();

        async Task Refused(SaveContractRequest req) =>
            Assert.Equal(HttpStatusCode.BadRequest,
                (await owner.PostAsJsonAsync("api/contracts", req)).StatusCode);

        await Refused(Sample() with { CustomerName = " " });
        await Refused(Sample() with { Lines = [] });
        await Refused(Sample() with { Lines = [new ContractLineDto("", 1, 1m)] });
        await Refused(Sample() with { Lines = [new ContractLineDto("Tray", 0, 1m)] });
        await Refused(Sample() with { Lines = [new ContractLineDto("Tray", 1, -1m)] });
        await Refused(Sample() with { TimesPerMonth = 0 });
        await Refused(Sample() with { Months = 61 });
        await Refused(Sample() with { MonthlyPrice = -5m });
    }

    [Fact]
    public async Task AnotherStoreSeesNothingAndTouchesNothing()
    {
        var owner = await OwnerAsync();
        var created = await (await owner.PostAsJsonAsync("api/contracts", Sample("Isolation Co")))
            .Content.ReadFromJsonAsync<ContractDto>();

        // A different partner: their list is their own, and the row is untouchable.
        var other = await OwnerAsync("sara@majidfood.com");
        var theirs = await other.GetFromJsonAsync<List<ContractDto>>("api/contracts");
        Assert.DoesNotContain(theirs!, c => c.Id == created!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"api/contracts/{created!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PostAsJsonAsync($"api/contracts/{created.Id}/email", new { })).StatusCode);

        // A plain customer is not in the room at all.
        var customer = _factory.CreateClient();
        await customer.SignInAsync("customer@majidfood.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("api/contracts")).StatusCode);
    }

    [Fact]
    public async Task EmailingNeedsAnAddressAndAMailbox()
    {
        var owner = await OwnerAsync();

        // No customer email on the contract → told so.
        var bare = await (await owner.PostAsJsonAsync("api/contracts", Sample(email: "")))
            .Content.ReadFromJsonAsync<ContractDto>();
        var noAddress = await owner.PostAsJsonAsync($"api/contracts/{bare!.Id}/email", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noAddress.StatusCode);

        // An address but no mailbox anywhere (tests run without Smtp config) → told so,
        // and the contract is NOT stamped as sent.
        var addressed = await (await owner.PostAsJsonAsync("api/contracts", Sample()))
            .Content.ReadFromJsonAsync<ContractDto>();
        var noMailbox = await owner.PostAsJsonAsync($"api/contracts/{addressed!.Id}/email", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noMailbox.StatusCode);
        var list = await owner.GetFromJsonAsync<List<ContractDto>>("api/contracts");
        Assert.Null(list!.First(c => c.Id == addressed.Id).SentAt);
    }

    [Fact]
    public async Task TheCustomerAcceptsFromThePublicLinkAndTheTermsFreeze()
    {
        var owner = await OwnerAsync();
        var created = await (await owner.PostAsJsonAsync("api/contracts", Sample("Accepting Co")))
            .Content.ReadFromJsonAsync<ContractDto>();
        var code = created!.PublicUrl.Split("/contract/")[^1];

        // Anyone holding the signed link can read and accept — no account anywhere.
        var guest = _factory.CreateClient();
        var page = await guest.GetFromJsonAsync<PublicContractDto>($"api/contract/{code}");
        Assert.Equal("Accepting Co", page!.CustomerName);
        Assert.Null(page.AcceptedAt);

        var accepted = await (await guest.PostAsJsonAsync($"api/contract/{code}/accept",
            new AcceptContractRequest("Hamed of Accepting Co")))
            .Content.ReadFromJsonAsync<PublicContractDto>();
        Assert.NotNull(accepted!.AcceptedAt);
        Assert.Equal("Hamed of Accepting Co", accepted.AcceptedBy);

        // A second yes changes nothing; the first stamp stands.
        var again = await (await guest.PostAsJsonAsync($"api/contract/{code}/accept",
            new AcceptContractRequest("Somebody Else")))
            .Content.ReadFromJsonAsync<PublicContractDto>();
        Assert.Equal(accepted.AcceptedAt, again!.AcceptedAt);
        Assert.Equal("Hamed of Accepting Co", again.AcceptedBy);

        // Accepted terms are a handshake — the owner can no longer rewrite them.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PutAsJsonAsync($"api/contracts/{created.Id}", Sample())).StatusCode);

        // The owner's list shows the stamp.
        var list = await owner.GetFromJsonAsync<List<ContractDto>>("api/contracts");
        Assert.NotNull(list!.First(c => c.Id == created.Id).AcceptedAt);
    }

    [Fact]
    public async Task TheCustomerWritesBackAndTheStoreReadsIt()
    {
        var owner = await OwnerAsync();
        var created = await (await owner.PostAsJsonAsync("api/contracts", Sample("Replying Co")))
            .Content.ReadFromJsonAsync<ContractDto>();
        var code = created!.PublicUrl.Split("/contract/")[^1];
        var guest = _factory.CreateClient();

        // Empty words are refused; real ones land, in order.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await guest.PostAsJsonAsync($"api/contract/{code}/reply", new ReplyContractRequest("  "))).StatusCode);
        (await guest.PostAsJsonAsync($"api/contract/{code}/reply",
            new ReplyContractRequest("First word."))).EnsureSuccessStatusCode();
        var after = await (await guest.PostAsJsonAsync($"api/contract/{code}/reply",
            new ReplyContractRequest("Second word.")))
            .Content.ReadFromJsonAsync<PublicContractDto>();
        Assert.Equal(["First word.", "Second word."], after!.Replies!.Select(r => r.Text));

        // The owner reads the same thread on their list.
        var mine = (await owner.GetFromJsonAsync<List<ContractDto>>("api/contracts"))!
            .First(c => c.Id == created.Id);
        Assert.Equal(2, mine.Replies!.Count);

        // A forged code neither reads, accepts nor replies.
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"api/contract/{created.Id}-aaaaaaaaaa")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await guest.PostAsJsonAsync($"api/contract/{created.Id}-aaaaaaaaaa/reply",
                new ReplyContractRequest("no"))).StatusCode);
    }

    [Fact]
    public async Task TheOwnersMailboxRoundTripsThroughSettings()
    {
        var owner = await OwnerAsync();
        var mine = await owner.GetFromJsonAsync<MyRestaurantDto>("api/restaurants/mine");

        var update = new UpdateRestaurantRequest(
            mine!.Name, mine.Description, mine.CuisineId, mine.LogoEmoji, mine.BannerColor,
            mine.Area, mine.Street, mine.Phone, mine.DeliveryFee, mine.MinOrder, mine.AvgPrepMinutes,
            mine.StoreType, mine.AllowsPickup, mine.TaxPercent, mine.PosDefaultOpen,
            SmtpHost: "smtp.example.com", SmtpPort: 465, SmtpUser: "shop@example.com",
            SmtpPassword: "abcd efgh", SmtpFrom: "orders@example.com");
        (await owner.PutAsJsonAsync("api/restaurants/mine", update)).EnsureSuccessStatusCode();

        var back = await owner.GetFromJsonAsync<MyRestaurantDto>("api/restaurants/mine");
        Assert.Equal("smtp.example.com", back!.SmtpHost);
        Assert.Equal(465, back.SmtpPort);
        Assert.Equal("shop@example.com", back.SmtpUser);
        Assert.Equal("orders@example.com", back.SmtpFrom);
    }
}
