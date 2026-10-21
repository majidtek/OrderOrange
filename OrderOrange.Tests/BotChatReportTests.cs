using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>The assistant report says who asked: a partner (portal or POS) or a customer.</summary>
public class BotChatReportTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public BotChatReportTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Report_ShowsWhetherAPartnerOrACustomerAsked_AndFiltersByIt()
    {
        var partnerIp = $"10.9.{Random.Shared.Next(1, 250)}.1";
        var customerIp = $"10.9.{Random.Shared.Next(1, 250)}.2";

        var sara = _factory.CreateClient();
        var login = await sara.SignInAsync("sara@majidfood.com"); // Burger Bros
        (await sara.PostAsJsonAsync("api/track/bot", new BotChatLogRequest(
            "s-partner-" + Guid.NewGuid().ToString("N")[..8], "pos", "2 burger table 3", "Added.",
            "/pos", "en", login.RestaurantId, partnerIp))).EnsureSuccessStatusCode();

        var visitor = _factory.CreateClient();
        (await visitor.PostAsJsonAsync("api/track/bot", new BotChatLogRequest(
            "s-customer-" + Guid.NewGuid().ToString("N")[..8], "customer", "do you deliver to Seeb?", "Yes.",
            "/", "en", null, customerIp))).EnsureSuccessStatusCode();

        var admin = _factory.CreateClient();
        await admin.SignInAsync("admin@majidfood.com");

        var all = await admin.GetFromJsonAsync<BotChatIpsDto>("api/admin/bot-chats?take=200");
        var partnerRow = all!.Items.Single(r => r.Ip == partnerIp);
        Assert.Equal(["pos"], partnerRow.Bots);
        Assert.Equal(login.FullName, partnerRow.UserName);
        Assert.Equal("Burger Bros", partnerRow.StoreName);
        var customerRow = all.Items.Single(r => r.Ip == customerIp);
        Assert.Equal(["customer"], customerRow.Bots);
        Assert.Null(customerRow.UserName);

        var partners = await admin.GetFromJsonAsync<BotChatIpsDto>("api/admin/bot-chats?take=200&who=partner");
        Assert.Contains(partners!.Items, r => r.Ip == partnerIp);
        Assert.DoesNotContain(partners.Items, r => r.Ip == customerIp);

        var customers = await admin.GetFromJsonAsync<BotChatIpsDto>("api/admin/bot-chats?take=200&who=customer");
        Assert.Contains(customers!.Items, r => r.Ip == customerIp);
        Assert.DoesNotContain(customers.Items, r => r.Ip == partnerIp);

        var turns = await admin.GetFromJsonAsync<BotChatTurnsDto>($"api/admin/bot-chats/turns?ip={partnerIp}");
        Assert.Equal("Burger Bros", Assert.Single(turns!.Items).StoreName);
    }
}
