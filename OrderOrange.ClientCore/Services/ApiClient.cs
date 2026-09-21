using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OrderOrange.Shared;

namespace OrderOrange.ClientCore.Services;

/// <summary>Strongly-typed wrapper over the MajidFood delivery Web API.</summary>
public class ApiClient(HttpClient http, AppState state, LanguageService lang)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Raised when a WRITE is refused with 403 — a member trying a door their role
    /// does not open. The shell listens and raises the refusal as a dialog, so it
    /// is seen even from inside a sheet or a page that forgot its snackbar. Reads
    /// never raise it: a page quietly showing less is not an event.
    /// </summary>
    public event Action<string>? PermissionRefused;
    private void NotifyForbidden(HttpResponseMessage res, string message)
    {
        if (res.StatusCode != HttpStatusCode.Forbidden) return;
        try { PermissionRefused?.Invoke(message); } catch { }
    }

    private void Auth()
    {
        http.DefaultRequestHeaders.Authorization =
            state.Token is null ? null : new AuthenticationHeaderValue("Bearer", state.Token);
    }

    /// <summary>
    /// GET that never throws into a page lifecycle method. Handles: 204/404/empty body →
    /// default; 401/403 (expired token / lost access) → sign out and return default so the
    /// UI routes back to login; network failure / timeout / malformed JSON → default.
    /// </summary>
    private async Task<T?> GetAsync<T>(string url)
    {
        try
        {
            Auth();
            using var res = await http.GetAsync(url);
            if (res.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
                return default;
            if (res.StatusCode is HttpStatusCode.Unauthorized)
            {
                state.SignOut();
                return default;
            }
            // 403 is "this door is not yours", NOT "you are signed out": a cashier
            // whose page brushes an owner-only endpoint must keep their session.
            if (!res.IsSuccessStatusCode)
                return default;
            var text = await res.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, JsonOpts);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return default;
        }
    }

    private async Task<(bool ok, string? error)> SendAsync(HttpMethod method, string url, object? body = null)
    {
        try
        {
            Auth();
            var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body);
            using var res = await http.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                if (url.Contains("restaurants/mine", StringComparison.OrdinalIgnoreCase)) { _mine = null; _stores = null; }
                if (url.Contains("my-stores", StringComparison.OrdinalIgnoreCase)) _stores = null;
                return (true, null);
            }
            if (res.StatusCode is HttpStatusCode.Unauthorized)
            {
                state.SignOut();
                return (false, lang["common.sessionExpired"]);
            }
            // A 403 falls through: the server's own refusal is the message.
            var error = await SafeReadError(res);
            NotifyForbidden(res, error);
            return (false, error);
        }
        catch (TaskCanceledException)
        {
            return (false, lang["common.timedOut"]);
        }
        catch (HttpRequestException)
        {
            return (false, lang["common.noServer"]);
        }
    }

    /// <summary>
    /// POST that returns a body — used where the caller needs the created resource back.
    /// authCall marks login/register themselves: there a 401 means "wrong credentials",
    /// which must surface as the server's own message, never as a session expiry.
    /// </summary>
    /// <summary>Any verb with a body that returns the updated resource (PUT on a tab line…).</summary>
    private async Task<(T? data, string? error)> SendForAsync<T>(HttpMethod method, string url, object body)
    {
        try
        {
            Auth();
            using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
            using var res = await http.SendAsync(request);
            if (res.IsSuccessStatusCode)
            {
                var text = await res.Content.ReadAsStringAsync();
                var data = string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, JsonOpts);
                return data is null ? (default, lang["common.badResponse"]) : (data, null);
            }
            if (res.StatusCode is HttpStatusCode.Unauthorized)
            {
                state.SignOut();
                return (default, lang["common.sessionExpired"]);
            }
            var refusal = await SafeReadError(res);
            NotifyForbidden(res, refusal);
            return (default, refusal);
        }
        catch (TaskCanceledException)
        {
            return (default, lang["common.timedOut"]);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return (default, lang["common.noServer"]);
        }
    }

    /// <summary>DELETE that returns the updated resource (e.g. a tab after a line is struck).</summary>
    private async Task<(T? data, string? error)> DeleteForAsync<T>(string url)
    {
        try
        {
            Auth();
            using var res = await http.DeleteAsync(url);
            if (res.IsSuccessStatusCode)
            {
                var text = await res.Content.ReadAsStringAsync();
                var data = string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, JsonOpts);
                return data is null ? (default, lang["common.badResponse"]) : (data, null);
            }
            if (res.StatusCode is HttpStatusCode.Unauthorized)
            {
                state.SignOut();
                return (default, lang["common.sessionExpired"]);
            }
            var refusal = await SafeReadError(res);
            NotifyForbidden(res, refusal);
            return (default, refusal);
        }
        catch (TaskCanceledException)
        {
            return (default, lang["common.timedOut"]);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return (default, lang["common.noServer"]);
        }
    }

    private async Task<(T? data, string? error)> PostForAsync<T>(string url, object body, bool authCall = false)
    {
        try
        {
            Auth();
            using var res = await http.PostAsJsonAsync(url, body);
            if (res.IsSuccessStatusCode)
            {
                var text = await res.Content.ReadAsStringAsync();
                var data = string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, JsonOpts);
                return data is null ? (default, lang["common.badResponse"]) : (data, null);
            }
            if (!authCall && res.StatusCode is HttpStatusCode.Unauthorized)
            {
                state.SignOut();
                return (default, lang["common.sessionExpired"]);
            }
            var refusal = await SafeReadError(res);
            NotifyForbidden(res, refusal);
            return (default, refusal);
        }
        catch (TaskCanceledException)
        {
            return (default, lang["common.timedOut"]);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return (default, lang["common.noServer"]);
        }
    }

    /// <summary>Extracts a human message from {message}, ProblemDetails, or ValidationProblemDetails.
    /// Coded refusals come out already in the reader's language.</summary>
    private async Task<string> SafeReadError(HttpResponseMessage res)
    {
        try
        {
            var text = await res.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                // A coded refusal travels as "§code§arg§arg…" — LanguageService.ServerError
                // unpacks it into the customer's language; anything else shows it raw.
                if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                {
                    var packed = "§" + code.GetString();
                    if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
                        foreach (var a in args.EnumerateArray())
                            packed += "§" + a.ToString();
                    // Unpack here, once, so EVERY page shows the refusal in the
                    // reader's language — with or without its own ServerError call.
                    return lang.ServerError(packed) ?? packed;
                }
                if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    return msg.GetString()!;
                if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                {
                    var parts = new List<string>();
                    foreach (var field in errors.EnumerateObject())
                        foreach (var m in field.Value.EnumerateArray())
                            parts.Add(m.GetString() ?? "");
                    var joined = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
                    if (!string.IsNullOrWhiteSpace(joined)) return joined;
                }
                if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    return detail.GetString()!;
                if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    return title.GetString()!;
            }
        }
        catch { /* fall through */ }
        return $"Request failed ({(int)res.StatusCode}).";
    }

    // ---------- Auth ----------
    public Task<(LoginResponse? user, string? error)> LoginAsync(string email, string password, bool remember = false) =>
        PostForAsync<LoginResponse>("api/auth/login", new LoginRequest(email, password, remember), authCall: true);
    public Task<(LoginResponse? user, string? error)> RegisterAsync(RegisterRequest req) =>
        PostForAsync<LoginResponse>("api/auth/register", req, authCall: true);
    /// <summary>Exchanges a verified Google credential for our own session token.</summary>
    public Task<(LoginResponse? user, string? error)> GoogleLoginAsync(string idToken, bool staffOnly, bool remember = false) =>
        PostForAsync<LoginResponse>("api/auth/google", new GoogleLoginRequest(idToken, staffOnly, remember), authCall: true);

    /// <summary>Null client id — or no answer at all — means "don't offer the Google button".</summary>
    public Task<GoogleAuthConfig?> GetGoogleConfigAsync() => GetAsync<GoogleAuthConfig>("api/auth/google/config");

    /// <summary>Which ways in this deployment offers. Null answer = fall back to what still works.</summary>
    public Task<AuthMethods?> GetAuthMethodsAsync() => GetAsync<AuthMethods>("api/auth/methods");

    // ---------- Admin: open another app as another user ----------
    public Task<(ImpersonationCodeDto? code, string? error)> StartImpersonationAsync(int userId) =>
        PostForAsync<ImpersonationCodeDto>($"api/admin/impersonate/{userId}", new { });
    public Task<(LoginResponse? user, string? error)> RedeemImpersonationAsync(string code) =>
        PostForAsync<LoginResponse>("api/auth/impersonate", new ImpersonationRedeemRequest(code), authCall: true);

    /// <summary>Asks for a one-time code by email. Never reveals whether the account exists.</summary>
    public Task<(OtpRequestResult? result, string? error)> RequestLoginCodeAsync(string email, bool staffOnly) =>
        PostForAsync<OtpRequestResult>("api/auth/otp/request", new OtpRequest(email, staffOnly), authCall: true);

    public Task<(LoginResponse? user, string? error)> VerifyLoginCodeAsync(string email, string code, bool staffOnly, bool remember = false) =>
        PostForAsync<LoginResponse>("api/auth/otp/verify", new OtpVerifyRequest(email, code, staffOnly, remember), authCall: true);

    public Task<(bool, string?)> RegisterDriverAsync(RegisterDriverRequest req) =>
        SendAsync(HttpMethod.Post, "api/auth/register-driver", req);
    public Task<(bool, string?)> RegisterPartnerAsync(RegisterPartnerRequest req) => SendAsync(HttpMethod.Post, "api/auth/register-partner", req);
    public Task<(bool, string?)> UpdateProfileAsync(UpdateProfileRequest req) => SendAsync(HttpMethod.Put, "api/auth/profile", req);
    public Task<(bool, string?)> UpdateAvatarAsync(string? avatar) => SendAsync(HttpMethod.Put, "api/auth/avatar", new UpdateAvatarRequest(avatar));
    public Task<(bool, string?)> ChangePasswordAsync(ChangePasswordRequest req) => SendAsync(HttpMethod.Post, "api/auth/change-password", req);

    // ---------- The store's own customer book (partner portal) ----------
    public Task<List<StoreCustomerDto>?> GetStoreCustomersAsync(string? search = null) =>
        GetAsync<List<StoreCustomerDto>>($"api/storecustomers?search={Uri.EscapeDataString(search ?? "")}");
    public Task<(StoreCustomerDto? customer, string? error)> CreateStoreCustomerAsync(SaveStoreCustomerRequest req) =>
        PostForAsync<StoreCustomerDto>("api/storecustomers", req);
    public Task<(bool, string?)> UpdateStoreCustomerAsync(int id, SaveStoreCustomerRequest req) =>
        SendAsync(HttpMethod.Put, $"api/storecustomers/{id}", req);
    public Task<(bool, string?)> DeleteStoreCustomerAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/storecustomers/{id}");

    // ---------- The dining room ----------
    public Task<List<StoreTableDto>?> GetTablesAsync() => GetAsync<List<StoreTableDto>>("api/storetables");
    public Task<(StoreTableDto? table, string? error)> CreateTableAsync(SaveStoreTableRequest req) =>
        PostForAsync<StoreTableDto>("api/storetables", req);
    public Task<(bool, string?)> UpdateTableAsync(int id, SaveStoreTableRequest req) =>
        SendAsync(HttpMethod.Put, $"api/storetables/{id}", req);
    public Task<(bool, string?)> DeleteTableAsync(int id) => SendAsync(HttpMethod.Delete, $"api/storetables/{id}");
    public Task<(StoreTableDto? table, string? error)> MoveTableAsync(int id, MoveTableRequest req) =>
        PostForAsync<StoreTableDto>($"api/storetables/{id}/position", req);
    public Task<(bool, string?)> RenameFloorAsync(RenameFloorRequest req) =>
        SendAsync(HttpMethod.Post, "api/storetables/floors/rename", req);

    // ---------- Rooms drawn on the plan ----------
    public Task<List<StoreRoomDto>?> GetRoomsAsync() => GetAsync<List<StoreRoomDto>>("api/storerooms");
    public Task<(StoreRoomDto? room, string? error)> CreateRoomAsync(SaveStoreRoomRequest req) =>
        PostForAsync<StoreRoomDto>("api/storerooms", req);
    public Task<(bool, string?)> RenameRoomAsync(int id, SaveStoreRoomRequest req) =>
        SendAsync(HttpMethod.Put, $"api/storerooms/{id}", req);
    public Task<(StoreRoomDto? room, string? error)> MoveRoomAsync(int id, RoomRectRequest req) =>
        PostForAsync<StoreRoomDto>($"api/storerooms/{id}/position", req);
    public Task<(bool, string?)> DeleteRoomAsync(int id) => SendAsync(HttpMethod.Delete, $"api/storerooms/{id}");
    public Task<(bool, string?)> ReorderRoomsAsync(List<int> ids) => SendAsync(HttpMethod.Post, "api/storerooms/order", ids);

    // ---------- Open invoices (tabs) ----------
    public Task<List<StoreTabDto>?> GetTabsAsync() => GetAsync<List<StoreTabDto>>("api/storetabs");
    public Task<StoreTabDto?> GetTabForTableAsync(int tableId) =>
        GetAsync<StoreTabDto>($"api/storetabs/table/{tableId}");
    public Task<(StoreTabDto? tab, string? error)> OpenTabAsync(OpenTabRequest req) =>
        PostForAsync<StoreTabDto>("api/storetabs/open", req);
    public Task<(StoreTabDto? tab, string? error)> AddTabLinesAsync(int id, AddTabLinesRequest req) =>
        PostForAsync<StoreTabDto>($"api/storetabs/{id}/lines", req);
    public Task<(StoreTabDto? tab, string? error)> RemoveTabLineAsync(int id, int lineId) =>
        DeleteForAsync<StoreTabDto>($"api/storetabs/{id}/lines/{lineId}");
    public Task<(StoreTabDto? tab, string? error)> SetTabLineQtyAsync(int id, int lineId, int quantity) =>
        SendForAsync<StoreTabDto>(HttpMethod.Put, $"api/storetabs/{id}/lines/{lineId}", new SetTabLineQtyRequest(quantity));
    public Task<(StoreTabDto? tab, string? error)> SetTabLineNoteAsync(int id, int lineId, int quantity, string? note) =>
        SendForAsync<StoreTabDto>(HttpMethod.Put, $"api/storetabs/{id}/lines/{lineId}", new SetTabLineQtyRequest(quantity, note ?? ""));
    public Task<(OrderDto? order, string? error)> CloseTabAsync(int id, CloseTabRequest req) =>
        PostForAsync<OrderDto>($"api/storetabs/{id}/close", req);
    /// <summary>Closing an EMPTY tab: nothing to bill, the server just frees the table.</summary>
    public Task<(bool, string?)> CancelTabAsync(int id) =>
        SendAsync(HttpMethod.Post, $"api/storetabs/{id}/close", new CloseTabRequest(PaymentMethod.CashOnDelivery));

    // ---------- Table QR reservations ----------
    public Task<List<TableQrDto>?> GetTableQrsAsync() => GetAsync<List<TableQrDto>>("api/storetables/qrcodes");
    /// <summary>The owner's own wording for the printed table-QR cards.</summary>
    public Task<(bool ok, string? error)> SetQrCardTextAsync(string? text) =>
        SendAsync(HttpMethod.Put, "api/restaurants/mine/qr-text", new QrTextRequest(text));
    public Task<TableReserveInfoDto?> GetReserveInfoAsync(string code) =>
        GetAsync<TableReserveInfoDto>($"api/reserve/{Uri.EscapeDataString(code)}");
    public Task<PublicTabInvoiceDto?> GetPublicTabInvoiceAsync(string code, int tabId) =>
        GetAsync<PublicTabInvoiceDto>($"api/reserve/{Uri.EscapeDataString(code)}/invoice/{tabId}");
    public Task<(ReservationDto? reservation, string? error)> SubmitReservationAsync(string code, PublicReserveRequest req) =>
        PostForAsync<ReservationDto>($"api/reserve/{Uri.EscapeDataString(code)}", req);
    public Task<(PublicOrderResultDto? result, string? error)> SubmitTableOrderAsync(string code, PublicTableOrderRequest req) =>
        PostForAsync<PublicOrderResultDto>($"api/reserve/{Uri.EscapeDataString(code)}/order", req);

    // ---------- Table chat ----------
    public Task<List<TableChatMessageDto>?> GetTableChatAsync(string code, int afterId = 0) =>
        GetAsync<List<TableChatMessageDto>>($"api/reserve/{Uri.EscapeDataString(code)}/chat?afterId={afterId}");
    public Task<(TableChatMessageDto? message, string? error)> SendTableChatAsync(string code, SendTableChatRequest req) =>
        PostForAsync<TableChatMessageDto>($"api/reserve/{Uri.EscapeDataString(code)}/chat", req);
    public Task<List<TableChatThreadDto>?> GetTableChatThreadsAsync() =>
        GetAsync<List<TableChatThreadDto>>("api/tablechats");
    public Task<List<TableChatMessageDto>?> GetTableChatMessagesAsync(int tableId, int afterId = 0) =>
        GetAsync<List<TableChatMessageDto>>($"api/tablechats/{tableId}?afterId={afterId}");
    public Task<(TableChatMessageDto? message, string? error)> SendStoreTableChatAsync(int tableId, SendTableChatRequest req) =>
        PostForAsync<TableChatMessageDto>($"api/tablechats/{tableId}", req);
    public Task<TableChatSyncDto?> SyncTableChatAsync(string code, int afterId, long stamp) =>
        GetAsync<TableChatSyncDto>($"api/reserve/{Uri.EscapeDataString(code)}/chat/sync?afterId={afterId}&stamp={stamp}");
    public Task<(TableChatMessageDto? message, string? error)> EditTableChatAsync(string code, int id, string text) =>
        SendForAsync<TableChatMessageDto>(HttpMethod.Put, $"api/reserve/{Uri.EscapeDataString(code)}/chat/{id}", new EditTableChatRequest(text));
    public Task<(TableChatMessageDto? message, string? error)> DeleteTableChatAsync(string code, int id) =>
        DeleteForAsync<TableChatMessageDto>($"api/reserve/{Uri.EscapeDataString(code)}/chat/{id}");
    public Task<(bool, string?)> MarkGuestChatReadAsync(string code, int lastId) =>
        SendAsync(HttpMethod.Post, $"api/reserve/{Uri.EscapeDataString(code)}/chat/read", new MarkChatReadRequest(lastId));

    // ---------- Team chat: direct messages between teammates ----------
    public async Task<int> GetTeamUnreadAsync() =>
        await GetAsync<int>("api/communitychat/unread");
    public Task<List<TeamContactDto>?> GetTeamContactsAsync() =>
        GetAsync<List<TeamContactDto>>("api/communitychat/contacts");
    public Task<CommunityChatSyncDto?> SyncTeamChatAsync(int otherId, int afterId, long stamp) =>
        GetAsync<CommunityChatSyncDto>($"api/communitychat/thread/{otherId}/sync?afterId={afterId}&stamp={stamp}");
    public Task<(CommunityChatMessageDto? message, string? error)> SendTeamChatAsync(int otherId, SendTableChatRequest req) =>
        PostForAsync<CommunityChatMessageDto>($"api/communitychat/thread/{otherId}", req);
    public Task<(CommunityChatMessageDto? message, string? error)> EditTeamChatAsync(int id, string text) =>
        SendForAsync<CommunityChatMessageDto>(HttpMethod.Put, $"api/communitychat/messages/{id}", new EditTableChatRequest(text));
    public Task<(CommunityChatMessageDto? message, string? error)> DeleteTeamChatAsync(int id) =>
        DeleteForAsync<CommunityChatMessageDto>($"api/communitychat/messages/{id}");

    public Task<TableChatSyncDto?> SyncStoreTableChatAsync(int tableId, int afterId, long stamp) =>
        GetAsync<TableChatSyncDto>($"api/tablechats/{tableId}/sync?afterId={afterId}&stamp={stamp}");
    public Task<(TableChatMessageDto? message, string? error)> EditStoreTableChatAsync(int tableId, int id, string text) =>
        SendForAsync<TableChatMessageDto>(HttpMethod.Put, $"api/tablechats/{tableId}/messages/{id}", new EditTableChatRequest(text));
    public Task<(TableChatMessageDto? message, string? error)> DeleteStoreTableChatAsync(int tableId, int id) =>
        DeleteForAsync<TableChatMessageDto>($"api/tablechats/{tableId}/messages/{id}");
    public Task<(bool, string?)> MarkStoreChatReadAsync(int tableId, int lastId) =>
        SendAsync(HttpMethod.Post, $"api/tablechats/{tableId}/read", new MarkChatReadRequest(lastId));

    public Task<TableChatUnreadDto?> GetTableChatUnreadAsync() =>
        GetAsync<TableChatUnreadDto>("api/tablechats/unread");
    public Task<(bool, string?)> MarkTableChatSeenAsync() =>
        SendAsync(HttpMethod.Post, "api/tablechats/seen", new { });
    public Task<(LoginResponse?, string?)> GooglePartnerAsync(string idToken) =>
        PostForAsync<LoginResponse>("api/auth/google-partner", new GoogleLoginRequest(idToken), authCall: true);
    public Task<(bool, string?)> SetSetupStepAsync(int step) =>
        SendAsync(HttpMethod.Post, $"api/restaurants/mine/setup-step/{step}", new { });
    public Task<List<StoreMemberDto>?> GetTeamAsync() => GetAsync<List<StoreMemberDto>>("api/team");

    // ---------- Who is at work, and where they have been ----------
    public Task<List<TeamActivityDto>?> GetTeamActivityAsync() =>
        GetAsync<List<TeamActivityDto>>("api/teamactivity");
    public Task<List<TeamVisitDto>?> GetTeamVisitsAsync(int userId, DateTime? from, DateTime? to) =>
        GetAsync<List<TeamVisitDto>>($"api/teamactivity/{userId}/visits" +
            $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
    public Task TeamBeatAsync(string page) =>
        SendAsync(HttpMethod.Post, "api/teamactivity/beat", new TeamBeatRequest(page));

    /// <summary>Upload (or clear, with null) my profile picture.</summary>
    public Task<(bool, string?)> UpdateAvatarPhotoAsync(string? photo) =>
        SendAsync(HttpMethod.Put, "api/auth/avatar/photo", new UpdateAvatarPhotoRequest(photo));

    // ---------- Roles the owner writes themselves ----------
    public Task<List<StoreRoleDefDto>?> GetCustomRolesAsync() =>
        GetAsync<List<StoreRoleDefDto>>("api/team/roles");
    public Task<(StoreRoleDefDto? role, string? error)> CreateCustomRoleAsync(SaveStoreRoleDefRequest req) =>
        PostForAsync<StoreRoleDefDto>("api/team/roles", req);
    public Task<(StoreRoleDefDto? role, string? error)> UpdateCustomRoleAsync(int id, SaveStoreRoleDefRequest req) =>
        SendForAsync<StoreRoleDefDto>(HttpMethod.Put, $"api/team/roles/{id}", req);
    public Task<(bool, string?)> DeleteCustomRoleAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/team/roles/{id}");
    // ---------- The store's shared calendar, with reminders ----------
    public Task<List<CalendarEventDto>?> GetCalendarAsync(DateTime? from = null, DateTime? to = null) =>
        GetAsync<List<CalendarEventDto>>("api/calendar" + (from is null ? "" : $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));
    public Task<(CalendarEventDto? saved, string? error)> CreateCalendarEventAsync(SaveCalendarEventRequest req) =>
        PostForAsync<CalendarEventDto>("api/calendar", req);
    public Task<(CalendarEventDto? saved, string? error)> UpdateCalendarEventAsync(int id, SaveCalendarEventRequest req) =>
        SendForAsync<CalendarEventDto>(HttpMethod.Put, $"api/calendar/{id}", req);
    public Task<(bool, string?)> DeleteCalendarEventAsync(int id) => SendAsync(HttpMethod.Delete, $"api/calendar/{id}");
    public Task<List<CalendarReminderDto>?> GetDueRemindersAsync() => GetAsync<List<CalendarReminderDto>>("api/calendar/reminders/due");
    public Task<(bool, string?)> DismissReminderAsync(int id) => SendAsync(HttpMethod.Post, $"api/calendar/{id}/dismiss");

    // ---------- Attendance: "I'm here" / "Leaving", each with a place ----------
    public Task<MyAttendanceDto?> GetMyAttendanceAsync() => GetAsync<MyAttendanceDto>("api/attendance/me");
    public Task<(MyAttendanceDto? state, string? error)> ClockInAsync(ClockRequest req) =>
        PostForAsync<MyAttendanceDto>("api/attendance/in", req);
    public Task<(MyAttendanceDto? state, string? error)> ClockOutAsync(ClockRequest req) =>
        PostForAsync<MyAttendanceDto>("api/attendance/out", req);
    public Task<(MyAttendanceDto? state, string? error)> RequestLeaveAsync(LeaveRequestDto req) =>
        PostForAsync<MyAttendanceDto>("api/attendance/leave", req);
    public Task<(bool, string?)> CancelLeaveAsync(int id) => SendAsync(HttpMethod.Delete, $"api/attendance/leave/{id}");
    public Task<(bool, string?)> DecideLeaveAsync(int id, bool approve, string? note) =>
        SendAsync(HttpMethod.Post, $"api/attendance/leave/{id}/{(approve ? "approve" : "reject")}", new LeaveDecisionRequest(note));
    public Task<AttendanceReportDto?> GetAttendanceReportAsync(DateTime from, DateTime to, int? userId = null) =>
        GetAsync<AttendanceReportDto>($"api/attendance/report?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" + (userId is > 0 ? $"&userId={userId}" : ""));

    // ---------- Surveys: the store asks, the guests tick ----------
    public Task<List<SurveyDto>?> GetSurveysAsync() => GetAsync<List<SurveyDto>>("api/surveys");
    public Task<(SurveyDto? survey, string? error)> CreateSurveyAsync(SaveSurveyRequest req) =>
        PostForAsync<SurveyDto>("api/surveys", req);
    public Task<(SurveyDto? survey, string? error)> UpdateSurveyAsync(string id, SaveSurveyRequest req) =>
        SendForAsync<SurveyDto>(HttpMethod.Put, $"api/surveys/{id}", req);
    public Task<(bool, string?)> DeleteSurveyAsync(string id) => SendAsync(HttpMethod.Delete, $"api/surveys/{id}");
    public Task<SurveyResultsDto?> GetSurveyResultsAsync(string id) => GetAsync<SurveyResultsDto>($"api/surveys/{id}/results");
    /// <summary>The store's live survey for a guest — null when it is not asking anything.</summary>
    public Task<PublicSurveyDto?> GetPublicSurveyAsync(int storeId) => GetAsync<PublicSurveyDto>($"api/surveys/public/{storeId}");
    public Task<(bool, string?)> AnswerSurveyAsync(int storeId, string surveyId, SubmitSurveyRequest req) =>
        SendAsync(HttpMethod.Post, $"api/surveys/public/{storeId}/{surveyId}/answer", req);

    // ---------- Support tickets: the partner reports, the desk fixes ----------
    public Task<SupportPageDto?> GetSupportTicketsAsync(string status = "all", int skip = 0, int take = 50) =>
        GetAsync<SupportPageDto>($"api/support/tickets?status={status}&skip={skip}&take={take}");
    public Task<SupportTicketDetailDto?> GetSupportTicketAsync(string id) =>
        GetAsync<SupportTicketDetailDto>($"api/support/tickets/{id}");
    public Task<(SupportTicketDetailDto? ticket, string? error)> CreateSupportTicketAsync(CreateTicketRequest req) =>
        PostForAsync<SupportTicketDetailDto>("api/support/tickets", req);
    public Task<(SupportTicketDetailDto? ticket, string? error)> ReplySupportTicketAsync(string id, TicketReplyRequest req) =>
        PostForAsync<SupportTicketDetailDto>($"api/support/tickets/{id}/messages", req);
    public Task<(SupportTicketDetailDto? ticket, string? error)> CloseSupportTicketAsync(string id) =>
        PostForAsync<SupportTicketDetailDto>($"api/support/tickets/{id}/close", new { });
    public Task<(SupportTicketDetailDto? ticket, string? error)> RateSupportTicketAsync(string id, TicketRateRequest req) =>
        PostForAsync<SupportTicketDetailDto>($"api/support/tickets/{id}/rate", req);
    public Task<SupportFileDto?> GetSupportFileAsync(string id, string fileId) =>
        GetAsync<SupportFileDto>($"api/support/tickets/{id}/files/{fileId}");

    // The support desk (administrators) — every store
    public Task<SupportPageDto?> AdminSupportTicketsAsync(string status = "all", string? search = null, int skip = 0, int take = 50) =>
        GetAsync<SupportPageDto>($"api/admin/support/tickets?status={status}&search={Uri.EscapeDataString(search ?? "")}&skip={skip}&take={take}");
    public Task<SupportSummaryDto?> AdminSupportSummaryAsync() => GetAsync<SupportSummaryDto>("api/admin/support/summary");

    // ---------- Where the stock is kept ----------
    public Task<WarehouseBoardDto?> WarehousesAsync() => GetAsync<WarehouseBoardDto>("api/warehouses");
    public Task<List<WarehouseStockDto>?> WarehouseStockAsync(int id) =>
        GetAsync<List<WarehouseStockDto>>($"api/warehouses/{id}/stock");
    public Task<(bool ok, string? error)> CreateWarehouseAsync(SaveWarehouseRequest req) =>
        SendAsync(HttpMethod.Post, "api/warehouses", req);
    public Task<(bool ok, string? error)> UpdateWarehouseAsync(int id, SaveWarehouseRequest req) =>
        SendAsync(HttpMethod.Put, $"api/warehouses/{id}", req);
    public Task<(bool ok, string? error)> DeleteWarehouseAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/warehouses/{id}");
    public Task<(bool ok, string? error)> MoveWarehouseNodeAsync(int id, double x, double y) =>
        SendAsync(HttpMethod.Put, $"api/warehouses/{id}/position", new MoveNodeRequest(x, y));
    public Task<(bool ok, string? error)> LinkWarehousesAsync(int fromId, int toId) =>
        SendAsync(HttpMethod.Post, $"api/warehouses/{fromId}/link/{toId}");
    public Task<(bool ok, string? error)> UnlinkWarehousesAsync(int fromId, int toId) =>
        SendAsync(HttpMethod.Delete, $"api/warehouses/{fromId}/link/{toId}");
    public Task<List<WarehouseStockDto>?> CentralStockAsync() =>
        GetAsync<List<WarehouseStockDto>>("api/warehouses/central/stock");
    public Task<(bool ok, string? error)> CentralTransferAsync(CentralTransferRequest req) =>
        SendAsync(HttpMethod.Post, "api/warehouses/central/transfer", req);
    // ---- loyalty ----
    public Task<LoyaltyBoardDto?> LoyaltyBoardAsync() => GetAsync<LoyaltyBoardDto>("api/loyalty");
    public Task<LoyaltyLiveDto?> LoyaltyLiveAsync() => GetAsync<LoyaltyLiveDto>("api/loyalty/live");
    public Task<List<LoyaltyMemberDto>?> LoyaltyLookupAsync(string q) =>
        GetAsync<List<LoyaltyMemberDto>>($"api/loyalty/lookup?q={Uri.EscapeDataString(q ?? "")}");
    public Task<List<LoyaltyEventDto>?> LoyaltyHistoryAsync(int userId) =>
        GetAsync<List<LoyaltyEventDto>>($"api/loyalty/members/{userId}");
    public Task<(bool ok, string? error)> SaveLoyaltyProgramAsync(SaveLoyaltyProgramRequest req) =>
        SendAsync(HttpMethod.Put, "api/loyalty/program", req);
    public Task<(LoyaltyMemberDto? member, string? error)> LoyaltyEarnAsync(LoyaltyEarnRequest req) =>
        PostForAsync<LoyaltyMemberDto>("api/loyalty/earn", req);
    public Task<(LoyaltyMemberDto? member, string? error)> LoyaltyRedeemAsync(LoyaltyRedeemRequest req) =>
        PostForAsync<LoyaltyMemberDto>("api/loyalty/redeem", req);
    public Task<(LoyaltyMemberDto? member, string? error)> LoyaltyAdjustAsync(LoyaltyAdjustRequest req) =>
        PostForAsync<LoyaltyMemberDto>("api/loyalty/adjust", req);
    public Task<(bool ok, string? error)> TransferStockAsync(TransferRequest req) =>
        SendAsync(HttpMethod.Post, "api/warehouses/transfer", req);
    public Task<List<TransferDto>?> TransfersAsync(int take = 60) =>
        GetAsync<List<TransferDto>>($"api/warehouses/transfers?take={take}");

    // ---------- What was thrown away ----------
    public Task<WasteBoardDto?> WasteBoardAsync(int days = 30) =>
        GetAsync<WasteBoardDto>($"api/waste?days={days}");
    public Task<(bool ok, string? error)> RecordWasteAsync(RecordWasteRequest req) =>
        SendAsync(HttpMethod.Post, "api/waste", req);
    public Task<(bool ok, string? error)> UndoWasteAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/waste/{id}");

    // ---------- The kitchen screen ----------
    public Task<KdsBoardDto?> KdsBoardAsync() => GetAsync<KdsBoardDto>("api/kds/board");
    public Task<(bool ok, string? error)> KdsStartAsync(string key) =>
        SendAsync(HttpMethod.Post, "api/kds/ticket/start", new KdsActionRequest(key));
    public Task<(bool ok, string? error)> KdsDoneAsync(string key) =>
        SendAsync(HttpMethod.Post, "api/kds/ticket/done", new KdsActionRequest(key));
    public Task<(bool ok, string? error)> KdsRecallAsync(string key) =>
        SendAsync(HttpMethod.Post, "api/kds/ticket/recall", new KdsActionRequest(key));
    public Task<(bool ok, string? error)> KdsLineAsync(string key, int lineId) =>
        SendAsync(HttpMethod.Post, "api/kds/ticket/line", new KdsLineActionRequest(key, lineId));

    // ---------- What people ask the assistants ----------
    public Task<(bool ok, string? error)> LogBotChatAsync(BotChatLogRequest req) =>
        SendAsync(HttpMethod.Post, "api/track/bot", req);
    public Task<BotChatIpsDto?> AdminBotChatsAsync(int skip = 0, int take = 50, string? search = null) =>
        GetAsync<BotChatIpsDto>($"api/admin/bot-chats?skip={skip}&take={take}&search={Uri.EscapeDataString(search ?? "")}");
    public Task<BotChatTurnsDto?> AdminBotChatTurnsAsync(string ip, int limit = 500) =>
        GetAsync<BotChatTurnsDto>($"api/admin/bot-chats/turns?ip={Uri.EscapeDataString(ip)}&limit={limit}");

    // ---------- Who joined: the administrators' bell ----------
    public Task<AdminAlertsDto?> AdminAlertsAsync(int limit = 50) =>
        GetAsync<AdminAlertsDto>($"api/admin/alerts?limit={limit}");
    public Task<AdminAlertsDto?> AdminAlertsSummaryAsync() =>
        GetAsync<AdminAlertsDto>("api/admin/alerts/summary");
    public Task<(bool ok, string? error)> AdminAlertsReadAsync() =>
        SendAsync(HttpMethod.Post, "api/admin/alerts/read", new { });

    // ---------- Contact form inbox (admin) ----------
    public Task<ContactSummaryDto?> AdminContactSummaryAsync() => GetAsync<ContactSummaryDto>("api/contact/admin/summary");
    public Task<ContactPageDto?> AdminContactAsync(int skip = 0, int take = 50) =>
        GetAsync<ContactPageDto>($"api/contact/admin?skip={skip}&take={take}");
    public async Task<bool> AdminContactReadAsync(string id, bool read = true) =>
        (await SendAsync(HttpMethod.Post, $"api/contact/admin/{Uri.EscapeDataString(id)}/read?read={(read ? "true" : "false")}")).ok;
    public async Task<bool> AdminContactDeleteAsync(string id) =>
        (await SendAsync(HttpMethod.Delete, $"api/contact/admin/{Uri.EscapeDataString(id)}")).ok;
    public Task<SupportTicketDetailDto?> AdminSupportTicketAsync(string id) =>
        GetAsync<SupportTicketDetailDto>($"api/admin/support/tickets/{id}");
    public Task<(SupportTicketDetailDto? ticket, string? error)> AdminReplySupportAsync(string id, TicketReplyRequest req) =>
        PostForAsync<SupportTicketDetailDto>($"api/admin/support/tickets/{id}/messages", req);
    public Task<(SupportTicketDetailDto? ticket, string? error)> AdminSetSupportStatusAsync(string id, TicketStatusRequest req) =>
        PostForAsync<SupportTicketDetailDto>($"api/admin/support/tickets/{id}/status", req);
    public Task<(SupportTicketDetailDto? ticket, string? error)> AdminAssignSupportAsync(string id, TicketAssignRequest req) =>
        PostForAsync<SupportTicketDetailDto>($"api/admin/support/tickets/{id}/assign", req);
    public Task<SupportFileDto?> AdminSupportFileAsync(string id, string fileId) =>
        GetAsync<SupportFileDto>($"api/admin/support/tickets/{id}/files/{fileId}");

    public Task<List<PresetRolePermsDto>?> GetPresetRolesAsync() =>
        GetAsync<List<PresetRolePermsDto>>("api/team/presets");
    public Task<(PresetRolePermsDto? preset, string? error)> SavePresetRoleAsync(StoreRole role, List<string> perms) =>
        SendForAsync<PresetRolePermsDto>(HttpMethod.Put, $"api/team/presets/{role}", new SavePresetPermsRequest(perms));
    public Task<(bool, string?)> ResetPresetRoleAsync(StoreRole role) =>
        SendAsync(HttpMethod.Delete, $"api/team/presets/{role}");
    public Task<(StoreMemberDto?, string?)> AddTeamMemberAsync(SaveStoreMemberRequest req) =>
        PostForAsync<StoreMemberDto>("api/team", req);
    public Task<(StoreMemberDto?, string?)> UpdateTeamMemberAsync(int id, SaveStoreMemberRequest req) =>
        SendForAsync<StoreMemberDto>(HttpMethod.Put, $"api/team/{id}", req);
    public Task<(bool, string?)> RemoveTeamMemberAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/team/{id}");
    public Task<(MapPointDto?, string?)> ParseMapLinkAsync(string url) =>
        PostForAsync<MapPointDto>("api/restaurants/maplink", new MapLinkRequest(url));
    public Task<List<ChatArchiveSessionDto>?> GetChatHistoryAsync() =>
        GetAsync<List<ChatArchiveSessionDto>>("api/tablechats/history");
    public Task<List<TableChatMessageDto>?> GetChatHistoryMessagesAsync(string archiveId) =>
        GetAsync<List<TableChatMessageDto>>($"api/tablechats/history/{Uri.EscapeDataString(archiveId)}");
    // ---------- Contracts: the store's standing supply agreements ----------
    public Task<List<ContractDto>?> GetContractsAsync() => GetAsync<List<ContractDto>>("api/contracts");
    public Task<(ContractDto? contract, string? error)> CreateContractAsync(SaveContractRequest req) =>
        PostForAsync<ContractDto>("api/contracts", req);
    public Task<(ContractDto? contract, string? error)> UpdateContractAsync(int id, SaveContractRequest req) =>
        SendForAsync<ContractDto>(HttpMethod.Put, $"api/contracts/{id}", req);
    public Task<(bool ok, string? error)> DeleteContractAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/contracts/{id}");
    /// <summary>Email the contract to its customer, wearing the store's logo and details.</summary>
    public Task<(ContractDto? contract, string? error)> EmailContractAsync(int id) =>
        PostForAsync<ContractDto>($"api/contracts/{id}/email", new { });
    /// <summary>The public page behind an emailed contract link. Signed code, no account.</summary>
    public Task<PublicContractDto?> GetPublicContractAsync(string code) =>
        GetAsync<PublicContractDto>($"api/contract/{Uri.EscapeDataString(code)}");
    /// <summary>The customer's yes, from the public page.</summary>
    public Task<(PublicContractDto? contract, string? error)> AcceptContractAsync(string code, string? name = null) =>
        PostForAsync<PublicContractDto>($"api/contract/{Uri.EscapeDataString(code)}/accept", new AcceptContractRequest(name));
    /// <summary>The customer writes back to the store from the public page.</summary>
    public Task<(PublicContractDto? contract, string? error)> ReplyContractAsync(string code, string text) =>
        PostForAsync<PublicContractDto>($"api/contract/{Uri.EscapeDataString(code)}/reply", new ReplyContractRequest(text));
    /// <summary>The customer's counter-offer: extra products and/or a new term.</summary>
    public Task<(PublicContractDto? contract, string? error)> ProposeContractAsync(string code, ProposeContractRequest req) =>
        PostForAsync<PublicContractDto>($"api/contract/{Uri.EscapeDataString(code)}/propose", req);

    public Task<List<ReservationDto>?> GetReservationsAsync(bool history = false) =>
        GetAsync<List<ReservationDto>>($"api/reservations?history={history}");
    public Task<(ReservationDto? reservation, string? error)> CreateReservationAsync(OwnerReserveRequest req) =>
        PostForAsync<ReservationDto>("api/reservations", req);
    public Task<(ReservationDto? reservation, string? error)> SetReservationStatusAsync(int id, string status) =>
        PostForAsync<ReservationDto>($"api/reservations/{id}/status", new SetReservationStatusRequest(status));
    public Task<(ReservationDto? reservation, string? error)> ReserveStoreTableAsync(int storeId, PublicReserveRequest req) =>
        PostForAsync<ReservationDto>($"api/restaurants/{storeId}/reserve", req);
    public Task<List<PublicSalonDto>?> GetPublicFloorAsync(int storeId) =>
        GetAsync<List<PublicSalonDto>>($"api/restaurants/{storeId}/floor");
    /// <summary>The tables already taken in that window — greyed out before anyone types a name.</summary>
    public Task<List<int>?> GetBusyTablesAsync(int storeId, DateTime at, int minutes) =>
        GetAsync<List<int>>($"api/restaurants/{storeId}/floor/busy?at={at:yyyy-MM-ddTHH:mm:ss}&minutes={minutes}");
    public Task<List<MyReservationDto>?> GetMyReservationsAsync() =>
        GetAsync<List<MyReservationDto>>("api/reservations/mine");
    public Task<(ReservationDto? reservation, string? error)> CancelMyReservationAsync(int id) =>
        PostForAsync<ReservationDto>($"api/reservations/{id}/cancel-mine", new { });
    public Task<(StoreTableDto? table, string? error)> SeatTableAsync(int id, SeatTableRequest req) =>
        PostForAsync<StoreTableDto>($"api/storetables/{id}/seat", req);
    public Task<(StoreTableDto? table, string? error)> ClearTableAsync(int id) =>
        PostForAsync<StoreTableDto>($"api/storetables/{id}/clear", new { });

    /// <summary>An order the shop takes at the counter or by phone, for someone in its book.</summary>
    public Task<(OrderDto? order, string? error)> PlaceCounterOrderAsync(PlaceCounterOrderRequest req) =>
        PostForAsync<OrderDto>("api/orders/counter", req);

    // ---------- Lookups ----------
    public Task<List<CuisineDto>?> GetCuisinesAsync() => GetAsync<List<CuisineDto>>("api/lookups/cuisines");
    public Task<(bool, string?)> CreateCuisineAsync(SaveCuisineRequest req) => SendAsync(HttpMethod.Post, "api/lookups/cuisines", req);
    public Task<(bool, string?)> UpdateCuisineAsync(int id, SaveCuisineRequest req) => SendAsync(HttpMethod.Put, $"api/lookups/cuisines/{id}", req);
    public Task<(bool, string?)> DeleteCuisineAsync(int id) => SendAsync(HttpMethod.Delete, $"api/lookups/cuisines/{id}");

    // ---------- Restaurants: browse ----------
    /// <param name="storeType">
    /// Null browses EVERY vertical at once — restaurants, markets, pharmacies, flowers and
    /// shops together, which is what the home page shows before a vertical is picked.
    /// </param>
    public Task<List<RestaurantCardDto>?> BrowsePageAsync(StoreType? storeType, string? cuisineIds = null,
        string? sort = null, bool favoritesOnly = false, int skip = 0, int take = 30, string? search = null,
        double? lat = null, double? lng = null)
    {
        var url = $"api/restaurants?sort={sort}&skip={skip}&take={take}";
        if (storeType is not null) url += $"&storeType={(int)storeType}";
        if (!string.IsNullOrWhiteSpace(cuisineIds)) url += $"&cuisineIds={Uri.EscapeDataString(cuisineIds)}";
        if (favoritesOnly) url += "&favoritesOnly=true";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (lat is not null && lng is not null)
            url += FormattableString.Invariant($"&lat={lat}&lng={lng}");
        return GetAsync<List<RestaurantCardDto>>(url);
    }

    public Task<List<RestaurantCardDto>?> GetRestaurantsByIdsAsync(IEnumerable<int> ids) =>
        GetAsync<List<RestaurantCardDto>>($"api/restaurants/by-ids?ids={string.Join(',', ids)}");

    public Task<List<RestaurantCardDto>?> BrowseRestaurantsAsync(string? search = null, int? cuisineId = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        if (cuisineId is not null) query.Add($"cuisineId={cuisineId}");
        var suffix = query.Count > 0 ? "?" + string.Join("&", query) : "";
        return GetAsync<List<RestaurantCardDto>>($"api/restaurants{suffix}");
    }
    public Task<RestaurantDetailDto?> GetRestaurantAsync(int id) => GetAsync<RestaurantDetailDto>($"api/restaurants/{id}");
    public Task<List<ReviewDto>?> GetRestaurantReviewsAsync(int id) => GetAsync<List<ReviewDto>>($"api/restaurants/{id}/reviews");
    /// <summary>Dishes on offer right now — the customer home "Offers" strip.</summary>
    public Task<List<DealDto>?> GetDealsAsync(int take = 12) =>
        GetAsync<List<DealDto>>($"api/restaurants/deals?take={take}");

    /// <summary>
    /// Every available dish across every store, paged — the tail of the home page, which
    /// carries on into individual items once the shop grid is exhausted.
    /// </summary>
    public Task<List<DealDto>?> GetAllItemsAsync(int skip = 0, int take = 24) =>
        GetAsync<List<DealDto>>($"api/restaurants/items?skip={skip}&take={take}");

    /// <summary>The shops the platform is putting forward, with a taste of their menus.</summary>
    public Task<SuggestionsDto?> GetSuggestionsAsync(int take = 12) =>
        GetAsync<SuggestionsDto>($"api/restaurants/suggested?take={take}");

    /// <summary>The real shops, for the sitemap — the generated catalog is left out.</summary>
    public Task<List<SitemapStoreDto>?> GetSitemapStoresAsync() =>
        GetAsync<List<SitemapStoreDto>>("api/restaurants/sitemap");

    /// <param name="realOnly">
    /// Offer only real, hand-onboarded kitchens. The ordering assistant sets this — it
    /// puts food in a basket, and a generated demo shop cannot cook.
    /// </param>
    public Task<SearchResultsDto?> SearchAsync(string query, bool realOnly = false) =>
        GetAsync<SearchResultsDto>($"api/restaurants/search?query={Uri.EscapeDataString(query)}" +
            (realOnly ? "&realOnly=true" : ""));
    public Task<List<int>?> GetFavoriteRestaurantIdsAsync() => GetAsync<List<int>>("api/restaurants/favorites");
    public Task<(bool, string?)> ToggleFavoriteAsync(int id) => SendAsync(HttpMethod.Post, $"api/restaurants/{id}/favorite");
    public Task<List<PromoDto>?> GetPromosAsync() => GetAsync<List<PromoDto>>("api/coupons/promos");

    // ---------- Restaurants: owner ----------
    // The store profile (35 KB with its logo) is asked for by twenty pages; keep the last
    // answer for a short while, keyed to the session so a store switch or re-login never
    // shows another shop's data. Any write under restaurants/mine drops it.
    private MyRestaurantDto? _mine;
    private string? _mineToken;
    private DateTime _mineAt;

    private Task<MyRestaurantDto?>? _mineInFlight;

    public Task<MyRestaurantDto?> GetMyRestaurantAsync()
    {
        if (_mine is not null && _mineToken == state.Token && DateTime.UtcNow - _mineAt < TimeSpan.FromSeconds(90))
            return Task.FromResult<MyRestaurantDto?>(_mine);
        // The layout and the page ask at the same moment on every open — one request serves both.
        return _mineInFlight ??= FetchMineAsync();
    }

    private async Task<MyRestaurantDto?> FetchMineAsync()
    {
        try
        {
            var dto = await GetAsync<MyRestaurantDto>("api/restaurants/mine");
            if (dto is not null) { _mine = dto; _mineToken = state.Token; _mineAt = DateTime.UtcNow; }
            return dto;
        }
        finally { _mineInFlight = null; }
    }

    public void ForgetMyRestaurant() => _mine = null;

    // ---------- Short-link handles: orderorange.com/<slug> ----------
    public Task<SlugCheckDto?> CheckSlugAsync(string slug) =>
        GetAsync<SlugCheckDto>($"api/restaurants/slug-available?slug={Uri.EscapeDataString(slug)}");
    public Task<(bool, string?)> SetSlugAsync(string? slug) =>
        SendAsync(HttpMethod.Put, "api/restaurants/mine/slug", new SetSlugRequest(slug));
    public Task<SlugResolveDto?> ResolveSlugAsync(string slug) =>
        GetAsync<SlugResolveDto>($"api/restaurants/by-slug/{Uri.EscapeDataString(slug)}");

    /// <summary>A dine-in order from a scanned table QR — no account needed.</summary>
    public Task<(TableOrderResultDto? result, string? error)> PlaceTableOrderAsync(PlaceTableOrderRequest req) =>
        PostForAsync<TableOrderResultDto>("api/orders/table", req);

    // ---------- Invoices: cancel, edit, and the paper trail ----------
    public Task<List<InvoiceRowDto>?> GetInvoicesAsync(int days = 7, string? search = null, int skip = 0, int take = 25) =>
        GetAsync<List<InvoiceRowDto>>($"api/invoices?days={days}&skip={skip}&take={take}&search={Uri.EscapeDataString(search ?? "")}");
    public Task<InvoiceDetailDto?> GetInvoiceAsync(int id) =>
        GetAsync<InvoiceDetailDto>($"api/invoices/{id}");
    public Task<(bool, string?)> CancelInvoiceAsync(int id, string? reason) =>
        SendAsync(HttpMethod.Post, $"api/invoices/{id}/cancel", new CancelInvoiceRequest(reason));
    public Task<(bool, string?)> ReplaceInvoiceAsync(int id, ReplaceInvoiceRequest req) =>
        SendAsync(HttpMethod.Post, $"api/invoices/{id}/replace", req);
    public Task<List<CancelledInvoiceDto>?> GetCancelledInvoicesAsync(DateTime from, DateTime to, int skip = 0, int take = 25) =>
        GetAsync<List<CancelledInvoiceDto>>($"api/invoices/cancelled?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&skip={skip}&take={take}");
    public Task<List<InvoiceEditDto>?> GetInvoiceEditsAsync(DateTime from, DateTime to, int skip = 0, int take = 25) =>
        GetAsync<List<InvoiceEditDto>>($"api/invoices/edits?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&skip={skip}&take={take}");
    public Task<DailyInvoicesDto?> GetDailyInvoicesAsync(DateTime date) =>
        GetAsync<DailyInvoicesDto>($"api/invoices/daily?date={date:yyyy-MM-dd}");

    /// <summary>A printed card's table NUMBER → the signed /reserve code for that table.</summary>
    public Task<TableCodeDto?> ResolveTableAsync(int storeId, int table) =>
        GetAsync<TableCodeDto>($"api/reserve/resolve?store={storeId}&table={table}");

    // ---------- Web Push: the server rings the partner's device, portal open or not ----------
    public Task<PushVapidDto?> GetPushVapidAsync() => GetAsync<PushVapidDto>("api/push/vapid");
    public Task<(bool ok, string? error)> SubscribePushAsync(PushSubscribeRequest req) =>
        SendAsync(HttpMethod.Post, "api/push/subscribe", req);
    public Task<(bool ok, string? error)> SendTestPushAsync() =>
        SendAsync(HttpMethod.Post, "api/push/test", new { });

    // ---------- One partner, several businesses ----------
    // Same treatment as the profile: the layout's switcher and a few pages all ask for
    // the store list (one logo per store inside) — one answer serves them for 90 s.
    private List<StoreSummaryDto>? _stores;
    private string? _storesToken;
    private DateTime _storesAt;
    private Task<List<StoreSummaryDto>?>? _storesInFlight;

    public Task<List<StoreSummaryDto>?> GetMyStoresAsync()
    {
        if (_stores is not null && _storesToken == state.Token && DateTime.UtcNow - _storesAt < TimeSpan.FromSeconds(90))
            return Task.FromResult<List<StoreSummaryDto>?>(_stores);
        return _storesInFlight ??= FetchStoresAsync();
    }

    private async Task<List<StoreSummaryDto>?> FetchStoresAsync()
    {
        try
        {
            var list = await GetAsync<List<StoreSummaryDto>>("api/restaurants/my-stores");
            if (list is not null) { _stores = list; _storesToken = state.Token; _storesAt = DateTime.UtcNow; }
            return list;
        }
        finally { _storesInFlight = null; }
    }

    /// <summary>Re-issues the session for another store the same account owns.</summary>
    public Task<(LoginResponse? user, string? error)> SwitchStoreAsync(int restaurantId) =>
        PostForAsync<LoginResponse>($"api/auth/switch-store/{restaurantId}", new { });

    /// <summary>Opens another business under the signed-in partner's own account.</summary>
    public Task<(StoreSummaryDto? store, string? error)> CreateMyStoreAsync(CreateMyStoreRequest req) =>
        PostForAsync<StoreSummaryDto>("api/restaurants/my-stores", req);
    public Task<(bool, string?)> UpdateMyRestaurantAsync(UpdateRestaurantRequest req) => SendAsync(HttpMethod.Put, "api/restaurants/mine", req);

    // ---------- Product → printer routing + till charge ----------
    public Task<List<PrintRouteDto>?> GetPrintRoutesAsync() => GetAsync<List<PrintRouteDto>>("api/restaurants/mine/print-routes");
    public Task<(bool, string?)> SavePrintRoutesAsync(List<PrintRouteDto> routes) => SendAsync(HttpMethod.Put, "api/restaurants/mine/print-routes", routes);
    public Task<(bool, string?)> SendTillChargeAsync(decimal amount, string? reference) =>
        SendAsync(HttpMethod.Post, "api/orders/till-charge", new SendTillChargeRequest(amount, reference));
    public Task<(bool, string?)> ToggleOpenAsync() => SendAsync(HttpMethod.Post, "api/restaurants/mine/toggle-open");
    public Task<List<DayHoursDto>?> GetMyHoursAsync() => GetAsync<List<DayHoursDto>>("api/restaurants/mine/hours");
    public Task<(bool, string?)> SaveMyHoursAsync(List<DayHoursDto> days) => SendAsync(HttpMethod.Put, "api/restaurants/mine/hours", days);

    // ---------- Menu (owner) ----------
    public Task<List<MenuCategoryDto>?> GetMyMenuAsync() => GetAsync<List<MenuCategoryDto>>("api/menu");
    /// <summary>The whole menu without photos — names, prices, codes; what the till needs to search and ring up.</summary>
    public Task<List<MenuCategoryDto>?> GetMyMenuOutlineAsync() => GetAsync<List<MenuCategoryDto>>("api/menu?light=true");
    /// <summary>Names, prices and availability only — for pages that pick products, never show them.</summary>
    public Task<List<MenuCategoryDto>?> GetMyMenuBasicAsync() => GetAsync<List<MenuCategoryDto>>($"api/menu?basic=true&lang={Uri.EscapeDataString(lang.Locale)}");
    /// <summary>One group's items WITH photos, fetched when the cashier opens that group.</summary>
    public Task<List<MenuItemDto>?> GetMyCategoryItemsAsync(int categoryId) => GetAsync<List<MenuItemDto>>($"api/menu/categories/{categoryId}/items");
    public Task<(bool, string?)> CreateCategoryAsync(SaveCategoryRequest req) => SendAsync(HttpMethod.Post, "api/menu/categories", req);
    public Task<(bool, string?)> UpdateCategoryAsync(int id, SaveCategoryRequest req) => SendAsync(HttpMethod.Put, $"api/menu/categories/{id}", req);
    public Task<(bool, string?)> DeleteCategoryAsync(int id) => SendAsync(HttpMethod.Delete, $"api/menu/categories/{id}");
    public Task<(bool, string?)> CreateMenuItemAsync(SaveMenuItemRequest req) => SendAsync(HttpMethod.Post, "api/menu/items", req);
    public Task<(bool, string?)> UpdateMenuItemAsync(int id, SaveMenuItemRequest req) => SendAsync(HttpMethod.Put, $"api/menu/items/{id}", req);
    public Task<(bool, string?)> ToggleMenuItemAsync(int id) => SendAsync(HttpMethod.Post, $"api/menu/items/{id}/toggle-available");
    public Task<(bool, string?)> DeleteMenuItemAsync(int id) => SendAsync(HttpMethod.Delete, $"api/menu/items/{id}");

    // ---------- Addresses ----------
    public Task<List<AddressDto>?> GetAddressesAsync() => GetAsync<List<AddressDto>>("api/addresses");
    public Task<(bool, string?)> CreateAddressAsync(SaveAddressRequest req) => SendAsync(HttpMethod.Post, "api/addresses", req);
    public Task<(bool, string?)> UpdateAddressAsync(int id, SaveAddressRequest req) => SendAsync(HttpMethod.Put, $"api/addresses/{id}", req);
    public Task<(bool, string?)> DeleteAddressAsync(int id) => SendAsync(HttpMethod.Delete, $"api/addresses/{id}");

    // ---------- Orders: customer ----------
    public Task<(OrderDto? order, string? error)> PlaceOrderAsync(PlaceOrderRequest req) =>
        PostForAsync<OrderDto>("api/orders", req);
    public Task<List<OrderDto>?> GetMyOrdersAsync(int skip = 0, int take = 20) =>
        GetAsync<List<OrderDto>>($"api/orders/mine?skip={skip}&take={take}");
    public Task<OrderDto?> GetOrderAsync(int id) => GetAsync<OrderDto>($"api/orders/{id}");

    /// <summary>Looks up a bill from a scanned receipt QR — no sign-in needed.</summary>
    public Task<PublicBillDto?> VerifyBillAsync(string code) =>
        GetAsync<PublicBillDto>($"api/orders/verify/{Uri.EscapeDataString(code)}");
    public Task<(bool, string?)> CancelOrderAsync(int id, string? reason = null) =>
        SendAsync(HttpMethod.Post, $"api/orders/{id}/cancel", new CancelOrderRequest(reason));

    // ---------- Orders: restaurant ----------
    public Task<List<OrderDto>?> GetRestaurantBoardAsync() => GetAsync<List<OrderDto>>("api/orders/restaurant/board");

    // ---- The shop's own delivery run ----
    public Task<List<OrderDto>?> GetMyDeliveriesAsync() => GetAsync<List<OrderDto>>("api/orders/restaurant/deliveries");
    public Task<(bool, string?)> AssignCourierAsync(int orderId, string name, string? phone) =>
        SendAsync(HttpMethod.Post, $"api/orders/{orderId}/courier", new AssignCourierRequest(name, phone));
    public Task<(bool, string?)> PushCourierLocationAsync(int orderId, double lat, double lng) =>
        SendAsync(HttpMethod.Post, $"api/orders/{orderId}/courier-location", new CourierLocationRequest(lat, lng));
    public Task<(bool, string?)> MarkSelfDeliveredAsync(int orderId) =>
        SendAsync(HttpMethod.Post, $"api/orders/{orderId}/self-delivered");
    public Task<List<DayRevenueDto>?> GetWeekRevenueAsync() =>
        GetAsync<List<DayRevenueDto>>("api/orders/restaurant/week-revenue");
    public Task<List<OrderDto>?> GetRestaurantHistoryAsync(int skip = 0, int take = 25) =>
        GetAsync<List<OrderDto>>($"api/orders/restaurant/history?skip={skip}&take={take}");
    public Task<(bool, string?)> AcceptOrderAsync(int id) => SendAsync(HttpMethod.Post, $"api/orders/{id}/accept");
    public Task<(bool, string?)> RejectOrderAsync(int id, string reason) =>
        SendAsync(HttpMethod.Post, $"api/orders/{id}/reject", new RejectOrderRequest(reason));
    public Task<(bool, string?)> StartPreparingAsync(int id) => SendAsync(HttpMethod.Post, $"api/orders/{id}/preparing");
    public Task<(bool, string?)> MarkReadyAsync(int id) => SendAsync(HttpMethod.Post, $"api/orders/{id}/ready");

    // ---------- Orders: driver ----------
    public Task<List<OrderDto>?> GetAvailableDeliveriesAsync() => GetAsync<List<OrderDto>>("api/orders/driver/available");
    public Task<OrderDto?> GetDriverActiveOrderAsync() => GetAsync<OrderDto>("api/orders/driver/active");
    public Task<List<OrderDto>?> GetDriverHistoryAsync(int skip = 0, int take = 25, DateTime? from = null, DateTime? to = null) =>
        GetAsync<List<OrderDto>>($"api/orders/driver/history?skip={skip}&take={take}"
            + (from is null ? "" : $"&from={from:yyyy-MM-dd}") + (to is null ? "" : $"&to={to:yyyy-MM-dd}"));
    public Task<(bool, string?)> ClaimDeliveryAsync(int id) => SendAsync(HttpMethod.Post, $"api/orders/{id}/claim");
    public Task<(bool, string?)> PickupOrderAsync(int id) => SendAsync(HttpMethod.Post, $"api/orders/{id}/pickup");
    public Task<(bool, string?)> OnTheWayAsync(int id) => SendAsync(HttpMethod.Post, $"api/orders/{id}/on-the-way");
    public Task<(bool, string?)> DeliveredAsync(int id) => SendAsync(HttpMethod.Post, $"api/orders/{id}/delivered");

    // ---------- Store photo gallery ----------
    public Task<List<RestaurantPhotoDto>?> GetStorePhotosAsync(int restaurantId) => GetAsync<List<RestaurantPhotoDto>>($"api/restaurants/{restaurantId}/photos");
    public Task<List<RestaurantPhotoDto>?> GetMyStorePhotosAsync() => GetAsync<List<RestaurantPhotoDto>>("api/restaurants/mine/photos");
    public Task<(bool, string?)> AddMyStorePhotoAsync(string data) => SendAsync(HttpMethod.Post, "api/restaurants/mine/photos", new AddStorePhotoRequest(data));
    public Task<(bool, string?)> DeleteMyStorePhotoAsync(int id) => SendAsync(HttpMethod.Delete, $"api/restaurants/mine/photos/{id}");
    public Task<(bool, string?)> SetMainStorePhotoAsync(int id) => SendAsync(HttpMethod.Post, $"api/restaurants/mine/photos/{id}/main");
    /// <summary>Sets the shop's logo image; pass "" to clear it and fall back to the emoji.</summary>
    public Task<(bool, string?)> SetMyStoreLogoAsync(string data) => SendAsync(HttpMethod.Put, "api/restaurants/mine/logo", new AddStorePhotoRequest(data));

    // ---------- Raw materials (the kitchen's store cupboard) ----------
    public Task<StoreMaterialsDto?> GetMaterialsAsync(string category = "all", string? search = null) =>
        GetAsync<StoreMaterialsDto>($"api/materials?category={category}&search={Uri.EscapeDataString(search ?? "")}");
    public Task<(bool, string?)> CreateMaterialAsync(SaveStoreMaterialRequest req) =>
        SendAsync(HttpMethod.Post, "api/materials", req);
    public Task<(bool, string?)> UpdateMaterialAsync(int id, SaveStoreMaterialRequest req) =>
        SendAsync(HttpMethod.Put, $"api/materials/{id}", req);
    public Task<(bool, string?)> AdjustMaterialAsync(int id, decimal delta) =>
        SendAsync(HttpMethod.Post, $"api/materials/{id}/adjust", new AdjustStoreMaterialRequest(delta));
    public Task<(bool, string?)> DeleteMaterialAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/materials/{id}");

    public Task<AllRecipesDto?> GetRecipesAsync() => GetAsync<AllRecipesDto>("api/materials/recipes");
    public Task<(bool, string?)> SaveRecipeAsync(int menuItemId, SaveRecipeRequest req) =>
        SendAsync(HttpMethod.Put, $"api/materials/recipes/{menuItemId}", req);
    public Task<MaterialPurchasePageDto?> GetPurchasesAsync(int skip = 0, int take = 30) =>
        GetAsync<MaterialPurchasePageDto>($"api/materials/purchases?skip={skip}&take={take}");
    public Task<(bool, string?)> CreatePurchaseAsync(SavePurchaseRequest req) =>
        SendAsync(HttpMethod.Post, "api/materials/purchases", req);
    public Task<(bool, string?)> TogglePurchasePaidAsync(int id) =>
        SendAsync(HttpMethod.Post, $"api/materials/purchases/{id}/toggle-paid");
    public Task<(bool, string?)> PayInstallmentAsync(int purchaseId, int paymentId, string? method = null) =>
        SendAsync(HttpMethod.Post, $"api/materials/purchases/{purchaseId}/payments/{paymentId}/pay{(method is null ? "" : $"?method={method}")}");
    public Task<(bool, string?)> DeletePurchaseAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/materials/purchases/{id}");
    public Task<ConsumptionReportDto?> GetConsumptionAsync(string period = "week") =>
        GetAsync<ConsumptionReportDto>($"api/materials/consumption?period={period}");
    public Task<List<PurchaseTemplateDto>?> GetPurchaseTemplatesAsync() =>
        GetAsync<List<PurchaseTemplateDto>>("api/materials/purchase-templates");
    public Task<(bool, string?)> SavePurchaseTemplateAsync(SavePurchaseTemplateRequest req) =>
        SendAsync(HttpMethod.Post, "api/materials/purchase-templates", req);
    public Task<(bool, string?)> DeletePurchaseTemplateAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/materials/purchase-templates/{id}");
    public Task<List<SupplierDto>?> GetSuppliersAsync() => GetAsync<List<SupplierDto>>("api/materials/suppliers");
    public Task<(bool, string?)> CreateSupplierAsync(SaveSupplierRequest req) =>
        SendAsync(HttpMethod.Post, "api/materials/suppliers", req);
    public Task<(bool, string?)> UpdateSupplierAsync(int id, SaveSupplierRequest req) =>
        SendAsync(HttpMethod.Put, $"api/materials/suppliers/{id}", req);
    public Task<(bool, string?)> DeleteSupplierAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/materials/suppliers/{id}");
    public Task<NotificationsDto?> GetNotificationsAsync() =>
        GetAsync<NotificationsDto>("api/restaurants/mine/notifications");
    public Task<StockAlertsDto?> GetStockAlertsAsync() => GetAsync<StockAlertsDto>("api/materials/alerts");
    public Task<(bool, string?)> MarkStockAlertsSeenAsync() => SendAsync(HttpMethod.Post, "api/materials/alerts/seen");

    // ---------- Store bills (owner's expense book) ----------
    public Task<StoreBillPageDto?> GetBillsAsync(string status, string category, string? search, int skip, int take) =>
        GetAsync<StoreBillPageDto>($"api/bills?status={status}&category={category}&search={Uri.EscapeDataString(search ?? "")}&skip={skip}&take={take}");
    public Task<(bool, string?)> CreateBillAsync(SaveStoreBillRequest req) => SendAsync(HttpMethod.Post, "api/bills", req);
    public Task<(bool, string?)> UpdateBillAsync(int id, SaveStoreBillRequest req) => SendAsync(HttpMethod.Put, $"api/bills/{id}", req);
    public Task<(bool, string?)> ToggleBillPaidAsync(int id) => SendAsync(HttpMethod.Post, $"api/bills/{id}/toggle-paid");
    public Task<(bool, string?)> DeleteBillAsync(int id) => SendAsync(HttpMethod.Delete, $"api/bills/{id}");
    public Task<BillReportDto?> GetBillReportAsync(DateTime? from = null, DateTime? to = null) =>
        GetAsync<BillReportDto>($"api/bills/report{(from is null || to is null ? "" : $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}")}");

    // ---------- Staff & salaries ----------
    public Task<StaffPageDto?> GetStaffAsync(string status, string? search) =>
        GetAsync<StaffPageDto>($"api/staff?status={status}&search={Uri.EscapeDataString(search ?? "")}");
    public Task<(bool, string?)> CreateStaffAsync(SaveStaffRequest req) => SendAsync(HttpMethod.Post, "api/staff", req);
    public Task<(bool, string?)> UpdateStaffAsync(int id, SaveStaffRequest req) => SendAsync(HttpMethod.Put, $"api/staff/{id}", req);
    public Task<(bool, string?)> DeleteStaffAsync(int id) => SendAsync(HttpMethod.Delete, $"api/staff/{id}");
    public Task<PayrollDto?> GetPayrollAsync(int year, int month) =>
        GetAsync<PayrollDto>($"api/staff/payroll?year={year}&month={month}");
    public Task<(bool, string?)> PaySalaryAsync(PaySalaryRequest req) => SendAsync(HttpMethod.Post, "api/staff/payroll/pay", req);
    public Task<(bool, string?)> UndoSalaryAsync(int paymentId) => SendAsync(HttpMethod.Delete, $"api/staff/payroll/{paymentId}");
    public Task<SalaryReportDto?> GetSalaryReportAsync(DateTime? from = null, DateTime? to = null) =>
        GetAsync<SalaryReportDto>($"api/staff/salary-report{(from is null || to is null ? "" : $"?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}")}");

    // ---------- Receipt design ----------
    public Task<ReceiptDesignDto?> GetMyReceiptDesignAsync() => GetAsync<ReceiptDesignDto>("api/restaurants/mine/receipt");
    public Task<(bool, string?)> SaveMyReceiptDesignAsync(ReceiptDesignDto design) => SendAsync(HttpMethod.Put, "api/restaurants/mine/receipt", design);

    // ---------- Anonymous visit analytics ----------
    public Task TrackStoreVisitAsync(int restaurantId, int? itemId = null, string? itemName = null) =>
        SendAsync(HttpMethod.Post, "api/track/store-visit", new TrackStoreVisitRequest(restaurantId, itemId, itemName));
    public Task<StoreVisitStatsDto?> GetMyVisitStatsAsync(string period) =>
        GetAsync<StoreVisitStatsDto>($"api/restaurants/mine/visits?period={period}");
    private static string RangeQuery(DateTime? from, DateTime? to) =>
        from is null || to is null ? "" : $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

    public Task<SalesSummaryDto?> GetMySalesReportAsync(string period, DateTime? from = null, DateTime? to = null) =>
        GetAsync<SalesSummaryDto>($"api/restaurants/mine/report/sales?period={period}{RangeQuery(from, to)}");
    public Task<ProductSalesPageDto?> GetMyProductReportAsync(string period, string? search, int skip, int take, DateTime? from = null, DateTime? to = null) =>
        GetAsync<ProductSalesPageDto>($"api/restaurants/mine/report/products?period={period}&search={Uri.EscapeDataString(search ?? "")}&skip={skip}&take={take}{RangeQuery(from, to)}");
    public Task<CustomerSalesPageDto?> GetMyCustomerReportAsync(string period, string? search, int skip, int take, DateTime? from = null, DateTime? to = null) =>
        GetAsync<CustomerSalesPageDto>($"api/restaurants/mine/report/customers?period={period}&search={Uri.EscapeDataString(search ?? "")}&skip={skip}&take={take}{RangeQuery(from, to)}");

    /// <summary>
    /// Sends a report grid to the server and gets a branded workbook back. Returns null
    /// if anything at all goes wrong — the caller shows a toast rather than a stack
    /// trace, because a failed download must never take the report page down with it.
    /// </summary>
    public async Task<byte[]?> ExportXlsxAsync(ExportSheetRequest request)
    {
        try
        {
            Auth();
            using var res = await http.PostAsJsonAsync("api/export/xlsx", request);
            if (res.StatusCode is HttpStatusCode.Unauthorized)
            {
                state.SignOut();
                return null;
            }
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    // ---------- Product photos (max 4 per dish, one main) ----------
    public Task<List<DishPhotoDto>?> GetDishPhotosAsync(int itemId) => GetAsync<List<DishPhotoDto>>($"api/restaurants/menu-items/{itemId}/photos");
    public Task<List<DishPhotoDto>?> GetMyDishPhotosAsync(int itemId) => GetAsync<List<DishPhotoDto>>($"api/menu/items/{itemId}/photos");

    // ---------- The shop's own Instagram ----------
    public Task<InstagramStatusDto?> GetInstagramAsync() => GetAsync<InstagramStatusDto>("api/instagram");
    public Task<(bool, string?)> ConnectInstagramAsync(ConnectInstagramRequest req) =>
        SendAsync(HttpMethod.Post, "api/instagram/connect", req);
    public Task<(bool, string?)> DisconnectInstagramAsync() => SendAsync(HttpMethod.Delete, "api/instagram", new { });
    public Task<List<InstagramMediaDto>?> GetInstagramPostsAsync() => GetAsync<List<InstagramMediaDto>>("api/instagram/posts");
    public Task<(InstagramPostResultDto? result, string? error)> PostToInstagramAsync(InstagramPostRequest req) =>
        PostForAsync<InstagramPostResultDto>("api/instagram/post", req);
    public Task<List<ScheduledPostDto>?> GetScheduledPostsAsync() => GetAsync<List<ScheduledPostDto>>("api/instagram/scheduled");
    public Task<(ScheduledPostDto? post, string? error)> SchedulePostAsync(SchedulePostRequest req) =>
        PostForAsync<ScheduledPostDto>("api/instagram/schedule", req);
    public Task<(bool, string?)> CancelScheduledPostAsync(string id) =>
        SendAsync(HttpMethod.Delete, $"api/instagram/scheduled/{id}", new { });

    // ---------- Driver profile ----------
    public Task<DriverStateDto?> GetDriverStateAsync() => GetAsync<DriverStateDto>("api/drivers/state");
    public Task UpdateDriverLocationAsync(double lat, double lng) =>
        SendAsync(HttpMethod.Post, "api/drivers/location", new UpdateLocationRequest(lat, lng));
    public Task<DriverLocationDto?> GetDriverLocationAsync(int orderId) =>
        GetAsync<DriverLocationDto>($"api/orders/{orderId}/driver-location");
    public Task<(bool, string?)> ToggleOnlineAsync() => SendAsync(HttpMethod.Post, "api/drivers/toggle-online");
    public Task<EarningsDto?> GetEarningsAsync(int days = 14) => GetAsync<EarningsDto>($"api/drivers/earnings?days={days}");

    // ---------- Driver document verification ----------
    public Task<DriverVerificationDto?> GetDriverVerificationAsync() =>
        GetAsync<DriverVerificationDto>("api/drivers/verification");
    public Task<(bool, string?)> SubmitDriverDocsAsync(SubmitDriverDocsRequest req) =>
        SendAsync(HttpMethod.Post, "api/drivers/verification", req);
    public Task<int> GetAdminDriverVerificationsCountAsync(DriverVerificationStatus status) =>
        GetAsync<int>($"api/admin/driver-verifications/count?status={status}");
    public Task<List<AdminDriverDocsDto>?> GetAdminDriverVerificationsAsync(DriverVerificationStatus status, int skip = 0, int take = 30) =>
        GetAsync<List<AdminDriverDocsDto>>($"api/admin/driver-verifications?status={status}&skip={skip}&take={take}");
    public Task<(bool, string?)> AdminApproveDriverDocsAsync(int userId) =>
        SendAsync(HttpMethod.Post, $"api/admin/driver-verifications/{userId}/approve");
    public Task<(bool, string?)> AdminRejectDriverDocsAsync(int userId, string reason) =>
        SendAsync(HttpMethod.Post, $"api/admin/driver-verifications/{userId}/reject", new RejectDriverDocsRequest(reason));

    // ---------- Reviews ----------
    public Task<(bool, string?)> CreateReviewAsync(CreateReviewRequest req) => SendAsync(HttpMethod.Post, "api/reviews", req);
    public Task<List<ReviewDto>?> GetMyRestaurantReviewsAsync() => GetAsync<List<ReviewDto>>("api/reviews/restaurant");

    // ---------- Store promotions: the partner's own discount codes ----------
    public Task<PromotionPageDto?> GetPromotionsAsync() => GetAsync<PromotionPageDto>("api/promotions");
    public Task<(PromotionDto? promo, string? error)> CreatePromotionAsync(SavePromotionRequest req) =>
        PostForAsync<PromotionDto>("api/promotions", req);
    public Task<(PromotionDto? promo, string? error)> UpdatePromotionAsync(string id, SavePromotionRequest req) =>
        SendForAsync<PromotionDto>(HttpMethod.Put, $"api/promotions/{id}", req);
    public Task<(PromotionDto? promo, string? error)> TogglePromotionAsync(string id) =>
        PostForAsync<PromotionDto>($"api/promotions/{id}/toggle", new { });
    public Task<(bool, string?)> DeletePromotionAsync(string id) => SendAsync(HttpMethod.Delete, $"api/promotions/{id}");
    /// <summary>The offers a store shows its guests.</summary>
    public Task<List<PublicPromotionDto>?> GetStorePromotionsAsync(int storeId) =>
        GetAsync<List<PublicPromotionDto>>($"api/promotions/public/{storeId}");
    /// <summary>Is this code one of the store's own, and does it apply to this cart?</summary>
    public Task<(PromotionCheckDto? result, string? error)> CheckPromotionAsync(CheckPromotionRequest req) =>
        PostForAsync<PromotionCheckDto>("api/promotions/check", req);

    // ---------- Coupons ----------
    public Task<(ValidateCouponResponse? result, string? error)> ValidateCouponAsync(string code, decimal subtotal) =>
        PostForAsync<ValidateCouponResponse>("api/coupons/validate", new ValidateCouponRequest(code, subtotal));
    public Task<List<CouponDto>?> GetCouponsAsync() => GetAsync<List<CouponDto>>("api/coupons");
    public Task<(bool, string?)> CreateCouponAsync(SaveCouponRequest req) => SendAsync(HttpMethod.Post, "api/coupons", req);
    public Task<(bool, string?)> UpdateCouponAsync(int id, SaveCouponRequest req) => SendAsync(HttpMethod.Put, $"api/coupons/{id}", req);
    public Task<(bool, string?)> DeleteCouponAsync(int id) => SendAsync(HttpMethod.Delete, $"api/coupons/{id}");

    // ---------- Order chat ----------
    public Task<List<ChatMessageDto>?> GetChatAsync(int orderId, int afterId = 0) =>
        GetAsync<List<ChatMessageDto>>($"api/chat/{orderId}?afterId={afterId}");
    public Task<(bool, string?)> SendChatAsync(int orderId, SendChatRequest req) =>
        SendAsync(HttpMethod.Post, $"api/chat/{orderId}", req);

    // ---------- Tracking ----------
    /// <summary>Best-effort: a failed log must never disturb a search.</summary>
    public Task TrackSearchAsync(string term, int results, string? locale = null) =>
        SendAsync(HttpMethod.Post, "api/track/search", new TrackSearchRequest(term, results, "Customer", locale));

    public Task<InsightsDto?> GetInsightsAsync(int days = 7, string app = "Customer") =>
        GetAsync<InsightsDto>($"api/admin/insights?days={days}&app={Uri.EscapeDataString(app)}");

    /// <summary>
    /// Report a page view. Pass <paramref name="ip"/> when the app knows the visitor's
    /// address — the API cannot work it out for itself across a Blazor Server circuit.
    /// </summary>
    public Task TrackVisitAsync(string app, string page, string? ip = null) =>
        SendAsync(HttpMethod.Post, "api/track", new TrackRequest(app, page, ip));

    // ---------- Admin ----------
    public Task<List<ActivityDto>?> GetActivityAsync(int skip = 0, int take = 40,
        DateTime? from = null, DateTime? to = null, UserRole? role = null, string? app = null, string? search = null) =>
        GetAsync<List<ActivityDto>>($"api/admin/activity?skip={skip}&take={take}" +
            (from is null ? "" : $"&from={from:yyyy-MM-dd}") +
            (to is null ? "" : $"&to={to:yyyy-MM-dd}") +
            (role is null ? "" : $"&role={role}") +
            (string.IsNullOrWhiteSpace(app) ? "" : $"&app={Uri.EscapeDataString(app)}") +
            (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}"));

    /// <summary>The visitor board: one row per address that has been on the site.</summary>
    public Task<VisitorsDto?> GetVisitorsAsync(int days = 7, string app = "Customer",
        string? search = null, int skip = 0, int take = 50) =>
        GetAsync<VisitorsDto>($"api/admin/visitors?days={days}&app={Uri.EscapeDataString(app)}&skip={skip}&take={take}" +
            (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}"));

    /// <summary>Every page one address opened — the drill-down under a visitor row.</summary>
    public Task<List<VisitorPageDto>?> GetVisitorPagesAsync(string ip, int days = 7, string app = "Customer") =>
        GetAsync<List<VisitorPageDto>>(
            $"api/admin/visitors/pages?ip={Uri.EscapeDataString(ip)}&days={days}&app={Uri.EscapeDataString(app)}");

    public Task<List<AdminChatSummaryDto>?> GetAdminChatsAsync(int skip = 0, int take = 30, string? search = null) =>
        GetAsync<List<AdminChatSummaryDto>>($"api/admin/chats?skip={skip}&take={take}" +
            (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}"));

    public Task<List<ChatMessageDto>?> GetAdminChatMessagesAsync(int orderId) =>
        GetAsync<List<ChatMessageDto>>($"api/admin/chats/{orderId}");

    public Task<(bool, string?)> AdminAdvanceOrderAsync(int id) =>
        SendAsync(HttpMethod.Post, $"api/admin/orders/{id}/advance");

    public Task<(bool, string?)> AdminToggleRestaurantOpenAsync(int id) =>
        SendAsync(HttpMethod.Post, $"api/admin/restaurants/{id}/toggle-open");
    public Task<List<UserPresenceDto>?> GetPresenceAsync() => GetAsync<List<UserPresenceDto>>("api/admin/activity/presence");
    public Task<AdminReportDto?> GetReportAsync(DateTime from, DateTime to) =>
        GetAsync<AdminReportDto>($"api/admin/reports?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
    public Task<AdminDashboardDto?> GetAdminDashboardAsync() => GetAsync<AdminDashboardDto>("api/admin/dashboard");
    public Task<List<UserDto>?> GetUsersAsync(UserRole? role = null, string? search = null, int skip = 0, int take = 50) =>
        GetAsync<List<UserDto>>($"api/admin/users?skip={skip}&take={take}" +
            (role is null ? "" : $"&role={role}") +
            (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}"));

    public Task<int?> GetUsersCountAsync(UserRole? role = null, string? search = null) =>
        GetAsync<int?>("api/admin/users/count?" +
            (role is null ? "" : $"&role={role}") +
            (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}"));
    public Task<(bool, string?)> CreateUserAsync(CreateUserRequest req) => SendAsync(HttpMethod.Post, "api/admin/users", req);
    public Task<(bool, string?)> ToggleUserActiveAsync(int id) => SendAsync(HttpMethod.Post, $"api/admin/users/{id}/toggle-active");
    public Task<(bool, string?)> ResetUserPasswordAsync(int id, string newPassword) =>
        SendAsync(HttpMethod.Post, $"api/admin/users/{id}/reset-password", new ResetPasswordRequest(newPassword));
    /// <summary>An admin rewrites a user's name, sign-in email and phone.</summary>
    public Task<(bool ok, string? error)> AdminUpdateUserAsync(int id, AdminUpdateUserRequest req) =>
        SendAsync(HttpMethod.Put, $"api/admin/users/{id}", req);
    /// <summary>The caller's OWN sign-in email / password — the owner card on the team page.</summary>
    public Task<(bool ok, string? error)> UpdateMyCredentialsAsync(string? email, string? newPassword) =>
        SendAsync(HttpMethod.Put, "api/auth/credentials", new UpdateCredentialsRequest(email, newPassword));
    public Task<(bool, string?)> CancelOrderAsAdminAsync(int id, string? reason) =>
        SendAsync(HttpMethod.Post, $"api/admin/orders/{id}/cancel", new CancelOrderRequest(reason));
    public Task<(bool, string?)> DeleteRestaurantAsAdminAsync(int id) =>
        SendAsync(HttpMethod.Delete, $"api/admin/restaurants/{id}");
    public Task<AdminStoreFootprintDto?> GetStoreFootprintAsync(int id) =>
        GetAsync<AdminStoreFootprintDto>($"api/admin/restaurants/{id}/footprint");
    /// <summary>The admin store list's full filter set, shared by page and count calls.</summary>
    private static string AdminStoreFilterQuery(string? search, bool? approved, StoreType? storeType,
        int? cuisineId, bool? open, string? sort = null) =>
        (approved is null ? "" : $"&approved={approved}") +
        (storeType is null ? "" : $"&storeType={storeType}") +
        (cuisineId is null ? "" : $"&cuisineId={cuisineId}") +
        (open is null ? "" : $"&open={open}") +
        (string.IsNullOrWhiteSpace(sort) ? "" : $"&sort={sort}") +
        (string.IsNullOrWhiteSpace(search) ? "" : $"&search={Uri.EscapeDataString(search)}");

    public Task<List<AdminRestaurantDto>?> GetAdminRestaurantsAsync(string? search = null, bool? approved = null,
        int skip = 0, int take = 50, StoreType? storeType = null, int? cuisineId = null,
        bool? open = null, string? sort = null) =>
        GetAsync<List<AdminRestaurantDto>>($"api/admin/restaurants?skip={skip}&take={take}" +
            AdminStoreFilterQuery(search, approved, storeType, cuisineId, open, sort));

    public Task<int?> GetAdminRestaurantsCountAsync(string? search = null, bool? approved = null,
        StoreType? storeType = null, int? cuisineId = null, bool? open = null) =>
        GetAsync<int?>("api/admin/restaurants/count?" +
            AdminStoreFilterQuery(search, approved, storeType, cuisineId, open));
    public Task<List<MenuCategoryDto>?> GetAdminMenuAsync(int restaurantId) => GetAsync<List<MenuCategoryDto>>($"api/admin/restaurants/{restaurantId}/menu");
    public Task<(bool, string?)> AdminCreateCategoryAsync(int restaurantId, SaveCategoryRequest req) => SendAsync(HttpMethod.Post, $"api/admin/restaurants/{restaurantId}/menu/categories", req);
    public Task<(bool, string?)> AdminUpdateCategoryAsync(int id, SaveCategoryRequest req) => SendAsync(HttpMethod.Put, $"api/admin/menu/categories/{id}", req);
    public Task<(bool, string?)> AdminDeleteCategoryAsync(int id) => SendAsync(HttpMethod.Delete, $"api/admin/menu/categories/{id}");
    public Task<(bool, string?)> AdminCreateMenuItemAsync(int restaurantId, SaveMenuItemRequest req) => SendAsync(HttpMethod.Post, $"api/admin/restaurants/{restaurantId}/menu/items", req);
    public Task<(bool, string?)> AdminUpdateMenuItemAsync(int id, SaveMenuItemRequest req) => SendAsync(HttpMethod.Put, $"api/admin/menu/items/{id}", req);
    public Task<(bool, string?)> AdminToggleMenuItemAsync(int id) => SendAsync(HttpMethod.Post, $"api/admin/menu/items/{id}/toggle-available");
    public Task<(bool, string?)> AdminDeleteMenuItemAsync(int id) => SendAsync(HttpMethod.Delete, $"api/admin/menu/items/{id}");
    public Task<(bool, string?)> ToggleRestaurantApprovedAsync(int id) => SendAsync(HttpMethod.Post, $"api/admin/restaurants/{id}/approve");
    public Task<(bool, string?)> ToggleSuggestedAsync(int id) => SendAsync(HttpMethod.Post, $"api/admin/restaurants/{id}/suggest");
    public Task<List<AdminSuggestedDto>?> GetAdminSuggestedAsync() => GetAsync<List<AdminSuggestedDto>>("api/admin/suggested");
    public Task<(bool, string?)> SetSuggestedOrderAsync(List<int> ids) => SendAsync(HttpMethod.Put, "api/admin/suggested", ids);
    public Task<(bool, string?)> SetCommissionAsync(int id, decimal percent) =>
        SendAsync(HttpMethod.Post, $"api/admin/restaurants/{id}/commission", new SetCommissionRequest(percent));
    public Task<int?> GetAdminOrdersCountAsync(OrderStatus? status = null, string? search = null)
    {
        var query = new List<string>();
        if (status is not null) query.Add($"status={status}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        var suffix = query.Count > 0 ? "?" + string.Join("&", query) : "";
        return GetAsync<int?>($"api/admin/orders/count{suffix}");
    }

    public Task<List<OrderDto>?> GetAdminOrdersAsync(OrderStatus? status = null, string? search = null, int skip = 0, int take = 50)
    {
        var query = new List<string> { $"skip={skip}", $"take={take}" };
        if (status is not null) query.Add($"status={status}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        return GetAsync<List<OrderDto>>($"api/admin/orders?{string.Join("&", query)}");
    }

    // ---------- Saved cards (test payments) ----------
    public Task<List<CardDto>?> GetCardsAsync() => GetAsync<List<CardDto>>("api/cards");
    public Task<(bool, string?)> AddCardAsync(SaveCardRequest req) => SendAsync(HttpMethod.Post, "api/cards", req);
    public Task<(bool, string?)> DeleteCardAsync(int id) => SendAsync(HttpMethod.Delete, $"api/cards/{id}");
}
