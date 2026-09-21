using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Rooms sketched on the floor plan. What matters: a room lives on its floor, keeps
/// the rectangle it was drawn as (clamped into the canvas), can be renamed and
/// removed, and one store can never touch another's rooms.
/// </summary>
public class StoreRoomTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StoreRoomTests(ApiFactory factory) => _factory = factory;

    private async Task<HttpClient> OwnerAsync(string email = "marco@majidfood.com")
    {
        var client = _factory.CreateClient();
        await client.SignInAsync(email);
        return client;
    }

    [Fact]
    public async Task ARoomIsSketchedRenamedAndRemoved()
    {
        var owner = await OwnerAsync();

        var room = await (await owner.PostAsJsonAsync("api/storerooms",
            new SaveStoreRoomRequest("Salon", "Roof A", 5, 10, 40, 35)))
            .Content.ReadFromJsonAsync<StoreRoomDto>();
        Assert.Equal("Salon", room!.Name);
        Assert.Equal(40, room.W);

        var renamed = await owner.PutAsJsonAsync($"api/storerooms/{room.Id}",
            new SaveStoreRoomRequest("Family salon", room.Floor, room.X, room.Y, room.W, room.H));
        renamed.EnsureSuccessStatusCode();

        var list = await owner.GetFromJsonAsync<List<StoreRoomDto>>("api/storerooms");
        Assert.Contains(list!, r => r.Id == room.Id && r.Name == "Family salon");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"api/storerooms/{room.Id}")).StatusCode);
    }

    [Fact]
    public async Task ARoomDraggedOffTheCanvasIsPulledBackIn()
    {
        var owner = await OwnerAsync();
        var room = await (await owner.PostAsJsonAsync("api/storerooms",
            new SaveStoreRoomRequest("Clamp room", "Roof B", 0, 0, 30, 30)))
            .Content.ReadFromJsonAsync<StoreRoomDto>();

        var moved = await (await owner.PostAsJsonAsync($"api/storerooms/{room!.Id}/position",
            new RoomRectRequest(95, -20, 30, 30))).Content.ReadFromJsonAsync<StoreRoomDto>();

        // X clamps so the whole rectangle stays inside; Y snaps back to the edge.
        Assert.Equal(70, moved!.X);
        Assert.Equal(0, moved.Y);
    }

    [Fact]
    public async Task OneStoreCannotTouchAnothersRooms()
    {
        var marco = await OwnerAsync("marco@majidfood.com");
        var room = await (await marco.PostAsJsonAsync("api/storerooms",
            new SaveStoreRoomRequest("Marco Salon", "", 10, 10, 25, 25)))
            .Content.ReadFromJsonAsync<StoreRoomDto>();

        var sara = await OwnerAsync("sara@majidfood.com");
        var theirList = await sara.GetFromJsonAsync<List<StoreRoomDto>>("api/storerooms");
        Assert.DoesNotContain(theirList!, r => r.Id == room!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await sara.DeleteAsync($"api/storerooms/{room!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await sara.PostAsJsonAsync($"api/storerooms/{room.Id}/position", new RoomRectRequest(1, 1, 20, 20))).StatusCode);
    }

    [Fact]
    public async Task RoomsStayOnTheirOwnFloor()
    {
        var owner = await OwnerAsync();
        var terrace = await (await owner.PostAsJsonAsync("api/storerooms",
            new SaveStoreRoomRequest("Roof lounge", "Roof", 10, 10, 30, 30)))
            .Content.ReadFromJsonAsync<StoreRoomDto>();

        Assert.Equal("Roof", terrace!.Floor);
    }
}
