using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Booking a table from the store's own page: how long the party means to stay, how
/// many tables they take, and — above all — that no two parties are ever sold the same
/// table at the same hour. The store must have opted in; a store that never did takes
/// nothing.
/// </summary>
public class ReservationBookingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ReservationBookingTests(ApiFactory factory) => _factory = factory;

    /// <summary>An owner signed in, with online booking switched ON for their store.</summary>
    private async Task<(HttpClient Owner, int StoreId)> BookableStoreAsync(
        decimal perTable = 0m, decimal perMinute = 0m)
    {
        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");

        var mine = await owner.GetFromJsonAsync<MyRestaurantDto>("api/restaurants/mine");
        var update = new UpdateRestaurantRequest(
            mine!.Name, mine.Description, mine.CuisineId, mine.LogoEmoji, mine.BannerColor,
            mine.Area, mine.Street, mine.Phone, mine.DeliveryFee, mine.MinOrder, mine.AvgPrepMinutes,
            mine.StoreType, mine.AllowsPickup, mine.TaxPercent, mine.PosDefaultOpen,
            OnlineReservations: true,
            ReservePricePerTable: perTable, ReservePricePerMinute: perMinute);
        (await owner.PutAsJsonAsync("api/restaurants/mine", update)).EnsureSuccessStatusCode();

        return (owner, mine.Id);
    }

    private static async Task<StoreTableDto> TableAsync(HttpClient owner, string name)
    {
        var response = await owner.PostAsJsonAsync("api/storetables", new SaveStoreTableRequest(name, "indoor", 4));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StoreTableDto>())!;
    }

    private static Task<HttpResponseMessage> BookAsync(
        HttpClient guest, int storeId, DateTime at, int minutes, string phone, params int[] tables) =>
        guest.PostAsJsonAsync($"api/restaurants/{storeId}/reserve",
            new PublicReserveRequest("Party", phone, 4, at, null, 0, minutes, tables.ToList()));

    /// <summary>A fresh evening far from every other test's bookings.</summary>
    private static DateTime Evening(int daysAhead) =>
        DateTime.Today.AddDays(daysAhead).AddHours(19);

    [Fact]
    public async Task TheGuestSaysHowLongAndTheBookingKeepsIt()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var table = await TableAsync(owner, "Dur T1");
        var guest = _factory.CreateClient();
        var at = Evening(20);

        var booking = await (await BookAsync(guest, storeId, at, 120, "+968 9555 0001", table.Id))
            .Content.ReadFromJsonAsync<ReservationDto>();

        Assert.Equal(120, booking!.DurationMinutes);
        Assert.Equal(at.AddHours(2), booking.Until);
        Assert.Equal("Dur T1", booking.TableName);
    }

    [Fact]
    public async Task AnImpossibleLengthIsBroughtBackIntoRange()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var short_ = await TableAsync(owner, "Clamp T1");
        var long_ = await TableAsync(owner, "Clamp T2");
        var guest = _factory.CreateClient();

        var tooShort = await (await BookAsync(guest, storeId, Evening(21), 5, "+968 9555 0002", short_.Id))
            .Content.ReadFromJsonAsync<ReservationDto>();
        var tooLong = await (await BookAsync(guest, storeId, Evening(22), 5000, "+968 9555 0003", long_.Id))
            .Content.ReadFromJsonAsync<ReservationDto>();

        Assert.Equal(30, tooShort!.DurationMinutes);
        Assert.Equal(360, tooLong!.DurationMinutes);
    }

    [Fact]
    public async Task ABigPartyTakesSeveralTablesAndSeatingOccupiesThemAll()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var first = await TableAsync(owner, "Big T1");
        var second = await TableAsync(owner, "Big T2");
        var guest = _factory.CreateClient();

        var booking = await (await BookAsync(guest, storeId, Evening(23), 120, "+968 9555 0004", first.Id, second.Id))
            .Content.ReadFromJsonAsync<ReservationDto>();

        Assert.Equal(2, booking!.TableCount);
        Assert.Equal("Big T1 + Big T2", booking.TableName);
        Assert.Equal($"{first.Id},{second.Id}", booking.TableIds);

        // Seating the party takes BOTH tables and opens an invoice on each.
        (await owner.PostAsJsonAsync($"api/reservations/{booking.Id}/status",
            new SetReservationStatusRequest("seated"))).EnsureSuccessStatusCode();

        var floor = (await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables"))!;
        Assert.True(floor.First(t => t.Id == first.Id).IsOccupied);
        Assert.True(floor.First(t => t.Id == second.Id).IsOccupied);
        Assert.NotNull(await owner.GetFromJsonAsync<StoreTabDto>($"api/storetabs/table/{first.Id}"));
        Assert.NotNull(await owner.GetFromJsonAsync<StoreTabDto>($"api/storetabs/table/{second.Id}"));
    }

    [Fact]
    public async Task TwoPartiesCannotHoldOneTableAtTheSameHour()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var table = await TableAsync(owner, "Clash T1");
        var guest = _factory.CreateClient();
        var at = Evening(24);

        (await BookAsync(guest, storeId, at, 120, "+968 9555 0010", table.Id)).EnsureSuccessStatusCode();

        // Starting inside the first party's two hours…
        var inside = await BookAsync(guest, storeId, at.AddHours(1), 60, "+968 9555 0011", table.Id);
        Assert.Equal(HttpStatusCode.BadRequest, inside.StatusCode);

        // …and ending inside them, having started earlier.
        var before = await BookAsync(guest, storeId, at.AddHours(-1), 120, "+968 9555 0012", table.Id);
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);

        // The refusal is coded AND names the table, so the app can say it in the
        // guest's own language with the table in it.
        var said = await inside.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("rsv.e.taken", said!["code"].ToString());
        Assert.Contains("Clash T1", said["args"].ToString());
    }

    [Fact]
    public async Task TheHourOneStayEndsIsTheHourTheNextMayBegin()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var table = await TableAsync(owner, "Edge T1");
        var guest = _factory.CreateClient();
        var at = Evening(25);

        (await BookAsync(guest, storeId, at, 60, "+968 9555 0020", table.Id)).EnsureSuccessStatusCode();

        // Ends 20:00, so 20:00 is free — the window is half-open, not sticky.
        (await BookAsync(guest, storeId, at.AddHours(1), 60, "+968 9555 0021", table.Id)).EnsureSuccessStatusCode();

        // And the hour that ends exactly when the first begins is free too.
        (await BookAsync(guest, storeId, at.AddHours(-1), 60, "+968 9555 0022", table.Id)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task OneTakenTableSpoilsTheWholeBooking()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var taken = await TableAsync(owner, "Mixed T1");
        var free = await TableAsync(owner, "Mixed T2");
        var guest = _factory.CreateClient();
        var at = Evening(26);

        (await BookAsync(guest, storeId, at, 90, "+968 9555 0030", taken.Id)).EnsureSuccessStatusCode();

        // Asking for one free table and one taken one books neither.
        var both = await BookAsync(guest, storeId, at, 90, "+968 9555 0031", free.Id, taken.Id);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

        var busy = await guest.GetFromJsonAsync<List<int>>(
            $"api/restaurants/{storeId}/floor/busy?at={at:yyyy-MM-ddTHH:mm:ss}&minutes=90");
        Assert.DoesNotContain(free.Id, busy!);
    }

    [Fact]
    public async Task TheBusyListTellsTheBookingPageWhatIsGone()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var table = await TableAsync(owner, "Busy T1");
        var guest = _factory.CreateClient();
        var at = Evening(27);

        (await BookAsync(guest, storeId, at, 120, "+968 9555 0040", table.Id)).EnsureSuccessStatusCode();

        async Task<List<int>> BusyAt(DateTime when, int minutes) =>
            (await guest.GetFromJsonAsync<List<int>>(
                $"api/restaurants/{storeId}/floor/busy?at={when:yyyy-MM-ddTHH:mm:ss}&minutes={minutes}"))!;

        Assert.Contains(table.Id, await BusyAt(at, 60));                 // at the hour itself
        Assert.Contains(table.Id, await BusyAt(at.AddMinutes(90), 60));  // halfway through
        Assert.DoesNotContain(table.Id, await BusyAt(at.AddHours(3), 60)); // after they leave
        Assert.DoesNotContain(table.Id, await BusyAt(at.AddDays(-1), 60)); // the day before
    }

    [Fact]
    public async Task ACancelledBookingGivesTheTableBack()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var table = await TableAsync(owner, "Freed T1");
        var guest = _factory.CreateClient();
        var at = Evening(28);

        var booking = await (await BookAsync(guest, storeId, at, 120, "+968 9555 0050", table.Id))
            .Content.ReadFromJsonAsync<ReservationDto>();

        // While it stands, nobody else may have the table…
        Assert.Equal(HttpStatusCode.BadRequest,
            (await BookAsync(guest, storeId, at, 60, "+968 9555 0051", table.Id)).StatusCode);

        // …once the store declines it, the hour is open again.
        (await owner.PostAsJsonAsync($"api/reservations/{booking!.Id}/status",
            new SetReservationStatusRequest("cancelled"))).EnsureSuccessStatusCode();
        (await BookAsync(guest, storeId, at, 60, "+968 9555 0052", table.Id)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task AStoreWithAFloorRefusesABookingThatNamesNoTable()
    {
        var (owner, storeId) = await BookableStoreAsync();
        await TableAsync(owner, "Needed T1");
        var guest = _factory.CreateClient();

        var response = await BookAsync(guest, storeId, Evening(29), 90, "+968 9555 0060");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownTableIsNotBookable()
    {
        var (owner, storeId) = await BookableStoreAsync();
        await TableAsync(owner, "Ghost T1");
        var guest = _factory.CreateClient();

        var response = await BookAsync(guest, storeId, Evening(30), 90, "+968 9555 0070", 999_999);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AStoreThatNeverOptedInTakesNoBookingsAndShowsNoFloor()
    {
        // A second store, left exactly as it was — booking was never switched on.
        var guest = _factory.CreateClient();
        var stores = await guest.GetFromJsonAsync<List<RestaurantCardDto>>("api/restaurants");
        var closed = stores!.First(s => !s.OnlineReservations);

        Assert.Equal(HttpStatusCode.NotFound,
            (await guest.GetAsync($"api/restaurants/{closed.Id}/floor")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await guest.GetAsync($"api/restaurants/{closed.Id}/floor/busy?at={Evening(31):yyyy-MM-ddTHH:mm:ss}&minutes=90")).StatusCode);

        var response = await BookAsync(guest, closed.Id, Evening(31), 90, "+968 9555 0080", 1);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheQrPathObeysTheSameOneTableOneHourRule()
    {
        var (owner, _) = await BookableStoreAsync();
        var table = await TableAsync(owner, "QR Clash");
        var qrs = await owner.GetFromJsonAsync<List<TableQrDto>>("api/storetables/qrcodes");
        var code = qrs!.First(q => q.TableId == table.Id).Code;

        var guest = _factory.CreateClient();
        var at = Evening(32);

        var first = await guest.PostAsJsonAsync($"api/reserve/{code}",
            new PublicReserveRequest("Scanner", "+968 9555 0090", 2, at, null, 0, 120));
        first.EnsureSuccessStatusCode();

        var clash = await guest.PostAsJsonAsync($"api/reserve/{code}",
            new PublicReserveRequest("Scanner Two", "+968 9555 0091", 2, at.AddMinutes(30), null, 0, 60));
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
    }

    [Fact]
    public async Task APartyTooBigForItsTablesIsTurnedAway()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var first = await TableAsync(owner, "Seats T1");    // four chairs each
        var second = await TableAsync(owner, "Seats T2");
        var guest = _factory.CreateClient();
        var at = Evening(37);

        // Six people at one four-seat table: no.
        var tooMany = await guest.PostAsJsonAsync($"api/restaurants/{storeId}/reserve",
            new PublicReserveRequest("Party", "+968 9555 0140", 6, at, null, 0, 90, [first.Id]));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);

        // The refusal is CODED, so the guest reads it in their own language.
        var body = await tooMany.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("rsv.e.seats", body!["code"].ToString());

        // The same six across both tables fit.
        (await guest.PostAsJsonAsync($"api/restaurants/{storeId}/reserve",
            new PublicReserveRequest("Party", "+968 9555 0141", 6, at, null, 0, 90, [first.Id, second.Id])))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task EveryRefusalCarriesACodeTheAppCanTranslate()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var table = await TableAsync(owner, "Coded T1");
        var guest = _factory.CreateClient();
        var at = Evening(38);

        async Task<string?> CodeOf(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            return body!.TryGetValue("code", out var code) ? code.ToString() : null;
        }

        Assert.Equal("rsv.e.name", await CodeOf(await guest.PostAsJsonAsync(
            $"api/restaurants/{storeId}/reserve",
            new PublicReserveRequest("", "+968 9555 0150", 2, at, null, 0, 90, [table.Id]))));
        Assert.Equal("rsv.e.phone", await CodeOf(await guest.PostAsJsonAsync(
            $"api/restaurants/{storeId}/reserve",
            new PublicReserveRequest("Party", "", 2, at, null, 0, 90, [table.Id]))));
        Assert.Equal("rsv.e.past", await CodeOf(await BookAsync(
            guest, storeId, DateTime.Now.AddDays(-1), 90, "+968 9555 0151", table.Id)));
        Assert.Equal("rsv.e.tooFar", await CodeOf(await BookAsync(
            guest, storeId, DateTime.Now.AddDays(400), 90, "+968 9555 0152", table.Id)));
        Assert.Equal("rsv.e.needTable", await CodeOf(await BookAsync(
            guest, storeId, at, 90, "+968 9555 0153")));
        Assert.Equal("rsv.e.gone", await CodeOf(await BookAsync(
            guest, storeId, at, 90, "+968 9555 0154", 999_999)));

        (await BookAsync(guest, storeId, at, 90, "+968 9555 0155", table.Id)).EnsureSuccessStatusCode();
        Assert.Equal("rsv.e.taken", await CodeOf(await BookAsync(
            guest, storeId, at, 90, "+968 9555 0156", table.Id)));
    }

    [Fact]
    public async Task AHouseThatChargesQuotesPerTableAndPerMinute()
    {
        // 1.500 for each table held, plus 0.010 a minute.
        var (owner, storeId) = await BookableStoreAsync(perTable: 1.500m, perMinute: 0.010m);
        var first = await TableAsync(owner, "Priced T1");
        var second = await TableAsync(owner, "Priced T2");
        var guest = _factory.CreateClient();

        var booking = await (await BookAsync(guest, storeId, Evening(34), 120, "+968 9555 0110", first.Id, second.Id))
            .Content.ReadFromJsonAsync<ReservationDto>();

        // 2 × 1.500 + 120 × 0.010 = 4.200
        Assert.Equal(4.200m, booking!.Price);

        // The store's page carries the prices, so the booking form can quote before sending.
        var card = (await guest.GetFromJsonAsync<List<RestaurantCardDto>>($"api/restaurants/by-ids?ids={storeId}"))!.Single();
        Assert.Equal(1.500m, card.ReservePricePerTable);
        Assert.Equal(0.010m, card.ReservePricePerMinute);
    }

    [Fact]
    public async Task AHouseThatChargesNothingQuotesNothing()
    {
        var (owner, storeId) = await BookableStoreAsync();   // both prices left at zero
        var table = await TableAsync(owner, "Free T1");
        var guest = _factory.CreateClient();

        var booking = await (await BookAsync(guest, storeId, Evening(35), 90, "+968 9555 0120", table.Id))
            .Content.ReadFromJsonAsync<ReservationDto>();

        Assert.Equal(0m, booking!.Price);
        var card = (await guest.GetFromJsonAsync<List<RestaurantCardDto>>($"api/restaurants/by-ids?ids={storeId}"))!.Single();
        Assert.Equal(0m, card.ReservePricePerTable);
        Assert.Equal(0m, card.ReservePricePerMinute);
    }

    [Fact]
    public async Task ANegativePriceIsRefusedRatherThanPaidOut()
    {
        var (owner, storeId) = await BookableStoreAsync(perTable: -5m, perMinute: -1m);
        var table = await TableAsync(owner, "Negative T1");
        var guest = _factory.CreateClient();

        var booking = await (await BookAsync(guest, storeId, Evening(36), 90, "+968 9555 0130", table.Id))
            .Content.ReadFromJsonAsync<ReservationDto>();

        Assert.Equal(0m, booking!.Price);
    }

    [Fact]
    public async Task TheHouseCannotDoubleBookItsOwnTableEither()
    {
        var (owner, storeId) = await BookableStoreAsync();
        var table = await TableAsync(owner, "House T1");
        var guest = _factory.CreateClient();
        var at = Evening(33);

        (await BookAsync(guest, storeId, at, 120, "+968 9555 0100", table.Id)).EnsureSuccessStatusCode();

        // The owner books over the guest by phone — refused all the same.
        var response = await owner.PostAsJsonAsync("api/reservations",
            new OwnerReserveRequest(table.Id, "Walk-in", 2, at.AddMinutes(30)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // A clear hour later, the house books freely.
        (await owner.PostAsJsonAsync("api/reservations",
            new OwnerReserveRequest(table.Id, "Walk-in", 2, at.AddHours(4)))).EnsureSuccessStatusCode();
    }
}
