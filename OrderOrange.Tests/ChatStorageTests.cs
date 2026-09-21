using System.Net.Http.Json;
using OrderOrange.ApiServer.Data;
using OrderOrange.Shared;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace OrderOrange.Tests;

/// <summary>
/// Order chat is stored in MongoDB, not SQL. These pin the behaviour the clients
/// depend on: the increasing id that "give me everything after N" polling needs,
/// and the fact that a sent message really lands in a Mongo collection.
/// </summary>
public class ChatStorageTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ChatStorageTests(ApiFactory factory) => _factory = factory;

    private async Task<OrderDto> PlaceOrderAsync(HttpClient client)
    {
        await client.SignInAsync("customer@majidfood.com");
        var restaurants = await client.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants");
        var bella = restaurants!.Single(r => r.Name == "Bella Napoli");
        var detail = await client.GetFromJsonAsync<RestaurantDetailDto>($"api/restaurants/{bella.Id}");
        var dish = detail!.Categories.SelectMany(c => c.Items).First();
        var addresses = await client.GetFromJsonAsync<List<AddressDto>>("api/addresses");

        var placed = await client.PostAsJsonAsync("api/orders", new PlaceOrderRequest(
            bella.Id, addresses![0].Id, PaymentMethod.CashOnDelivery, null, null,
            [new PlaceOrderItem(dish.Id, 1, null)]));
        placed.EnsureSuccessStatusCode();
        return (await placed.Content.ReadFromJsonAsync<OrderDto>())!;
    }

    [Fact]
    public async Task SentMessage_LandsInMongoNotSql()
    {
        var client = _factory.CreateClient();
        var order = await PlaceOrderAsync(client);

        var sent = await client.PostAsJsonAsync($"api/chat/{order.Id}",
            new SendChatRequest("is my pizza on the way?", null, null, null));
        sent.EnsureSuccessStatusCode();

        // Straight to the collection — proves the write went to Mongo.
        var db = _factory.Services.GetRequiredService<IMongoDatabase>();
        var docs = await db.GetCollection<ChatDoc>("chatMessages")
            .Find(m => m.OrderId == order.Id).ToListAsync();

        Assert.Single(docs);
        Assert.Equal("is my pizza on the way?", docs[0].Text);
        Assert.Equal(UserRole.Customer, docs[0].SenderRole);
        Assert.True(docs[0].Id > 0, "the message needs a positive integer id for polling");
    }

    [Fact]
    public async Task Ids_IncreaseSoPollingNeverRepeatsOrSkips()
    {
        var client = _factory.CreateClient();
        var order = await PlaceOrderAsync(client);

        for (var i = 1; i <= 5; i++)
        {
            var r = await client.PostAsJsonAsync($"api/chat/{order.Id}",
                new SendChatRequest($"message {i}", null, null, null));
            r.EnsureSuccessStatusCode();
        }

        var all = await client.GetFromJsonAsync<List<ChatMessageDto>>($"api/chat/{order.Id}");
        Assert.Equal(5, all!.Count);
        Assert.Equal(all.Select(m => m.Id).OrderBy(id => id), all.Select(m => m.Id));
        Assert.Equal(all.Select(m => m.Id).Distinct().Count(), all.Count);

        // Exactly how ChatPanel polls: everything after the last id it already has.
        var afterThird = await client.GetFromJsonAsync<List<ChatMessageDto>>(
            $"api/chat/{order.Id}?afterId={all[2].Id}");
        Assert.Equal(2, afterThird!.Count);
        Assert.Equal(["message 4", "message 5"], afterThird.Select(m => m.Text));
    }

    [Fact]
    public async Task Attachments_RoundTripThroughMongo()
    {
        var client = _factory.CreateClient();
        var order = await PlaceOrderAsync(client);

        const string dataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUg==";
        var sent = await client.PostAsJsonAsync($"api/chat/{order.Id}",
            new SendChatRequest(null, dataUrl, "image", "photo.png"));
        sent.EnsureSuccessStatusCode();

        var back = await client.GetFromJsonAsync<List<ChatMessageDto>>($"api/chat/{order.Id}");
        var msg = Assert.Single(back!);
        Assert.Equal(dataUrl, msg.AttachmentData);
        Assert.Equal("image", msg.AttachmentType);
        Assert.Equal("photo.png", msg.FileName);
    }

    [Fact]
    public async Task Outsider_CannotReadTheConversation()
    {
        var owner = _factory.CreateClient();
        var order = await PlaceOrderAsync(owner);
        (await owner.PostAsJsonAsync($"api/chat/{order.Id}",
            new SendChatRequest("private", null, null, null))).EnsureSuccessStatusCode();

        // A different signed-in customer must not see it, even though chat now lives
        // in a store with no row-level security of its own.
        var stranger = _factory.CreateClient();
        await stranger.SignInAsync("fatima@majidfood.com");
        var res = await stranger.GetAsync($"api/chat/{order.Id}");

        Assert.True(res.StatusCode is System.Net.HttpStatusCode.Forbidden
                                   or System.Net.HttpStatusCode.Unauthorized,
            $"expected the request to be refused, got {res.StatusCode}");
    }

    [Fact]
    public async Task ChatCollection_IsIndexedForTheQueryTheClientsRun()
    {
        // Force the store to exist before inspecting its indexes.
        var client = _factory.CreateClient();
        await PlaceOrderAsync(client);

        var db = _factory.Services.GetRequiredService<IMongoDatabase>();
        var cursor = await db.GetCollection<ChatDoc>("chatMessages").Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();
        var keys = indexes.Select(i => i["key"].AsBsonDocument.ToString()).ToList();

        Assert.Contains(keys, k => k.Contains("OrderId") && k.Contains("_id"));
    }
}
