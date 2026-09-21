using System.Net;
using System.Net.Http.Json;
using OrderOrange.Shared;

namespace OrderOrange.Tests;

/// <summary>
/// Table-QR reservations. What matters: only a SIGNED code opens the public page, a
/// guest with no account can book, the store sees and works the list, and seating a
/// reservation occupies the table with its invoice already open.
/// </summary>
public class ReservationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ReservationTests(ApiFactory factory) => _factory = factory;

    private async Task<(HttpClient Owner, StoreTableDto Table, string Code)> TableWithCodeAsync(string name)
    {
        var owner = _factory.CreateClient();
        await owner.SignInAsync("marco@majidfood.com");
        var response = await owner.PostAsJsonAsync("api/storetables", new SaveStoreTableRequest(name, "indoor", 4));
        response.EnsureSuccessStatusCode();
        var table = (await response.Content.ReadFromJsonAsync<StoreTableDto>())!;

        var qrs = await owner.GetFromJsonAsync<List<TableQrDto>>("api/storetables/qrcodes");
        var code = qrs!.First(q => q.TableId == table.Id).Code;
        return (owner, table, code);
    }

    [Fact]
    public async Task AGuestWithNoAccountReservesFromTheQr()
    {
        var (owner, table, code) = await TableWithCodeAsync("QR T1");
        var guest = _factory.CreateClient(); // never signs in

        // The scan shows which store and table this is…
        var info = await guest.GetFromJsonAsync<TableReserveInfoDto>($"api/reserve/{code}");
        Assert.Equal("QR T1", info!.TableName);

        // …and the booking goes through with just a name and phone.
        var at = DateTime.Now.AddDays(1);
        var reservation = await (await guest.PostAsJsonAsync($"api/reserve/{code}",
            new PublicReserveRequest("Aisha", "+968 9444 0001", 4, at, "Window please")))
            .Content.ReadFromJsonAsync<ReservationDto>();
        Assert.Equal("pending", reservation!.Status);

        // The store sees it in its list.
        var list = await owner.GetFromJsonAsync<List<ReservationDto>>("api/reservations");
        Assert.Contains(list!, r => r.Id == reservation.Id && r.Name == "Aisha");
    }

    [Fact]
    public async Task AForgedCodeOpensNothing()
    {
        var guest = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync("api/reserve/1-1-abcdefabcdef")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await guest.PostAsJsonAsync("api/reserve/1-1-abcdefabcdef",
                new PublicReserveRequest("X", "1", 2, DateTime.Now.AddDays(1)))).StatusCode);
    }

    [Fact]
    public async Task SeatingAReservationOccupiesTheTableWithItsInvoiceOpen()
    {
        var (owner, table, code) = await TableWithCodeAsync("QR Seat");
        var guest = _factory.CreateClient();
        var reservation = await (await guest.PostAsJsonAsync($"api/reserve/{code}",
            new PublicReserveRequest("Hamed", "+968 9444 0002", 2, DateTime.Now.AddHours(2))))
            .Content.ReadFromJsonAsync<ReservationDto>();

        (await owner.PostAsJsonAsync($"api/reservations/{reservation!.Id}/status",
            new SetReservationStatusRequest("seated"))).EnsureSuccessStatusCode();

        var floor = await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables");
        var seated = floor!.First(t => t.Id == table.Id);
        Assert.True(seated.IsOccupied);
        Assert.Equal("Hamed", seated.GuestName);

        var tab = await owner.GetFromJsonAsync<StoreTabDto>($"api/storetabs/table/{table.Id}");
        Assert.Equal("Hamed", tab!.GuestName);
    }

    [Fact]
    public async Task AGuestOrdersFromTheQrStraightOntoTheTablesInvoice()
    {
        var (owner, table, code) = await TableWithCodeAsync("QR Order T");
        var guest = _factory.CreateClient(); // no account, ever

        // The public info carries the menu; the guest picks from it…
        var info = await guest.GetFromJsonAsync<TableReserveInfoDto>($"api/reserve/{code}");
        var dish = info!.Menu!.SelectMany(c => c.Items).First(i => i.IsAvailable);

        var result = await (await guest.PostAsJsonAsync($"api/reserve/{code}/order",
            new PublicTableOrderRequest([new PlaceOrderItem(dish.Id, 2, null)])))
            .Content.ReadFromJsonAsync<PublicOrderResultDto>();

        Assert.Equal("QR Order T", result!.TableName);
        Assert.Equal(2 * dish.FinalPrice, result.Subtotal);

        // …and the STORE's own POS sees the same lines on the table's open invoice.
        var tab = await owner.GetFromJsonAsync<StoreTabDto>($"api/storetabs/table/{table.Id}");
        Assert.Single(tab!.Lines);
        Assert.Equal(dish.Name, tab.Lines[0].Name);

        // The floor plan shows the table taken by the QR party.
        var floor = await owner.GetFromJsonAsync<List<StoreTableDto>>("api/storetables");
        Assert.True(floor!.First(t => t.Id == table.Id).IsOccupied);
    }

    [Fact]
    public async Task AForgedCodeCannotOrderAnything()
    {
        var guest = _factory.CreateClient();
        var response = await guest.PostAsJsonAsync("api/reserve/1-1-abcdefabcdef/order",
            new PublicTableOrderRequest([new PlaceOrderItem(1, 1, null)]));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheTableChatFlowsBothWaysIncludingVoice()
    {
        var (owner, table, code) = await TableWithCodeAsync("QR Chat T");
        var guest = _factory.CreateClient();

        // The guest writes, then speaks…
        (await guest.PostAsJsonAsync($"api/reserve/{code}/chat",
            new SendTableChatRequest("More bread please"))).EnsureSuccessStatusCode();
        (await guest.PostAsJsonAsync($"api/reserve/{code}/chat",
            new SendTableChatRequest("", "data:audio/webm;base64,AAAA"))).EnsureSuccessStatusCode();

        // …the store sees the thread and answers.
        var threads = await owner.GetFromJsonAsync<List<TableChatThreadDto>>("api/tablechats");
        Assert.Contains(threads!, t => t.TableId == table.Id && t.LastText == "🎤");

        (await owner.PostAsJsonAsync($"api/tablechats/{table.Id}",
            new SendTableChatRequest("On its way!"))).EnsureSuccessStatusCode();

        // The guest's poll picks the reply up.
        var messages = await guest.GetFromJsonAsync<List<TableChatMessageDto>>($"api/reserve/{code}/chat");
        Assert.Equal(3, messages!.Count);
        Assert.Equal("store", messages[^1].From);
        Assert.NotNull(messages[1].Audio);

        // Garbage instead of audio is refused.
        var bad = await guest.PostAsJsonAsync($"api/reserve/{code}/chat",
            new SendTableChatRequest("", "data:image/png;base64,AAAA"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task MessagesCanBeEditedDeletedAndSeen()
    {
        var (owner, table, code) = await TableWithCodeAsync("QR EditDel");
        var guest = _factory.CreateClient();

        var sent = await (await guest.PostAsJsonAsync($"api/reserve/{code}/chat",
            new SendTableChatRequest("Bring slat please"))).Content.ReadFromJsonAsync<TableChatMessageDto>();

        // The guest fixes the typo…
        var edited = await (await guest.PutAsJsonAsync($"api/reserve/{code}/chat/{sent!.Id}",
            new EditTableChatRequest("Bring salt please"))).Content.ReadFromJsonAsync<TableChatMessageDto>();
        Assert.Equal("Bring salt please", edited!.Text);
        Assert.NotNull(edited.EditedAt);

        // …the store's sync sees the change to the OLD message.
        var before = DateTime.Now.AddMinutes(-1).Ticks;
        var sync = await owner.GetFromJsonAsync<TableChatSyncDto>(
            $"api/tablechats/{table.Id}/sync?afterId={sent.Id}&stamp={before}");
        Assert.Contains(sync!.Changed, m => m.Id == sent.Id && m.Text == "Bring salt please");

        // The store reads it — the guest's sync now carries the seen marker.
        (await owner.PostAsJsonAsync($"api/tablechats/{table.Id}/read",
            new MarkChatReadRequest(sent.Id))).EnsureSuccessStatusCode();
        var guestSync = await guest.GetFromJsonAsync<TableChatSyncDto>(
            $"api/reserve/{code}/chat/sync?afterId={sent.Id}&stamp=0");
        Assert.True(guestSync!.OtherReadId >= sent.Id);
        Assert.NotNull(guestSync.OtherReadAt);

        // Deleting leaves a stub — and nobody can delete the OTHER side's words.
        var deleted = await (await guest.DeleteAsync($"api/reserve/{code}/chat/{sent.Id}"))
            .Content.ReadFromJsonAsync<TableChatMessageDto>();
        Assert.True(deleted!.Deleted);
        Assert.Equal("", deleted.Text);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.DeleteAsync($"api/tablechats/{table.Id}/messages/{sent.Id}")).StatusCode);
    }

    [Fact]
    public async Task RepliesQuoteAndFilesTravel()
    {
        var (owner, table, code) = await TableWithCodeAsync("QR ReplyFile");
        var guest = _factory.CreateClient();

        var first = await (await guest.PostAsJsonAsync($"api/reserve/{code}/chat",
            new SendTableChatRequest("Is the soup spicy?"))).Content.ReadFromJsonAsync<TableChatMessageDto>();

        // The store replies TO that message — the quote is frozen into the reply.
        var reply = await (await owner.PostAsJsonAsync($"api/tablechats/{table.Id}",
            new SendTableChatRequest("Only mildly!", ReplyToId: first!.Id)))
            .Content.ReadFromJsonAsync<TableChatMessageDto>();
        Assert.Equal("Is the soup spicy?", reply!.ReplyPreview);

        // A document goes through with its name intact (dangerous chars stripped).
        var file = await (await guest.PostAsJsonAsync($"api/reserve/{code}/chat",
            new SendTableChatRequest("", File: "data:application/pdf;base64,AAAA", FileName: "al<ler>gy.pdf")))
            .Content.ReadFromJsonAsync<TableChatMessageDto>();
        Assert.NotNull(file!.File);
        Assert.Equal("allergy.pdf", file.FileName);

        // Both land on the other side's read with everything attached.
        var seen = await owner.GetFromJsonAsync<List<TableChatMessageDto>>($"api/tablechats/{table.Id}");
        Assert.Contains(seen!, m => m.Id == reply.Id && m.ReplyPreview == "Is the soup spicy?");
        Assert.Contains(seen!, m => m.Id == file.Id && m.FileName == "allergy.pdf");
    }

    [Fact]
    public async Task ClosingTheInvoiceArchivesTheChatIntoHistory()
    {
        var (owner, table, code) = await TableWithCodeAsync("QR Archive");
        var guest = _factory.CreateClient();

        // A conversation happens while the party orders…
        var info = await guest.GetFromJsonAsync<TableReserveInfoDto>($"api/reserve/{code}");
        var dish = info!.Menu!.SelectMany(c => c.Items).First(i => i.IsAvailable);
        (await guest.PostAsJsonAsync($"api/reserve/{code}/order",
            new PublicTableOrderRequest([new PlaceOrderItem(dish.Id, 1, null)]))).EnsureSuccessStatusCode();
        (await guest.PostAsJsonAsync($"api/reserve/{code}/chat",
            new SendTableChatRequest("Extra napkins please"))).EnsureSuccessStatusCode();

        // …then the bill is called and the invoice closes.
        var tab = await owner.GetFromJsonAsync<StoreTabDto>($"api/storetabs/table/{table.Id}");
        var order = await (await owner.PostAsJsonAsync($"api/storetabs/{tab!.Id}/close",
            new CloseTabRequest(PaymentMethod.CashOnDelivery, true))).Content.ReadFromJsonAsync<OrderDto>();

        // The live thread is empty for the next party at this table…
        var live = await owner.GetFromJsonAsync<List<TableChatMessageDto>>($"api/tablechats/{table.Id}");
        Assert.Empty(live!);
        var threads = await owner.GetFromJsonAsync<List<TableChatThreadDto>>("api/tablechats");
        Assert.DoesNotContain(threads!, t => t.TableId == table.Id);

        // …and the whole conversation sits in history under the invoice number.
        var history = await owner.GetFromJsonAsync<List<ChatArchiveSessionDto>>("api/tablechats/history");
        var session = history!.First(s => s.TableId == table.Id);
        Assert.Equal(order!.Number, session.InvoiceNumber);
        var archived = await owner.GetFromJsonAsync<List<TableChatMessageDto>>(
            $"api/tablechats/history/{session.Id}");
        Assert.Contains(archived!, m => m.Text == "Extra napkins please");
    }

    [Fact]
    public async Task OnePhoneCannotFloodOneStore()
    {
        var (_, _, code) = await TableWithCodeAsync("QR Flood");
        var guest = _factory.CreateClient();
        for (var i = 1; i <= 3; i++)
        {
            (await guest.PostAsJsonAsync($"api/reserve/{code}",
                new PublicReserveRequest($"Guest {i}", "+968 9444 0099", 2, DateTime.Now.AddDays(i))))
                .EnsureSuccessStatusCode();
        }

        var fourth = await guest.PostAsJsonAsync($"api/reserve/{code}",
            new PublicReserveRequest("Guest 4", "+968 9444 0099", 2, DateTime.Now.AddDays(4)));
        Assert.Equal(HttpStatusCode.BadRequest, fourth.StatusCode);
    }
}
