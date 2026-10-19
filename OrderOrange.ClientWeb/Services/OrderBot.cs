using Microsoft.JSInterop;
using OrderOrange.ClientCore.Services;
using OrderOrange.Shared;

namespace OrderOrange.ClientWeb.Services;

/// <summary>
/// The offline ordering assistant: a deterministic dialog engine on top of
/// <see cref="BotNlu"/>. It understands free text in all 14 app languages
/// (misspellings included), drives the real cart and API, and completes an
/// order end-to-end: search → pick → quantity → address → payment → confirm →
/// placed, plus live order tracking and cancellation. No network AI — the
/// only I/O is this app's own API server.
/// </summary>
public sealed class OrderBot(ApiClient api, CartState cart, AppState state, LanguageService lang, Microsoft.JSInterop.IJSRuntime js)
{
    /// <summary>
    /// The visitor's address, handed in by the widget while an HttpContext still exists —
    /// every API call from this app leaves the web server, so the API cannot see it itself.
    /// </summary>
    public string? VisitorIp { get; set; }

    /// <summary>The page the assistant was opened from, for the report to show.</summary>
    public string? Page { get; set; }

    /// <summary>
    /// The store whose page the customer is on. Majed 2026-10-01: that store is the MAIN
    /// one for the whole conversation — searches, recommendations and the model's answers
    /// stay inside it, and the platform is only searched when the customer asks for it.
    /// Null on the home page and everywhere else.
    /// </summary>
    public int? CurrentStoreId
    {
        get => _currentStoreId;
        set { if (_currentStoreId != value) { _currentStoreId = value; _currentStore = null; } }
    }
    private int? _currentStoreId;
    private RestaurantCardDto? _currentStore;

    /// <summary>The store id in a customer-site path: /p/5000216, /restaurant/…, /store/…, /reserve-table/…, /reserve/5000216-….</summary>
    public static int? StoreIdFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(path, @"^/(?:p|restaurant|store|reserve-table)/(\d+)(?:[/?#]|$)|^/reserve/(\d+)-", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var g = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        return int.TryParse(g, out var id) && id > 0 ? id : null;
    }

    /// <summary>
    /// The store this conversation is about: the page's store, or — away from a store page —
    /// the store the basket already holds (Majed 2026-10-01: "do not show other restaurants
    /// if there is [something] in basket"). Null on the home page with an empty basket.
    /// </summary>
    private int? EffectiveStoreId =>
        CurrentStoreId ?? (cart.Lines.Count > 0 && !cart.IsMultiStore ? cart.Restaurant?.Id : null);

    // ───────────────────────────── Where is the customer? ─────────────────────────────
    // Majed 2026-10-01: "ask for location first". Away from a store page with an empty
    // basket, a search over the whole platform first finds out where the customer is —
    // the browser's position, or an area they type — and the answer is built from the
    // stores around them, not a random spread of the whole country.

    private double? _lat, _lng;
    private List<RestaurantCardDto>? _nearStores;      // nearest first, once known
    private bool _locationAsked;                        // asked once per conversation
    private string? _pendingQuery;                      // the search that waits for the location
    private bool _areaWait;                             // the next short line may be an area name

    private sealed record GeoPos(double? Lat, double? Lng, bool Denied = false, bool Unsupported = false, bool Timeout = false);
    private bool _locationDenied;

    /// <summary>
    /// The customer's GPS position through mfGeo (Majed: "get user GPS location"): a request
    /// the chip's own tap may already have started, so the prompt appears on phones. The
    /// page's silently-known position is tried first with a short leash — it must never
    /// stall the chat. False = refused, unsupported or timed out; the area can be typed.
    /// </summary>
    private async Task<bool> TryLocateAsync()
    {
        if (_lat is not null) return true;
        try
        {
            var known = js.InvokeAsync<GeoPos?>("mfMap.position").AsTask();
            if (await Task.WhenAny(known, Task.Delay(2500)) == known && known.Result is { Lat: not null and not 0 } k)
            { _lat = k.Lat; _lng = k.Lng; return true; }
        }
        catch { /* the page may not carry that helper */ }
        try
        {
            var pos = await js.InvokeAsync<GeoPos?>("mfGeo.request", 15000);
            if (pos is { Lat: not null and not 0 } p) { _lat = p.Lat; _lng = p.Lng; _locationDenied = false; return true; }
            _locationDenied = pos?.Denied == true;
        }
        catch { /* refused or unsupported */ }
        return false;
    }

    /// <summary>The stores around the known position, nearest first (also the store filter for searches).</summary>
    private async Task<List<RestaurantCardDto>> NearStoresAsync()
    {
        if (_nearStores is not null) return _nearStores;
        if (_lat is null) return [];
        var page = await api.BrowsePageAsync(null, null, "recommended", false, 0, 40, null, _lat, _lng) ?? [];
        _nearStores = page.Where(r => r.Id >= 5_000_200).OrderBy(r => r.DistanceKm ?? double.MaxValue).ToList();
        return _nearStores;
    }

    /// <summary>
    /// "Nearby restaurants", or the first platform-wide search: locate, then show the nearest
    /// stores (or run the waiting search among them). Without a position, ask for the area.
    /// </summary>
    private async Task NearFlowAsync(string? header = null)
    {
        if (!await TryLocateAsync())
        {
            _locationAsked = true;
            _areaWait = true;
            // Blocked in the browser: say how to unblock it, otherwise offer the tap that asks.
            Bot(TT(_locationDenied ? "bot.locationDenied" : "bot.askArea"),
                Chip(TT("bot.chipUseLocation"), "act:near"), Chip(TT("bot.chipHelp"), "act:help"));
            return;
        }
        var near = await NearStoresAsync();
        if (_pendingQuery is { } waiting)
        {
            _pendingQuery = null;
            await SearchFlowAsync(waiting, allStores: true, header: header);
            return;
        }
        if (near.Count == 0) { Bot(TT("bot.nearNone"), Chip(TT("bot.chipOrder"), "act:order")); return; }
        ShowStoreCards(near.Take(8).ToList(), header ?? TT("bot.nearHere"));
    }

    /// <summary>The customer typed where they are: stores whose area matches become the neighbourhood.</summary>
    private async Task<bool> TryAreaAnswerAsync(string text)
    {
        if (!_areaWait) return false;
        var tokens = BotNlu.Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length > 5 || LooksLikeQuestion(text)) return false;
        var found = (await api.BrowsePageAsync(null, null, "recommended", false, 0, 40, text) ?? [])
            .Where(r => r.Id >= 5_000_200).ToList();
        if (found.Count == 0) return false;                 // not an area after all — the normal path takes it
        _areaWait = false;
        _nearStores = found;
        if (_pendingQuery is { } waiting)
        {
            _pendingQuery = null;
            await SearchFlowAsync(waiting, allStores: true);
            return true;
        }
        ShowStoreCards(found.Take(8).ToList(), TT("bot.nearHere"));
        return true;
    }

    /// <summary>
    /// "List the restaurants" / "other places" / "pizza places in Seeb": the platform's real
    /// stores as tappable cards — nearest first when the position is known, filtered by the
    /// model's word when it gave one. Works on a store page too: the customer asked.
    /// </summary>
    private async Task<bool> ShowStoresAsync(string? filter, string? header)
    {
        var search = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
        var page = await api.BrowsePageAsync(null, null, "recommended", false, 0, 40, search, _lat, _lng) ?? [];
        var stores = page.Where(r => r.Id >= 5_000_200).ToList();
        if (stores.Count == 0 && search is not null)
        {
            page = await api.BrowsePageAsync(null, null, "recommended", false, 0, 40, null, _lat, _lng) ?? [];
            stores = page.Where(r => r.Id >= 5_000_200).ToList();
        }
        if (stores.Count == 0) return false;
        if (_lat is not null) stores = stores.OrderBy(r => r.DistanceKm ?? double.MaxValue).ToList();
        ShowStoreCards(stores.Take(12).ToList(), header ?? TT("bot.results"));
        return true;
    }

    private void ShowStoreCards(List<RestaurantCardDto> stores, string header)
    {
        _options = [];
        var cards = new List<BotOptionView>();
        foreach (var r in stores)
        {
            _options.Add(new Option(OptKind.Restaurant, SN(r.Name), $"{r.Name} {SN(r.Name)} {r.Cuisine}", r));
            var km = r.DistanceKm is { } d ? $" · 📍 {d:0.0} km" : "";
            cards.Add(new BotOptionView(_options.Count, r.LogoEmoji, FoodPhoto.ForRestaurant(r.Cuisine, r.Id),
                SN(r.Name), $"{CN(r.Cuisine)} · ⭐{r.Rating:0.0}{km}", "")
            {
                Hue = HueOf(r.Name),
                Closed = !r.IsOpen,
            });
        }
        _step = Step.PickResult;
        _messages.Add(new BotMessage { FromBot = true, Text = header, OptionCards = cards });
    }

    private async Task<RestaurantCardDto?> CurrentStoreAsync()
    {
        if (EffectiveStoreId is not { } id) return null;
        if (_currentStore?.Id == id) return _currentStore;
        if (CurrentStoreId is null && cart.Restaurant is { } fromBasket && fromBasket.Id == id) { _currentStore = fromBasket; return _currentStore; }
        try { _currentStore = (await api.GetRestaurantAsync(id))?.Info; } catch { _currentStore = null; }
        return _currentStore;
    }

    /// <summary>One id for this sitting, so the report can show conversations, not a heap of lines.</summary>
    private readonly string _session = Guid.NewGuid().ToString("N");

    private const decimal ServiceFee = 0.300m;   // mirrors Checkout.razor

    // ───────────────────────── Conversation model ─────────────────────────

    public sealed record BotChip(string Label, string Payload);

    /// <summary>A tappable product/store card shown under a bot message.</summary>
    public sealed record BotOptionView(
        int Number, string Emoji, string? Photo, string Title, string? Subtitle, string Price)
    {
        /// <summary>Restaurant this card belongs to — drawn as a colour-coded pill.</summary>
        public string? Venue { get; init; }
        public string? VenueEmoji { get; init; }
        /// <summary>Venue colour from <see cref="HueOf"/>; 24 is the brand orange.</summary>
        public int Hue { get; init; } = 24;
        public bool Closed { get; init; }
        /// <summary>Order status: localized label plus a tone class the widget colours.</summary>
        public string? BadgeText { get; init; }
        public string? BadgeTone { get; init; }
    }

    /// <summary>
    /// Hues that all stay legible as dark text on their own light tint. Kept wide (18)
    /// because two restaurants landing on the same colour in one result list is exactly
    /// the confusion this is meant to remove.
    /// </summary>
    private static readonly int[] VenueHues =
        [4, 20, 38, 55, 78, 100, 130, 152, 172, 190, 205, 221, 240, 258, 280, 300, 322, 340];

    /// <summary>
    /// One restaurant keeps one colour everywhere it appears — same name in, same hue out —
    /// so a customer can tell two restaurants apart at a glance in a mixed result list.
    /// </summary>
    public static int HueOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return VenueHues[0];
        var h = 2166136261u;                                  // FNV-1a: stable across runs and cultures
        foreach (var ch in name) h = (h ^ char.ToLowerInvariant(ch)) * 16777619u;
        return VenueHues[h % (uint)VenueHues.Length];
    }

    private static string StatusTone(OrderStatus s) => s switch
    {
        OrderStatus.Pending => "wait",
        OrderStatus.Accepted => "ok",
        OrderStatus.Preparing => "cook",
        OrderStatus.Ready => "ready",
        OrderStatus.PickedUp or OrderStatus.OnTheWay => "way",
        OrderStatus.Delivered => "done",
        _ => "off",
    };

    /// <summary>One priced line of the confirmation card.</summary>
    public sealed record BotBillLine(string Text, string Amount, string Tone = "");

    /// <summary>
    /// The final "please confirm" receipt, as structured data instead of a wall of text —
    /// the widget draws it as a real bill so the total and the address are readable at a glance.
    /// </summary>
    public sealed record BotBill(
        string VenueEmoji, string Venue, int Hue,
        List<BotBillLine> Items, List<BotBillLine> Charges,
        string TotalLabel, string Total,
        string AddressEmoji, string AddressLabel, string Address,
        string PaymentEmoji, string PaymentLabel, string Payment,
        string Footer);

    public sealed class BotMessage
    {
        public required bool FromBot { get; init; }
        public required string Text { get; init; }
        public List<BotChip> Chips { get; init; } = [];
        public List<BotOptionView> OptionCards { get; init; } = [];
        /// <summary>Colour-coded order strip drawn above the text (venue + status).</summary>
        public BotOptionView? Banner { get; init; }
        /// <summary>Render the live modern basket card in place of the text.</summary>
        public bool CartCard { get; init; }
        /// <summary>Render the confirmation bill card under the text.</summary>
        public BotBill? Bill { get; init; }
        /// <summary>Render a −/+ quantity stepper under this message.</summary>
        public bool QtyPicker { get; init; }
        public DateTime At { get; } = DateTime.Now;
    }

    private readonly List<BotMessage> _messages = [];
    public IReadOnlyList<BotMessage> Messages => _messages;
    public bool Busy { get; private set; }
    public event Action? Changed;

    /// <summary>The customer tapped an "Open" chip: the widget (which owns navigation) takes them there.</summary>
    public event Action<string>? NavigateRequested;

    // ───────────────────────────── Dialog state ─────────────────────────────

    private enum Step
    {
        Idle, AwaitQuery, PickResult, PickQuantity, ConfirmSwitch,
        PickAddress, PickPayment, PickCard, ConfirmOrder,
        PickTrackOrder, PickCancelOrder, ConfirmCancel, ConfirmClear
    }

    private enum OptKind { Dish, Restaurant, Address, Payment, Card, Order }

    private sealed record Option(OptKind Kind, string Label, string Match, object Payload);

    private Step _step = Step.Idle;
    private List<Option> _options = [];
    private string? _replyLocale;                       // language the user last typed in

    // order-building context
    private RestaurantCardDto? _pendingRestaurant;
    private MenuItemDto? _pendingItem;
    private int? _pendingQty;
    private OrderDto? _pendingReorder;               // reorder awaiting basket-switch consent
    private int _addressId;
    private string _addressLabel = "";
    private PaymentMethod _payment = PaymentMethod.CashOnDelivery;
    private int? _cardId;
    private string? _couponCode;
    private decimal _discount;
    private int _cancelOrderId;
    private string _cancelOrderNumber = "";

    // ───────────────────────────── Public API ─────────────────────────────

    /// <summary>
    /// Wipes the whole conversation and context. Called on sign-out so the next
    /// user on this circuit never sees the previous user's chat or addresses.
    /// </summary>
    public void Reset()
    {
        _messages.Clear();
        _options = [];
        _step = Step.Idle;
        _replyLocale = null;
        _queue.Clear();
        _dishQueue.Clear();
        _budgetCombos = [];
        _pendingRestaurant = null;
        _pendingItem = null;
        _pendingQty = null;
        _pendingReorder = null;
        _addressId = 0;
        _addressLabel = "";
        _payment = PaymentMethod.CashOnDelivery;
        _cardId = null;
        _couponCode = null;
        _discount = 0;
        Changed?.Invoke();
    }

    /// <summary>First-open greeting (only once per circuit).</summary>
    public void Start()
    {
        if (_messages.Count > 0) return;
        var name = string.IsNullOrWhiteSpace(state.FullName) ? "" : state.FullName.Split(' ')[0];
        Bot(TT("bot.greeting", name),
            Chip(TT("bot.chipOrder"), "act:order"),
            Chip(TT("bot.chipTrack"), "act:track"),
            Chip(TT("bot.chipHelp"), "act:help"));
    }

    /// <summary>
    /// The greeting, naming the store when the chat opens on a store's page — the first
    /// sign that this conversation is about THIS store.
    /// </summary>
    public async Task StartAsync()
    {
        if (_messages.Count > 0) return;
        var store = await CurrentStoreAsync();
        if (_messages.Count > 0) return;                 // a tap raced the fetch
        var name = string.IsNullOrWhiteSpace(state.FullName) ? "" : state.FullName.Split(' ')[0];
        var text = TT("bot.greeting", name);
        if (store is not null) text += "\n" + TT("bot.greetStore", SN(store.Name));
        Bot(text,
            Chip(TT("bot.chipOrder"), "act:order"),
            Chip(TT("bot.chipTrack"), "act:track"),
            Chip(TT("bot.chipHelp"), "act:help"));
        Changed?.Invoke();
    }

    /// <summary>
    /// Feed one user utterance (or a chip tap: display label + machine payload)
    /// through the dialog engine.
    /// </summary>
    public async Task SendAsync(string display, string? payload = null)
    {
        if (Busy || string.IsNullOrWhiteSpace(display)) return;
        var mark = _messages.Count;          // everything added past here is the answer
        _messages.Add(new BotMessage { FromBot = false, Text = display.Trim() });
        Busy = true;
        Changed?.Invoke();
        try
        {
            if (payload is not null) await HandleCommandAsync(payload);
            else await HandleTextAsync(display.Trim());
        }
        catch
        {
            Bot(TT("common.wentWrong"));
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
            LogTurn(display.Trim(), mark);
        }
    }

    /// <summary>
    /// Files this exchange for the team to read later. Fire-and-forget on purpose: the
    /// person is waiting for their answer, not for our bookkeeping, and a log that fails
    /// must not surface as a broken assistant.
    /// </summary>
    private void LogTurn(string text, int mark)
    {
        var reply = string.Join(" ", _messages.Skip(mark + 1)
            .Where(m => m.FromBot && !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => m.Text));
        _ = Task.Run(async () =>
        {
            try
            {
                await api.LogBotChatAsync(new BotChatLogRequest(
                    _session, "customer", text, reply, Page, lang.Locale,
                    _pendingRestaurant?.Id, VisitorIp));
            }
            catch { /* the conversation matters; the record of it does not */ }
        });
    }

    // ───────────────────────── Chip command routing ─────────────────────────

    private async Task HandleCommandAsync(string payload)
    {
        var parts = payload.Split(':', 2);
        switch (parts[0])
        {
            case "sys" when parts[1] == "yes": await HandleYesNoAsync(true); break;
            case "sys" when parts[1] == "no": await HandleYesNoAsync(false); break;
            case "opt" when int.TryParse(parts[1], out var i): await SelectOptionAsync(i - 1); break;
            case "qty" when int.TryParse(parts[1], out var q): await HandleQuantityAsync(q); break;
            case "cancel" when int.TryParse(parts[1], out var id): await StartCancelAsync(id); break;
            case "rm" when int.TryParse(parts[1], out var itemId): RemoveLine(itemId, null); break;
            case "buy" when int.TryParse(parts[1], out var combo): await BuyComboAsync(combo); break;
            case "find": await SearchFlowAsync(parts[1]); break;
            case "findall": await SearchFlowAsync(parts[1], allStores: true); break;   // the customer chose to leave the store
            case "go" when parts.Length == 2 && (parts[1].StartsWith('/') || parts[1].StartsWith("https://")):
                NavigateRequested?.Invoke(parts[1]);
                break;
            case "act":
                switch (parts[1])
                {
                    case "order": AskWhatToEat(); break;
                    case "more": AskWhatToEat(); break;
                    case "track": await TrackFlowAsync(null); break;
                    case "cancel": await CancelFlowAsync(); break;
                    case "cart": ShowCart(); break;
                    case "clear": ConfirmClearCart(); break;
                    case "checkout": await CheckoutFlowAsync(); break;
                    case "reorder": await ReorderFlowAsync(); break;
                    case "help": ShowHelp(); break;
                    case "near": await NearFlowAsync(); break;
                }
                break;
        }
    }

    // ───────────────────────────── Text pipeline ─────────────────────────────

    private async Task HandleTextAsync(string text)
    {
        // Reply in the language the user writes in (sticky across turns). The app's own
        // language breaks Perso-Arabic ties, so a Persian customer writing words that are
        // spelled the same in Arabic still gets Persian back.
        // A bare count («3تا», "x2") carries no language of its own — Persian and Arabic share
        // the letters — so it must not flip the conversation's language.
        if (ParseBareQuantity(text) is null && BotNlu.DetectLocale(text, lang.Locale) is { } detected) _replyLocale = detected;

        // "switch to Arabic" changes the whole app, not just the reply. It runs ahead of
        // the dialog steps because a language name is a short word, and a step waiting for
        // a dish or an address would otherwise swallow it as an answer.
        if (LanguagePick.Resolve(text) is { } wanted) { await SwitchLanguageAsync(wanted); return; }

        // "Al Khuwair" typed after we asked where they are.
        if (await TryAreaAnswerAsync(text)) return;

        // Structured answers stay with the engine: a bare number or yes/no for a quantity
        // or an option, and the address / payment / card / confirmation steps, which are
        // menus of their own. Everything else — Majed 2026-10-01: "make all text connected
        // to grok, we just do filter and search" — goes to the language model first, which
        // answers or tells the engine what to do (search, pick, quantity, track…).
        if (IsStructuredStepInput(text) && await TryHandleStepAsync(text)) return;
        if (await TryModelTurnAsync(text)) return;

        // The model is unavailable: the old rule pipeline, exactly as before.
        if (TryFaq(text)) return;
        if (await TryHandleStepAsync(text)) return;
        await HandleGlobalAsync(text);
    }

    /// <summary>
    /// A number with a counter word glued to it, in any digit script — Persian «۳تا», «3 عدد»,
    /// Arabic «٣ حبات», English "3 pcs", "x3". Anything longer than a few letters around the
    /// digits is a sentence, not a count (Majed's screenshot 2026-10-01: «3تا» became a search).
    /// </summary>
    private static int? ParseBareQuantity(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 14) return null;
        var ascii = new string(text.Trim().Select(ch =>
            ch is >= '۰' and <= '۹' ? (char)('0' + (ch - '۰')) :
            ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch).ToArray());
        var m = System.Text.RegularExpressions.Regex.Match(ascii, @"^[^\d]{0,3}(\d{1,3})[^\d]{0,8}$");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n is >= 1 and <= 99 ? n : null;
    }

    /// <summary>Inputs the engine's own steps read better than any model: numbers, yes/no, and the checkout menus.</summary>
    private bool IsStructuredStepInput(string text)
    {
        switch (_step)
        {
            case Step.PickAddress or Step.PickPayment or Step.PickCard
                or Step.ConfirmSwitch or Step.ConfirmOrder or Step.ConfirmCancel or Step.ConfirmClear
                or Step.PickTrackOrder or Step.PickCancelOrder:
                return true;
            case Step.PickQuantity:
                return BotNlu.DetectYesNo(text) is not null || BotNlu.ExtractQuantity(text, bareAnswer: true).Item1 is not null
                       || ParseBareQuantity(text) is not null;
            case Step.PickResult:
                // An option number — or, with a single card on screen, a bare count («3تا»).
                return (int.TryParse(text.Trim().TrimEnd('.', ')'), out var n) && n >= 1 && n <= _options.Count)
                       || (_options.Count == 1 && ParseBareQuantity(text) is not null);
            default:
                return false;
        }
    }

    private DateTime _modelDownUntil = DateTime.MinValue;

    private static readonly HashSet<string> SupportedLocales = new(StringComparer.Ordinal)
        { "en", "ar", "fa", "ur", "hi", "tr", "de", "fr", "es", "it", "pt", "ru", "ja", "zh" };

    /// <summary>
    /// One turn through the language model in agent mode: it reads the whole situation
    /// (store menu, list on screen, basket, step) and either answers or names an action
    /// for the engine. False = the model could not be reached and the rules take over.
    /// </summary>
    private async Task<bool> TryModelTurnAsync(string text)
    {
        if (DateTime.Now < _modelDownUntil) return false;
        var earlier = _messages.Take(_messages.Count - 1)
            .Where(m => !string.IsNullOrWhiteSpace(m.Text) || m.OptionCards.Count > 0)
            .TakeLast(9)
            .Select(m => new AssistantTurnDto(m.FromBot ? "assistant" : "user", Truncate(HistoryText(m), 900)))
            .ToList();
        var options = _step is Step.PickResult && _options.Count > 0
            ? _options.Select(o => o.Label).ToList()
            : null;
        var storeId = EffectiveStoreId ?? _pendingRestaurant?.Id;

        AssistantReplyDto? answer;
        try
        {
            answer = await api.AskAssistantAsync(new AssistantAskRequest(
                _session, text, _replyLocale ?? lang.Locale, Page, storeId, earlier, VisitorIp,
                Agent: true, Step: StepName(), Cart: CartSummary(), Options: options));
        }
        catch { answer = null; }
        if (answer is null)
        {
            _modelDownUntil = DateTime.Now.AddSeconds(30);   // spare the next lines a wasted round trip
            return false;
        }

        var reply = (answer.Reply ?? "").Trim();
        // The model is the better judge of Persian vs Arabic: its call sets the language
        // the engine's own lines ("how many?", "added") are printed in.
        if (answer.Lang is { Length: 2 } spoken && SupportedLocales.Contains(spoken)) _replyLocale = spoken;
        switch (answer.Action)
        {
            case "search" when !string.IsNullOrWhiteSpace(answer.Query):
                _pendingQty = BotNlu.ExtractQuantity(text).Item1;   // "2 koobideh" keeps its 2
                await SearchFlowAsync(answer.Query, allStores: answer.Scope == "all", header: reply.Length > 0 ? reply : null);
                return true;
            case "menu":
                if (await ShowMenuCardsAsync(answer.Query, reply.Length > 0 ? reply : null)) return true;
                break;                                       // no store or empty menu: fall through to the text
            case "near":
                await NearFlowAsync(reply.Length > 0 ? reply : null);
                return true;
            case "stores":
                if (await ShowStoresAsync(answer.Query, reply.Length > 0 ? reply : null)) return true;
                break;
            case "pick" when answer.Option is { } n && n >= 1 && n <= _options.Count:
                if (reply.Length > 0) Bot(reply);
                // "the first one, three of them" / «۳ تا»: the count rides along with the pick.
                if (answer.Qty is { } pq && pq >= 1) _pendingQty = pq;
                else if (ParseBareQuantity(text) is { } hq) _pendingQty = hq;
                await SelectOptionAsync(n - 1);
                return true;
            case "qty" when answer.Qty is { } q && q >= 1 && _step == Step.PickQuantity:
                if (reply.Length > 0) Bot(reply);
                await HandleQuantityAsync(q);
                return true;
            // The engine asked "how many?" and the model read the answer as something else:
            // a count hidden in the text still wins over a fresh search.
            case "search" or "none" when _step == Step.PickQuantity && ParseBareQuantity(text) is { } hidden:
                await HandleQuantityAsync(hidden);
                return true;
            case "remove":
                if (reply.Length > 0) Bot(reply);
                await RemoveItemFlowAsync(answer.Query ?? "", "");
                return true;
            case "keep" when !string.IsNullOrWhiteSpace(answer.Query):
                if (reply.Length > 0) Bot(reply);
                await KeepOnlyAsync(answer.Query);
                return true;
            case "track": await TrackFlowAsync(BotNlu.ExtractOrderNumber(text)); return true;
            case "cancel": await CancelFlowAsync(BotNlu.ExtractOrderNumber(text)); return true;
            case "cart": ShowCart(); return true;
            case "checkout": await CheckoutFlowAsync(); return true;
            case "reorder": await ReorderFlowAsync(); return true;
            case "clear": ConfirmClearCart(); return true;
            case "help": ShowHelp(); return true;
        }
        if (reply.Length == 0) return false;            // nothing usable — let the rules try
        var chips = new List<BotChip>();
        if (!string.IsNullOrWhiteSpace(answer.Url)) chips.Add(Chip(TT("bot.chipOpen"), $"go:{answer.Url.Trim()}"));
        chips.Add(Chip(TT("bot.chipOrder"), "act:order"));
        chips.Add(Chip(TT("bot.chipHelp"), "act:help"));
        Bot(reply, chips.ToArray());
        return true;
    }

    private string StepName() => _step switch
    {
        Step.Idle => "idle",
        Step.AwaitQuery => "await-query",
        Step.PickResult => "pick-result",
        Step.PickQuantity => "pick-quantity",
        Step.ConfirmOrder => "confirm-order",
        var s => s.ToString().ToLowerInvariant(),
    };

    /// <summary>The basket as the model should read it: one line per item, then the subtotal.</summary>
    private string CartSummary()
    {
        if (cart.Lines.Count == 0) return "";
        var lines = cart.Lines.Select(l => $"{l.Quantity} x {l.Item.Name} @ {l.Restaurant.Name} = {Fmt.Money(l.LineTotal)}");
        return string.Join("\n", lines) + $"\nSubtotal {Fmt.Money(cart.Subtotal)}";
    }

    /// <summary>Slot-filling for the current step. False → fall through to global intents.</summary>
    private async Task<bool> TryHandleStepAsync(string text)
    {
        switch (_step)
        {
            case Step.PickQuantity:
            {
                // "no" here means "never mind", not the Persian nine.
                if (BotNlu.DetectYesNo(text) is false)
                {
                    _pendingItem = null;
                    _pendingQty = null;
                    _step = Step.Idle;
                    Bot(TT("bot.orderKept"), Chip(TT("bot.chipOrder"), "act:order"));
                    return true;
                }
                var (qty, _) = BotNlu.ExtractQuantity(text, bareAnswer: true);
                qty ??= ParseBareQuantity(text);          // "3تا", "۳ عدد", "x3", "3 pcs"
                if (qty is { } q) { await HandleQuantityAsync(q); return true; }
                return false;
            }
            case Step.PickResult:
            {
                // One card on screen and the customer answers with a count («3تا», "x2"):
                // that is "this one, three of them" — pick it and skip the how-many question.
                if (_options.Count == 1 && ParseBareQuantity(text) is { } count && !int.TryParse(text.Trim(), out _))
                {
                    _pendingQty = count;
                    await SelectOptionAsync(0);
                    return true;
                }
                // "what is this?", "which one is spicy?" — a question about the list on
                // screen is for the language model (it gets the list as context), never
                // for the fuzzy matcher, which would happily pick a product out of "this".
                if (LooksLikeQuestion(text))
                {
                    if (await TryAskModelAsync()) return true;
                    Bot(TT("bot.pickHint"),
                        _options.Take(6).Select((o, i) => Chip($"{i + 1}. {Truncate(o.Label, 20)}", $"opt:{i + 1}")).ToArray());
                    return true;
                }
                // "the first one", "the cheapest" — pointing at the list already on screen
                // instead of typing its number.
                if (ResolveByReference(text) is { } referenced)
                {
                    await SelectOptionAsync(referenced);
                    return true;
                }

                var idx = BotNlu.MatchOption(text, _options.Select(o => o.Match).ToList());
                if (idx >= 0) { await SelectOptionAsync(idx); return true; }
                // Literal name matching failed — the user may have named the
                // product in another language ("بيتزا" against "Margherita").
                // The server's keyword search (MenuItems.SearchKeywords) maps
                // words in any language to menu-item ids.
                if (await ResolveOptionByKeywordAsync(text) is { } resolved)
                {
                    await SelectOptionAsync(resolved);
                    return true;
                }
                // Still nothing — treat the text as a fresh food search.
                return false;
            }
            case Step.PickAddress or Step.PickPayment or Step.PickCard:
            {
                var idx = BotNlu.MatchOption(text, _options.Select(o => o.Match).ToList());
                if (idx >= 0) { await SelectOptionAsync(idx); return true; }
                // Let real commands through to their handlers.
                if (BotNlu.DetectIntent(text).Intent is not BotLexicon.Intent.None
                    and not BotLexicon.Intent.Yes and not BotLexicon.Intent.No) return false;
                // "2 pizza" typed at the address step means "I also want 2
                // pizzas" — if the words are real products, leave checkout and
                // show them instead of endlessly re-asking for the address.
                var (qty, rest) = BotNlu.ExtractQuantity(BotNlu.StripToQuery(text, ""));
                rest = BotNlu.StripStartOrderNoise(rest);
                if (!string.IsNullOrWhiteSpace(rest))
                {
                    var probe = await api.SearchAsync(rest, realOnly: true, storeId: EffectiveStoreId);
                    if (probe is not null && (probe.Dishes.Count > 0 || probe.Restaurants.Count > 0))
                    {
                        _pendingQty = qty;
                        await SearchFlowAsync(rest);
                        return true;
                    }
                }
                Bot(TT("bot.pickHint"),
                    _options.Take(6).Select((o, i) => Chip($"{i + 1}. {Truncate(o.Label, 20)}", $"opt:{i + 1}")).ToArray());
                return true;
            }
            case Step.PickTrackOrder or Step.PickCancelOrder:
            {
                var idx = BotNlu.MatchOption(text, _options.Select(o => o.Match).ToList());
                if (idx >= 0) { await SelectOptionAsync(idx); return true; }
                if (BotNlu.DetectIntent(text).Intent is not BotLexicon.Intent.None
                    and not BotLexicon.Intent.Yes and not BotLexicon.Intent.No) return false;
                Bot(TT("bot.pickHint"),
                    _options.Take(6).Select((o, i) => Chip($"{i + 1}. {Truncate(o.Label, 20)}", $"opt:{i + 1}")).ToArray());
                return true;
            }
            case Step.ConfirmSwitch or Step.ConfirmOrder or Step.ConfirmCancel or Step.ConfirmClear:
            {
                if (BotNlu.DetectYesNo(text) is { } yn) { await HandleYesNoAsync(yn); return true; }
                if (_step == Step.ConfirmOrder)
                {
                    // Real commands ("help", "show my cart") must not be mistaken
                    // for coupon codes; everything else short may be one.
                    if (BotNlu.DetectIntent(text).Intent != BotLexicon.Intent.None) return false;
                    if (await TryCouponAsync(text)) return true;
                }
                return false;
            }
            default:
                return false;
        }
    }

    private async Task HandleGlobalAsync(string text)
    {
        var intent = BotNlu.DetectIntent(text);

        // A question that is not a command ("can I bring my dog?", "is it quiet in the
        // evening?") goes to the language model first. Commands keep their rules: "where
        // is my order?" is a question too, and the tracker answers it better than any
        // model could. A greeting word inside a long question ("…in the evening") must
        // not turn the question into small talk either.
        var actionable = intent.Intent is BotLexicon.Intent.TrackOrder or BotLexicon.Intent.CancelOrder
            or BotLexicon.Intent.ShowCart or BotLexicon.Intent.ClearCart or BotLexicon.Intent.RemoveItem
            or BotLexicon.Intent.Checkout or BotLexicon.Intent.Reorder or BotLexicon.Intent.Help
            or BotLexicon.Intent.StartOrder;
        // "What can I get for 5 rials?" has its own answer — real combinations with cards —
        // so the budget rule runs ahead of the model.
        if (!actionable && await TryBudgetAsync(text)) return;
        if (!actionable && LooksLikeQuestion(text) && await TryAskModelAsync()) return;

        switch (intent.Intent)
        {
            case BotLexicon.Intent.Greeting:
                Bot(TT("bot.greetAgain"),
                    Chip(TT("bot.chipOrder"), "act:order"),
                    Chip(TT("bot.chipTrack"), "act:track"),
                    Chip(TT("bot.chipCart"), "act:cart"));
                return;
            case BotLexicon.Intent.Help: ShowHelp(); return;
            case BotLexicon.Intent.Thanks: Bot(TT("bot.thanks")); return;
            case BotLexicon.Intent.TrackOrder: await TrackFlowAsync(BotNlu.ExtractOrderNumber(text)); return;
            case BotLexicon.Intent.CancelOrder: await CancelFlowAsync(BotNlu.ExtractOrderNumber(text)); return;
            case BotLexicon.Intent.ShowCart: ShowCart(); return;
            case BotLexicon.Intent.ClearCart: ConfirmClearCart(); return;
            case BotLexicon.Intent.RemoveItem: await RemoveItemFlowAsync(text, intent.Phrase); return;
            case BotLexicon.Intent.Checkout: await CheckoutFlowAsync(); return;
            case BotLexicon.Intent.Reorder: await ReorderFlowAsync(); return;
            case BotLexicon.Intent.Yes or BotLexicon.Intent.No:
            {
                // A yes only means yes when it is the WHOLE message. Several languages
                // spell a yes the way English spells an ordinary word — "do" is one of
                // them — so "do you have pizza" arrives here scoring a perfect Yes. If
                // real words are left once the yes word and the fillers come off, this is
                // a request, not a confirmation.
                if (!string.IsNullOrWhiteSpace(BotNlu.StripToQuery(text, intent.Phrase)))
                {
                    await SearchFreeTextAsync(text);
                    return;
                }
                // A stray yes/no with nothing to confirm — never search for it.
                if (!await TryAskModelAsync()) Bot(TT("bot.fallback"), Chip(TT("bot.chipHelp"), "act:help"));
                return;
            }
            case BotLexicon.Intent.StartOrder:
            {
                // "make it 3" corrects what was just added instead of ordering more.
                if (TryQuantityCorrection(text)) return;
                // "I have 5 rials, which pizza can I get" asks a different question.
                if (await TryBudgetAsync(text)) return;
                // "2 pizzas and a juice" is two orders in one breath — take them both.
                if (await TryMultiItemAsync(text)) return;

                // Deliberately NOT stripping the matched phrase: lexicon phrases
                // sometimes carry example dishes ("ابغى برجر") and stripping the
                // phrase would delete the dish itself. Fillers + generic order
                // verbs go; whatever remains is the food query.
                var (qty, rest) = BotNlu.ExtractQuantity(BotNlu.StripToQuery(text, ""));
                rest = BotNlu.StripStartOrderNoise(rest);
                _pendingQty = qty;
                if (string.IsNullOrWhiteSpace(rest)) AskWhatToEat();
                else await SearchFlowAsync(rest);
                return;
            }
            default:
                await SearchFreeTextAsync(text);
                return;
        }
    }

    /// <summary>
    /// Anything that is not a recognised command. Most free text in an ordering chat IS a
    /// food query, so it is corrected, split, priced and finally searched.
    /// </summary>
    private async Task SearchFreeTextAsync(string text)
    {
        if (TryQuantityCorrection(text)) return;
        if (await TryBudgetAsync(text)) return;
        if (await TryMultiItemAsync(text)) return;

        var (qty, rest) = BotNlu.ExtractQuantity(BotNlu.StripToQuery(text, ""));
        _pendingQty = qty;               // fresh message, fresh quantity
        // "What do you have?" — once the asking words are gone nothing food-shaped is
        // left, and searching for "what" would only find a dish that sounds like it.
        if (IsMenuQuestion(rest)) { ShowMenuOverview(); return; }
        if (!string.IsNullOrWhiteSpace(rest)) await SearchFlowAsync(rest, fallbackHelp: true);
        else if (!await TryAskModelAsync()) Bot(TT("bot.fallback"), Chip(TT("bot.chipHelp"), "act:help"));
    }

    /// <summary>
    /// A question or a sentence, as opposed to an order: a question mark, an asking word up
    /// front, or simply a long sentence. A SHORT question naming a dish ("do you have
    /// pizza?") stays with the search — tappable cards answer it better than prose — but a
    /// long one is a real question even when a food word sits inside it ("can I bring my
    /// dog…" has "dog", and the search is fuzzy enough to "find" something for any words).
    /// </summary>
    private static bool LooksLikeQuestion(string text)
    {
        var tokens = BotNlu.Fold(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        var asks = text.Contains('?') || text.Contains('؟')
                   || AskStarts.Contains(tokens[0]) || (tokens.Length > 1 && AskStarts.Contains(tokens[1]));
        if (!asks && tokens.Length < 6) return false;
        return tokens.Length > 5 || !tokens.Any(BotNlu.IsFoodWord);
    }

    // ───────────────────────────── The language model ─────────────────────────────

    /// <summary>
    /// The last resort before "I didn't catch that": the line that no rule understood
    /// goes to the language model on the API (Grok), together with the last few turns,
    /// the page and the store being looked at. The model knows the platform and that
    /// store's real menu; it answers questions, it never places orders — those stay
    /// with this engine. False = no answer (no key, outage, throttled) and the caller
    /// prints its own fallback exactly as before.
    /// </summary>
    private async Task<bool> TryAskModelAsync()
    {
        if (DateTime.Now < _modelDownUntil) return false;
        var text = _messages.LastOrDefault(m => !m.FromBot)?.Text;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Everything before the line being answered, most recent nine lines at most.
        // The product cards under a bot line are part of what the customer saw — "what
        // is this?" refers to them — so they travel as numbered text.
        var earlier = _messages.Take(_messages.Count - 1)
            .Where(m => !string.IsNullOrWhiteSpace(m.Text) || m.OptionCards.Count > 0)
            .TakeLast(9)
            .Select(m => new AssistantTurnDto(m.FromBot ? "assistant" : "user", Truncate(HistoryText(m), 900)))
            .ToList();

        AssistantReplyDto? answer;
        try
        {
            answer = await api.AskAssistantAsync(new AssistantAskRequest(
                _session, text, _replyLocale ?? lang.Locale, Page, EffectiveStoreId ?? _pendingRestaurant?.Id, earlier, VisitorIp));
        }
        catch
        {
            answer = null;
        }
        if (answer is null || string.IsNullOrWhiteSpace(answer.Reply)) return false;

        Bot(answer.Reply.Trim(), Chip(TT("bot.chipOrder"), "act:order"), Chip(TT("bot.chipHelp"), "act:help"));
        return true;
    }

    // ───────────────────────────── "Do you …?" ─────────────────────────────

    /// <summary>
    /// A question about the service rather than a dish: "do you deliver?", "are you open?",
    /// "can I pay by card?", «هل توصلون؟», «باز هستید؟». Each has a one-line answer and the
    /// chips that take the person to the next step. Anything with a dish in it is NOT
    /// caught here — "do you have pizza" must still reach the search.
    /// </summary>
    private static readonly (string Key, string[] Words)[] Faq =
    [
        ("delivery", ["deliver", "delivery", "delivering", "deliveries", "توصيل", "توصلون", "توصل", "تتوصل", "توصيلكم", "ارسال", "ارسال میکنید", "پیک", "ميكنيد", "تحويل"]),
        ("pickup", ["pickup", "pick", "takeaway", "take", "collect", "collection", "استلام", "سفري", "تيك", "حضوري", "بردن", "تحويل"]),
        ("hours", ["open", "opening", "opened", "close", "closing", "closed", "hours", "time", "late", "مفتوح", "مفتوحين", "فاتحين", "تفتحون", "تفتح", "تسكرون", "تقفلون", "ساعات", "دوام", "باز", "بازه", "بازید", "بازين", "بسته", "تعطیل", "ساعت", "کی"]),
        ("payment", ["pay", "payment", "card", "cash", "visa", "mastercard", "apple", "online", "دفع", "الدفع", "ادفع", "بطاقه", "كاش", "نقدا", "نقد", "پرداخت", "کارت", "کارتی", "نقدی", "آنلاین"]),
        ("booking", ["book", "booking", "reserve", "reservation", "reservations", "table", "tables", "seat", "seats", "حجز", "احجز", "نحجز", "طاوله", "طاولات", "رزرو", "میز", "جا"]),
        ("halal", ["halal", "حلال", "ذبح", "حلاله", "حلاله"]),
        ("veg", ["vegetarian", "vegan", "veggie", "veg", "نباتي", "نباتيه", "نباتية", "گیاهی", "گياهي", "وگان"]),
        ("offers", ["offer", "offers", "discount", "discounts", "deal", "deals", "promo", "promotion", "coupon", "code", "voucher", "عرض", "عروض", "خصم", "خصومات", "كوبون", "كود", "تخفیف", "تخفيف", "کد", "پیشنهاد"]),
        ("contact", ["phone", "number", "call", "contact", "whatsapp", "email", "رقم", "هاتف", "اتصل", "تواصل", "واتساب", "واتس", "شماره", "تماس", "تلفن", "واتساپ"]),
        ("where", ["where", "location", "located", "address", "branch", "branches", "near", "nearby", "وين", "فين", "اين", "أين", "موقع", "موقعكم", "عنوان", "فرع", "فروع", "قريب", "کجا", "کجاست", "آدرس", "شعبه", "نزدیک"]),
    ];

    private static readonly HashSet<string> AskStarts = new(StringComparer.Ordinal)
    {
        "do", "does", "did", "can", "could", "is", "are", "will", "would", "may", "how", "when", "where", "what", "which", "any",
        "هل", "ممكن", "تقدر", "تقدرون", "عندكم", "عندك", "فيه", "في", "وين", "متى", "كيف", "شلون", "كم",
        "آیا", "ایا", "میشه", "میشود", "می‌شه", "می‌شود", "دارید", "داريد", "کی", "کجا", "چطور", "چند",
    };

    private bool TryFaq(string text)
    {
        // "where is my order?" is about an order, not about where the shop is — the
        // tracker owns it, however many FAQ words it carries.
        if (BotNlu.DetectIntent(text).Intent is BotLexicon.Intent.TrackOrder or BotLexicon.Intent.CancelOrder) return false;
        var folded = BotNlu.Fold(text);
        var tokens = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length > 12) return false;
        var asks = text.Contains('?') || text.Contains('؟') || AskStarts.Contains(tokens[0])
                   || (tokens.Length > 1 && AskStarts.Contains(tokens[1]));
        if (!asks) return false;
        // a dish in the sentence means it is a menu question, not a service one
        if (tokens.Any(BotNlu.IsFoodWord)) return false;

        foreach (var (key, words) in Faq)
        {
            if (!tokens.Any(tk => words.Any(w => tk == BotNlu.Fold(w) || (w.Length > 3 && tk.StartsWith(BotNlu.Fold(w), StringComparison.Ordinal))))) continue;
            _step = Step.AwaitQuery;
            switch (key)
            {
                case "veg":
                    Bot(TT("bot.faq.veg"), Chip(TT("bot.find.salad"), "find:salad"), Chip(TT("bot.find.vegetarian"), "find:vegetarian"), Chip(TT("bot.find.pizza"), "find:margherita"));
                    break;
                case "booking":
                    Bot(TT("bot.faq.booking"), Chip(TT("bot.chipOrder"), "act:order"));
                    break;
                case "offers":
                    Bot(TT("bot.faq.offers"), Chip(TT("bot.chipOrder"), "act:order"));
                    break;
                default:
                    Bot(TT("bot.faq." + key), Chip(TT("bot.chipOrder"), "act:order"), Chip(TT("bot.chipTrack"), "act:track"));
                    break;
            }
            return true;
        }
        return false;
    }

    /// <summary>The words left behind by a question about the menu rather than a dish.</summary>
    private static readonly HashSet<string> MenuQuestionWords = new(StringComparer.Ordinal)
    {
        "what", "whats", "wat", "which", "anything", "something", "everything", "options", "option", "menu",
        "list", "food", "foods", "dish", "dishes", "stuff", "items", "item", "special", "specials", "today",
        "available", "offer", "offers", "sell", "serve", "kind", "kinds", "type", "types", "there",
        // Arabic / Gulf / Persian: what, which, menu, things
        "ماذا", "ما", "ايش", "إيش", "شو", "وش", "ايه", "إيه", "عندكم", "عندك", "منيو", "منو", "قائمه", "اشياء", "شي",
        "چی", "چي", "چه", "چیزی", "منو", "غذا", "غذایی", "دارید", "داري", "داريد",
    };

    private static bool IsMenuQuestion(string rest)
    {
        if (string.IsNullOrWhiteSpace(rest)) return false;
        var tokens = BotNlu.Fold(rest).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(t => MenuQuestionWords.Contains(t) || t.Length <= 1);
    }

    /// <summary>Everything, in other words — so offer the cravings people actually type.</summary>
    private void ShowMenuOverview()
    {
        _step = Step.AwaitQuery;
        Bot(TT("bot.menuOverview"),
            Chip(TT("bot.find.pizza"), "find:pizza"),
            Chip(TT("bot.find.burger"), "find:burger"),
            Chip(TT("bot.find.biryani"), "find:biryani"),
            Chip(TT("bot.find.shawarma"), "find:shawarma"),
            Chip(TT("bot.find.grill"), "find:grill"),
            Chip(TT("bot.find.dessert"), "find:dessert"),
            Chip(TT("bot.find.drinks"), "find:juice"));
    }

    // ───────────────────────────── Search & pick ─────────────────────────────

    private void AskWhatToEat()
    {
        _step = Step.AwaitQuery;
        Bot(TT("bot.whatToEat"));
    }

    /// <param name="allStores">
    /// Leave the store being viewed and search the whole platform — only when the customer
    /// asked for it (the "Search all stores" chip, or the model's scope "all").
    /// </param>
    /// <param name="header">
    /// The model's own sentence to put above the cards. When given, the engine's stock
    /// "I found this…" and "Tap one…" lines are left out — the conversation stays in one voice.
    /// </param>
    private async Task SearchFlowAsync(string query, bool fallbackHelp = false, bool allStores = false, string? header = null)
    {
        var storeId = allStores ? null : EffectiveStoreId;

        var results = await api.SearchAsync(query, realOnly: true, storeId: storeId);
        var dishes = results?.Dishes ?? [];
        var restaurants = results?.Restaurants ?? [];

        // Platform-wide, the customer's whereabouts unknown, and the answer would span several
        // stores: ask where they are first, once. A request that already names its store
        // («آب دوغ رستوران سراج» → everything from Siraj) needs no location.
        var oneStore = dishes.Count > 0 && restaurants.Count == 0 && dishes.Select(d => d.RestaurantId).Distinct().Count() == 1;
        if (storeId is null && !oneStore && (dishes.Count > 0 || restaurants.Count > 0)
            && _lat is null && _nearStores is null && !_locationAsked)
        {
            _pendingQuery = query;
            await NearFlowAsync();
            return;
        }

        // Around the customer first: dishes from the neighbourhood stores, nearest store first.
        if (storeId is null && _nearStores is { Count: > 0 } near)
        {
            var rank = near.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i);
            var local = dishes.Where(d => rank.ContainsKey(d.RestaurantId)).OrderBy(d => rank[d.RestaurantId]).ToList();
            if (local.Count > 0) dishes = local;
            var localStores = restaurants.Where(r => rank.ContainsKey(r.Id)).OrderBy(r => rank[r.Id]).ToList();
            if (localStores.Count > 0 || local.Count > 0) restaurants = localStores;
        }

        if (dishes.Count == 0 && restaurants.Count == 0)
        {
            _pendingQty = null;                  // the quantity belonged to this failed query
            if (storeId is not null)
            {
                // Not on THIS store's menu. The store stays the main one: say so and leave the
                // door to the rest of the platform as a tap, never an automatic switch.
                var store = await CurrentStoreAsync();
                Bot(TT("bot.notHere", SN(store?.Name)),
                    Chip(TT("bot.chipAllStores"), $"findall:{query}"),
                    Chip(TT("bot.chipHelp"), "act:help"));
                _step = _step == Step.PickResult ? Step.PickResult : Step.AwaitQuery;
                return;
            }
            if (!await TryAskModelAsync())
                Bot(fallbackHelp
                    ? TT("bot.fallback")
                    : TT("bot.noResults", query), Chip(TT("bot.chipHelp"), "act:help"));
            _step = _step == Step.PickResult ? Step.PickResult : Step.AwaitQuery;
            return;
        }

        _options = [];
        var cards = new List<BotOptionView>();
        var lines = new List<string>();
        // One header only. The customer's own words (or our spelling of them) are not read
        // back — Majed 2026-09-21: "just answer like 'I found …, maybe it helps you'".
        // With the model speaking, its sentence IS the header (Majed 2026-10-01: "full grok").
        lines.Add(header ?? TT("bot.results"));

        // The partner's own uploaded product photos beat stock photography —
        // fetch mains for the real menu items in parallel (virtual ids ≤ 0
        // have none). Stock fallback also tries the QUERY, so searching
        // "burger" gives every result a burger photo even with a fancy name.
        var topDishes = dishes.Take(6).ToList();
        var uploaded = await Task.WhenAll(topDishes.Select(d => d.MenuItemId > 0
            ? api.GetDishPhotosAsync(d.MenuItemId)
            : Task.FromResult<List<DishPhotoDto>?>(null)));

        for (var i = 0; i < topDishes.Count; i++)
        {
            var d = topDishes[i];
            var photo = uploaded[i]?.FirstOrDefault()?.Data
                        ?? FoodPhoto.ForDish(d.Name)
                        ?? FoodPhoto.ForDish(query);
            // Match on BOTH names: the customer may type either what the card shows or the
            // English the catalog stores.
            _options.Add(new Option(OptKind.Dish, PN(d.Name), $"{d.Name} {PN(d.Name)} {d.RestaurantName}", d));
            cards.Add(new BotOptionView(_options.Count, d.ImageEmoji, photo,
                PN(d.Name), null, Fmt.Money(d.Price))
            {
                Venue = SN(d.RestaurantName),
                VenueEmoji = d.RestaurantLogoEmoji,
                Hue = HueOf(d.RestaurantName),
                Closed = !d.RestaurantIsOpen,
            });
        }
        foreach (var r in restaurants.Take(3))
        {
            _options.Add(new Option(OptKind.Restaurant, SN(r.Name), $"{r.Name} {SN(r.Name)} {r.Cuisine}", r));
            cards.Add(new BotOptionView(_options.Count, r.LogoEmoji, FoodPhoto.ForRestaurant(r.Cuisine, r.Id),
                SN(r.Name), $"{CN(r.Cuisine)} · ⭐{r.Rating:0.0}", "")
            {
                Hue = HueOf(r.Name),
                Closed = !r.IsOpen,
            });
        }
        if (header is null) lines.Add(TT("bot.pickHint"));

        _step = Step.PickResult;
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = string.Join('\n', lines),
            OptionCards = cards,
            // Inside a store the wider platform is one tap away, never the default.
            Chips = storeId is not null ? [Chip(TT("bot.chipAllStores"), $"findall:{query}")] : [],
        });
    }

    /// <summary>
    /// "Show me the menu with pictures" / "what's in Main Dishes?": the store's menu as
    /// photo cards — one category when named, otherwise the highlights (popular first) —
    /// each card tappable to order. The model's sentence sits above them.
    /// </summary>
    private async Task<bool> ShowMenuCardsAsync(string? category, string? header)
    {
        var storeId = EffectiveStoreId ?? _pendingRestaurant?.Id;
        if (storeId is null) return false;
        var detail = await api.GetRestaurantAsync(storeId.Value);
        if (detail is null) return false;

        var cats = detail.Categories;
        var pool = cats.SelectMany(c => c.Items.Select(i => (Cat: c, Item: i)));
        if (!string.IsNullOrWhiteSpace(category))
        {
            var f = BotNlu.Fold(category);
            var match = cats.Where(c =>
                    BotNlu.Fold(c.Name).Contains(f) || (f.Length >= 3 && f.Contains(BotNlu.Fold(c.Name))) ||
                    (c.Names?.Values.Any(n => !string.IsNullOrWhiteSpace(n) && BotNlu.Fold(n).Contains(f)) ?? false))
                .ToList();
            if (match.Count > 0) pool = match.SelectMany(c => c.Items.Select(i => (Cat: c, Item: i)));
        }
        var items = pool.Where(x => x.Item.IsAvailable)
            .OrderByDescending(x => x.Item.IsPopular).ThenBy(x => x.Cat.SortOrder).ThenBy(x => x.Item.SortOrder)
            .Take(12).ToList();
        if (items.Count == 0) return false;

        var locale = _replyLocale ?? lang.Locale;
        _options = [];
        var cards = new List<BotOptionView>();
        foreach (var (cat, i) in items)
        {
            var hit = new DishHitDto(i.Id, i.Name, i.Description, i.ImageEmoji, i.FinalPrice,
                detail.Info.Id, detail.Info.Name, detail.Info.LogoEmoji, detail.Info.IsOpen);
            _options.Add(new Option(OptKind.Dish, PN(i.Name), $"{i.Name} {PN(i.Name)}", hit));
            cards.Add(new BotOptionView(_options.Count, i.ImageEmoji, i.Photo ?? FoodPhoto.ForDish(i.Name),
                PN(i.Name), cat.NameFor(locale), Fmt.Money(i.FinalPrice))
            {
                Venue = SN(detail.Info.Name),
                VenueEmoji = detail.Info.LogoEmoji,
                Hue = HueOf(detail.Info.Name),
                Closed = !detail.Info.IsOpen,
            });
        }
        _step = Step.PickResult;
        _messages.Add(new BotMessage { FromBot = true, Text = header ?? TT("bot.results"), OptionCards = cards });
        return true;
    }

    private async Task SelectOptionAsync(int index)
    {
        if (index < 0 || index >= _options.Count) return;
        var option = _options[index];
        switch (option.Kind)
        {
            case OptKind.Dish: await PickDishAsync((DishHitDto)option.Payload); break;
            case OptKind.Restaurant: await ShowRestaurantAsync(((RestaurantCardDto)option.Payload).Id); break;
            case OptKind.Address: await PickAddressAsync((AddressDto)option.Payload); break;
            case OptKind.Payment: await PickPaymentAsync((PaymentMethod)option.Payload); break;
            case OptKind.Card: await PickCardAsync((CardDto)option.Payload); break;
            case OptKind.Order:
            {
                var order = (OrderDto)option.Payload;
                if (_step == Step.PickCancelOrder) await StartCancelAsync(order.Id);
                else await ShowOrderStatusAsync(order.Id);
                break;
            }
        }
    }

    /// <summary>
    /// Resolves a typed product word against the currently offered dish options
    /// through the server's multilingual keyword search — "برجر", "chiken" or
    /// "пицца" all land on the right listed item even though the stored name
    /// is in another language. Returns the option index, or null.
    /// </summary>
    private async Task<int?> ResolveOptionByKeywordAsync(string text)
    {
        // Real commands ("show my cart") must fall through to their handlers.
        if (BotNlu.DetectIntent(text).Intent
            is not (BotLexicon.Intent.None or BotLexicon.Intent.StartOrder)) return null;

        var (qty, rest) = BotNlu.ExtractQuantity(BotNlu.StripToQuery(text, ""));
        if (string.IsNullOrWhiteSpace(rest)) return null;

        var offered = new Dictionary<int, int>();
        for (var i = 0; i < _options.Count; i++)
            if (_options[i] is { Kind: OptKind.Dish, Payload: DishHitDto d })
                offered.TryAdd(d.MenuItemId, i);
        if (offered.Count == 0) return null;

        var hits = await api.SearchAsync(rest, realOnly: true);
        foreach (var d in hits?.Dishes ?? [])
            if (offered.TryGetValue(d.MenuItemId, out var index))
            {
                if (qty is not null) _pendingQty = qty;
                return index;
            }
        return null;
    }

    private async Task ShowRestaurantAsync(int restaurantId)
    {
        var detail = await api.GetRestaurantAsync(restaurantId);
        if (detail is null) { Bot(TT("common.wentWrong")); return; }
        if (!detail.Info.IsOpen)
        {
            Bot(TT("bot.closedNow", detail.Info.Name));
            return;
        }

        var items = detail.Categories.SelectMany(c => c.Items)
            .Where(i => i.IsAvailable)
            .OrderByDescending(i => i.IsPopular)
            .Take(8)
            .ToList();
        if (items.Count == 0) { Bot(TT("bot.noResults", detail.Info.Name)); return; }

        _options = [];
        var cards = new List<BotOptionView>();
        foreach (var item in items)
        {
            _options.Add(new Option(OptKind.Dish, PN(item.Name), $"{item.Name} {PN(item.Name)}",
                new DishHitDto(item.Id, item.Name, item.Description, item.ImageEmoji, item.FinalPrice,
                    detail.Info.Id, detail.Info.Name, detail.Info.LogoEmoji, detail.Info.IsOpen)));
            cards.Add(new BotOptionView(_options.Count, item.ImageEmoji,
                item.Photo ?? FoodPhoto.ForDish(item.Name)
                           ?? FoodPhoto.ForRestaurant(detail.Info.Cuisine, item.Id),
                PN(item.Name), item.Description is { Length: > 0 } ? Truncate(item.Description, 40) : null,
                Fmt.Money(item.FinalPrice))
            {
                Hue = HueOf(detail.Info.Name),   // the whole menu wears this restaurant's colour
            });
        }
        _step = Step.PickResult;
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = $"{detail.Info.LogoEmoji} {detail.Info.Name} — {TT("rest.popular")}:\n{TT("bot.pickHint")}",
            OptionCards = cards,
        });
    }

    private async Task PickDishAsync(DishHitDto dish)
    {
        var detail = await api.GetRestaurantAsync(dish.RestaurantId);
        if (detail is null) { Bot(TT("common.wentWrong")); return; }
        if (!detail.Info.IsOpen) { Bot(TT("bot.closedNow", detail.Info.Name)); return; }

        var item = detail.Categories.SelectMany(c => c.Items).FirstOrDefault(i => i.Id == dish.MenuItemId)
                   ?? detail.Categories.SelectMany(c => c.Items)
                       .FirstOrDefault(i => BotNlu.Fold(i.Name) == BotNlu.Fold(dish.Name));
        if (item is null || !item.IsAvailable) { Bot(TT("bot.unavailable", PN(dish.Name))); return; }

        _pendingRestaurant = detail.Info;
        _pendingItem = item;

        // Food from another restaurant just joins the basket — it becomes its own order.
        if (_pendingQty is { } q) await HandleQuantityAsync(q);
        else
        {
            _step = Step.PickQuantity;
            _messages.Add(new BotMessage
            {
                FromBot = true,
                Text = TT("bot.howMany", PN(item.Name)),
                QtyPicker = true,
            });
        }
    }

    private async Task HandleQuantityAsync(int qty)
    {
        if (_pendingItem is null || _pendingRestaurant is null)
        {
            _step = Step.Idle;
            Bot(TT("bot.fallback"), Chip(TT("bot.chipHelp"), "act:help"));
            return;
        }
        qty = Math.Clamp(qty, 1, 50);

        cart.Add(_pendingRestaurant, _pendingItem);
        var line = cart.FindLine(_pendingRestaurant.Id, _pendingItem.Id)!;
        for (var i = 1; i < qty; i++) cart.Increment(line);

        var itemName = PN(_pendingItem.Name);
        var lineTotal = Cash(_pendingItem.FinalPrice * qty);
        _lastAdded = (_pendingRestaurant.Id, _pendingItem.Id);   // what "it" refers to next
        _pendingItem = null;
        _pendingQty = null;
        _step = Step.Idle;

        Bot(TT("bot.added", qty, itemName, lineTotal, cart.Count, Cash(cart.Subtotal)),
            Chip(TT("bot.chipCheckout"), "act:checkout"),
            Chip(TT("bot.chipMore"), "act:more"),
            Chip(TT("bot.chipCart"), "act:cart"));

        // The customer asked for several things at once — carry on with the rest.
        await ProcessQueueAsync();
    }

    // ─────────────── Follow-ups that lean on the previous messages ───────────────

    /// <summary>The line added last, so "make it 3" and "remove it" know what "it" is.</summary>
    private (int RestaurantId, int ItemId)? _lastAdded;

    /// <summary>
    /// Turns "the first one" / "the cheapest" into an index in the list currently offered.
    /// Prices come from the offered dishes themselves, so "cheapest" means what it says.
    /// </summary>
    private int? ResolveByReference(string text)
    {
        var (kind, index) = BotNlu.ResolveReference(text, _options.Count);
        if (kind == BotNlu.Reference.None) return null;
        if (kind == BotNlu.Reference.Ordinal) return index;

        var priced = _options
            .Select((o, i) => (Index: i, Price: o.Payload switch
            {
                DishHitDto d => d.Price,
                MenuItemDto m => m.FinalPrice,
                _ => (decimal?)null,
            }))
            .Where(x => x.Price is not null)
            .ToList();
        if (priced.Count == 0) return null;

        return kind == BotNlu.Reference.Cheapest
            ? priced.MinBy(x => x.Price)!.Index
            : priced.MaxBy(x => x.Price)!.Index;
    }

    /// <summary>
    /// "Make it 3" right after adding something means correct that line, not order three
    /// more. Returns false when the message isn't a correction or there is nothing to correct.
    /// </summary>
    private bool TryQuantityCorrection(string text)
    {
        if (BotNlu.ReadQuantityCorrection(text) is not { } qty) return false;
        if (_lastAdded is not { } last) return false;
        if (cart.FindLine(last.RestaurantId, last.ItemId) is not { } line) return false;

        qty = Math.Clamp(qty, 1, 50);
        while (line.Quantity < qty) cart.Increment(line);
        while (line.Quantity > qty && cart.Lines.Contains(line)) cart.Decrement(line);

        Bot(TT("bot.added", qty, PN(line.Item.Name), Cash(line.LineTotal), cart.Count, Cash(cart.Subtotal)),
            Chip(TT("bot.chipCheckout"), "act:checkout"),
            Chip(TT("bot.chipCart"), "act:cart"));
        return true;
    }

    // ──────────────────────────── Shopping to a budget ────────────────────────────

    /// <summary>Combinations offered for the last budget question, addressable by chip.</summary>
    private List<List<(DishHitDto Dish, int Qty)>> _budgetCombos = [];

    /// <summary>
    /// Answers "I have 5 rials, which pizza can I buy with a pepsi or water" — the amount is
    /// a spending limit, the products are slots, and "or" offers alternatives inside a slot.
    /// Only combinations from ONE restaurant are offered, because a basket holds one.
    /// </summary>
    private async Task<bool> TryBudgetAsync(string text)
    {
        var (amount, rest) = BotNlu.ExtractBudget(text);
        if (amount is not { } budget) return false;

        // "What can I get for 5 rials?" names no food — answer with a spread of things
        // that actually fit, instead of searching the question words themselves.
        var slots = BotNlu.SplitAlternatives(rest);
        if (slots.Count == 0)
        {
            await SuggestWithinBudgetAsync(budget);
            return true;
        }

        // Each alternative becomes a handful of real products to choose between.
        var resolved = new List<(int Qty, List<DishHitDto> Options)>();
        foreach (var slot in slots)
        {
            var found = new List<DishHitDto>();
            foreach (var option in slot.Options)
            {
                var hits = await api.SearchAsync(option, realOnly: true, storeId: EffectiveStoreId);
                // A shut restaurant can't fill the order, so it must not shape the answer.
                if (hits is not null) found.AddRange(hits.Dishes.Where(d => d.RestaurantIsOpen).Take(8));
            }
            if (found.Count > 0) resolved.Add((slot.Qty, found));
        }
        if (resolved.Count == 0)
        {
            Bot(TT("bot.noResults", rest), Chip(TT("bot.chipOrder"), "act:order"));
            return true;
        }

        // A basket belongs to one restaurant, so only stores that stock EVERY slot count.
        var shared = resolved
            .Select(s => s.Options.Select(d => d.RestaurantId).ToHashSet())
            .Aggregate((a, b) => { a.IntersectWith(b); return a; });

        // Every basket a single restaurant can really fill, priced for the quantities asked.
        var baskets = new List<(List<(DishHitDto Dish, int Qty)> Combo, decimal Total)>();
        foreach (var storeId in shared)
        {
            var pick = resolved
                .Select(s => (Dish: s.Options.Where(d => d.RestaurantId == storeId)
                                             .OrderBy(d => d.Price).FirstOrDefault(), s.Qty))
                .ToList();
            if (pick.Any(p => p.Dish is null)) continue;
            var combo = pick.Select(p => (Dish: p.Dish!, p.Qty)).ToList();
            baskets.Add((combo, combo.Sum(c => c.Dish.Price * c.Qty)));
        }

        // No one restaurant stocks every slot. That is not a budget problem, and calling it
        // one is what produced "20 OMR isn't enough" for a 3.100 basket nobody could buy:
        // the old cheapest summed the slots across DIFFERENT restaurants, so it quoted a
        // total that was both unreachable and, as here, far below the stated budget.
        if (baskets.Count == 0)
        {
            Bot(TT("bot.budgetNoStore"), Chip(TT("bot.chipOrder"), "act:order"));
            return true;
        }

        // Two restaurants selling the same dish are one suggestion, not two.
        _budgetCombos = baskets
            .Where(b => b.Total <= budget)
            .OrderByDescending(b => b.Total)
            .DistinctBy(b => string.Join('|', b.Combo.Select(c => BotNlu.Fold(c.Dish.Name))))
            .Take(3)
            .Select(b => b.Combo)
            .ToList();

        if (_budgetCombos.Count == 0)
        {
            // Genuinely over budget — quote the cheapest basket one restaurant could fill.
            Bot(TT("bot.budgetNone", Cash(budget), Cash(baskets.Min(b => b.Total))),
                Chip(TT("bot.chipOrder"), "act:order"));
            return true;
        }

        var lines = new List<string> { TT("bot.budgetTitle", Cash(budget)) };
        for (var i = 0; i < _budgetCombos.Count; i++)
        {
            var combo = _budgetCombos[i];
            var names = string.Join(" + ", combo.Select(c =>
                c.Qty > 1 ? $"{c.Qty} × {PN(c.Dish.Name)}" : PN(c.Dish.Name)));
            lines.Add($"{i + 1}. {names} — {Cash(combo.Sum(c => c.Dish.Price * c.Qty))}");
        }
        _step = Step.Idle;
        Bot(string.Join('\n', lines),
            _budgetCombos.Select((_, i) => Chip($"{i + 1}. {TT("bot.chipBuy")}", $"buy:{i}")).ToArray());
        return true;
    }

    /// <summary>
    /// Answers a bare "what can I get for X?" — a varied handful of real products that fit,
    /// never the same dish repeated from three different kitchens.
    /// </summary>
    private async Task SuggestWithinBudgetAsync(decimal budget)
    {
        var found = new List<DishHitDto>();
        foreach (var term in BotLexicon.BudgetSampleTerms)
        {
            var hits = await api.SearchAsync(term, realOnly: true, storeId: EffectiveStoreId);
            if (hits is null) continue;
            found.AddRange(hits.Dishes.Where(d => d.RestaurantIsOpen));
        }

        // One entry per product name, cheapest source wins; then the ones that use the
        // budget best come first, so "5 rials" doesn't answer with a 0.100 bottle of water.
        var affordable = found
            .Where(d => d.Price <= budget)
            .GroupBy(d => BotNlu.Fold(d.Name))
            .Select(g => g.OrderBy(d => d.Price).First())
            .ToList();

        var picks = OneKindOfShop(affordable)
            .OrderByDescending(d => d.Price)
            .Take(5)
            .ToList();

        if (picks.Count == 0)
        {
            // The cheapest thing actually on sale. Filtering by budget first made this
            // report a price that could never exceed the budget it was contradicting.
            var cheapest = found.Count > 0 ? found.Min(d => d.Price) : 0;
            Bot(TT("bot.budgetNone", Cash(budget), Cash(cheapest)), Chip(TT("bot.chipOrder"), "act:order"));
            return;
        }

        _budgetCombos = picks.Select(d => new List<(DishHitDto Dish, int Qty)> { (d, 1) }).ToList();

        var lines = new List<string> { TT("bot.budgetTitle", Cash(budget)) };
        lines.AddRange(picks.Select((d, i) =>
            $"{i + 1}. {PN(d.Name)} · {d.RestaurantLogoEmoji} {SN(d.RestaurantName)} — {Cash(d.Price)}"));

        _step = Step.Idle;
        Bot(string.Join('\n', lines),
            picks.Select((_, i) => Chip($"{i + 1}. {TT("bot.chipBuy")}", $"buy:{i}")).ToArray());
    }

    /// <summary>
    /// Narrows a mixed pile of search hits to ONE kind of shop.
    ///
    /// <para>"What can I get for 10 rials?" must not come back with a pizza and a flash
    /// drive side by side. The catalog spans restaurants, groceries, pharmacies, florists
    /// and general shops, and a list drawn from all of them at once is not a suggestion —
    /// it is a search result the customer has to sort out themselves.</para>
    ///
    /// <para>Restaurants win whenever they have anything at all to offer, because this is
    /// first and foremost a food app and "what can I buy" from a hungry customer means
    /// food. Only when no restaurant has something in budget does the answer move to
    /// whichever other kind of shop has the most to show.</para>
    /// </summary>
    public static List<DishHitDto> OneKindOfShop(IEnumerable<DishHitDto> hits)
    {
        var all = hits as IList<DishHitDto> ?? hits.ToList();
        if (all.Count == 0) return [];

        var food = all.Where(d => d.StoreType == StoreType.Restaurant).ToList();
        if (food.Count > 0) return food;

        return all.GroupBy(d => d.StoreType)
                  .OrderByDescending(g => g.Count())
                  .ThenBy(g => g.Key)
                  .First()
                  .ToList();
    }

    /// <summary>
    /// Puts every product of a chosen combination into the basket. These products are
    /// already resolved, so they are added directly — searching their names again would
    /// only re-open the ambiguity the customer just settled.
    /// </summary>
    private async Task BuyComboAsync(int index)
    {
        if (index < 0 || index >= _budgetCombos.Count) { Bot(TT("bot.fallback")); return; }

        _queue.Clear();
        _dishQueue.Clear();
        _dishQueue.AddRange(_budgetCombos[index]);
        _budgetCombos = [];
        await ProcessQueueAsync();
    }

    // ──────────────────── Several products in one sentence ────────────────────

    /// <summary>What is still owed from a message like "2 pizzas and a juice".</summary>
    private readonly List<BotNlu.OrderPhrase> _queue = [];

    /// <summary>
    /// Handles "ثنين بيترا و واحد عصير" — two pizzas AND one juice. Returns false for an
    /// ordinary single-product message so the normal search path runs.
    /// </summary>
    private async Task<bool> TryMultiItemAsync(string text)
    {
        var phrases = BotNlu.SplitOrderItems(text);
        if (phrases.Count < 2) return false;

        _queue.Clear();
        _queue.AddRange(phrases);
        await ProcessQueueAsync();
        return true;
    }

    /// <summary>
    /// Works down the queue, adding anything that resolves to a single obvious product and
    /// stopping at the first genuinely ambiguous one so the customer can choose. The rest
    /// resumes by itself once they have.
    /// </summary>
    /// <summary>Products already chosen (a budget combination) still waiting to be added.</summary>
    private readonly List<(DishHitDto Dish, int Qty)> _dishQueue = [];

    private async Task ProcessQueueAsync()
    {
        if (_dishQueue.Count > 0)
        {
            var (dish, qty) = _dishQueue[0];
            _dishQueue.RemoveAt(0);
            _pendingQty = qty;
            await PickDishAsync(dish);
            return;
        }

        while (_queue.Count > 0)
        {
            var phrase = _queue[0];
            _queue.RemoveAt(0);

            var results = await api.SearchAsync(phrase.Query, realOnly: true, storeId: EffectiveStoreId);
            var dishes = results?.Dishes ?? [];
            if (dishes.Count == 0)
            {
                Bot(TT("bot.noResults", phrase.Query));
                continue;
            }

            // Keep the basket in one restaurant when we can — an auto-pick must never
            // silently trigger a "start a new basket?" question mid-list.
            var sameStore = cart.Restaurant is { } r
                ? dishes.Where(d => d.RestaurantId == r.Id).ToList()
                : dishes;
            var candidates = sameStore.Count > 0 ? sameStore : dishes;

            var exact = candidates
                .Where(d => BotNlu.Fold(d.Name).Contains(phrase.Query, StringComparison.Ordinal))
                .ToList();
            var chosen = candidates.Count == 1 ? candidates[0]
                : exact.Count == 1 ? exact[0]
                : null;

            if (chosen is null)
            {
                // Ambiguous: show the choices, remember the quantity, and pause here.
                // No number said for this item means one of it — in a multi-item order
                // stopping to ask "how many?" for every line would be maddening.
                _pendingQty = phrase.Qty ?? 1;
                await SearchFlowAsync(phrase.Query);
                return;
            }

            _pendingQty = phrase.Qty;
            await PickDishAsync(chosen);
            // PickDishAsync either added it (and drained more of the queue) or asked
            // something — either way this loop must not double-handle the same item.
            return;
        }
    }

    // ───────────────────────────── Cart ─────────────────────────────

    private void ShowCart()
    {
        if (cart.Lines.Count == 0)
        {
            _step = Step.AwaitQuery;
            Bot(TT("bot.cartEmpty"));
            return;
        }
        // Text is the fallback for history; the widget renders the newest cart
        // message as a live modern card bound to CartState. One block per restaurant,
        // because each of them becomes its own order.
        var lines = new List<string>();
        foreach (var group in cart.Groups)
        {
            lines.Add(TT("bot.cartTitle", $"{group.Restaurant.LogoEmoji} {SN(group.Restaurant.Name)}"));
            lines.AddRange(group.Lines.Select(l => $"• {l.Quantity} × {PN(l.Item.Name)} — {Cash(l.LineTotal)}"));
        }
        lines.Add($"{TT("common.subtotal")}: {Cash(cart.Subtotal)}");
        _step = Step.Idle;
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = string.Join('\n', lines),
            CartCard = true,
            Chips =
            [
                Chip(TT("bot.chipCheckout"), "act:checkout"),
                Chip(TT("bot.chipMore"), "act:more"),
                Chip(TT("bot.chipClear"), "act:clear"),
            ],
        });
    }

    /// <summary>"remove the pizza" / "minus one cola" — take an item out of the cart.</summary>
    /// <summary>
    /// "Keep only the doogh": every basket line that is NOT the named item goes; the named
    /// item stays. Names are matched the same way removal matches them — by the basket's own
    /// names in either language, then by the server's multilingual keyword search.
    /// </summary>
    private async Task KeepOnlyAsync(string query)
    {
        if (cart.Lines.Count == 0) { _step = Step.AwaitQuery; Bot(TT("bot.cartEmpty")); return; }
        var keep = new HashSet<int>();
        var idx = BotNlu.MatchOption(query, cart.Lines.Select(l => $"{l.Item.Name} {PN(l.Item.Name)}").ToList());
        if (idx >= 0) keep.Add(cart.Lines[idx].Item.Id);
        var folded = BotNlu.Fold(query);
        foreach (var l in cart.Lines)
            if (BotNlu.Fold(l.Item.Name).Contains(folded) || BotNlu.Fold(PN(l.Item.Name)).Contains(folded)) keep.Add(l.Item.Id);
        if (keep.Count == 0)
        {
            var hits = await api.SearchAsync(query, realOnly: true, storeId: cart.Restaurant?.Id);
            var ids = hits?.Dishes.Select(d => d.MenuItemId).ToHashSet() ?? [];
            foreach (var l in cart.Lines) if (ids.Contains(l.Item.Id)) keep.Add(l.Item.Id);
        }
        if (keep.Count == 0)
        {
            Bot(TT("bot.removeWhat"), cart.Lines.Take(6).Select(l => Chip($"🗑️ {Truncate(PN(l.Item.Name), 20)}", $"rm:{l.Item.Id}")).ToArray());
            return;
        }
        foreach (var l in cart.Lines.Where(l => !keep.Contains(l.Item.Id)).ToList())
            RemoveLine(l.Item.Id, null);
        ShowCart();
    }

    private async Task RemoveItemFlowAsync(string text, string matchedPhrase)
    {
        if (cart.Lines.Count == 0)
        {
            _step = Step.AwaitQuery;
            Bot(TT("bot.cartEmpty"));
            return;
        }

        var (qty, query) = BotNlu.ExtractQuantity(BotNlu.StripToQuery(text, matchedPhrase));

        // "remove it" points at whatever went in last — no need to ask which.
        if ((string.IsNullOrWhiteSpace(query) || BotNlu.IsBarePronoun(query)) &&
            _lastAdded is { } last && cart.FindLine(last.RestaurantId, last.ItemId) is { } lastLine)
        {
            RemoveLine(lastLine.Item.Id, qty);
            return;
        }

        if (string.IsNullOrWhiteSpace(query) || BotNlu.IsBarePronoun(query))
        {
            // "remove something" with no item named — offer the cart lines.
            Bot(TT("bot.removeWhat"),
                cart.Lines.Take(6).Select(l => Chip($"🗑️ {Truncate(PN(l.Item.Name), 20)}", $"rm:{l.Item.Id}")).ToArray());
            return;
        }

        var idx = BotNlu.MatchOption(query, cart.Lines.Select(l => $"{l.Item.Name} {PN(l.Item.Name)}").ToList());
        if (idx >= 0)
        {
            RemoveLine(cart.Lines[idx].Item.Id, qty);
            return;
        }

        // Cart names are stored in one language, but the user may name the item
        // in another ("شيل الشاورما" against "Chicken Shawarma"). The server's
        // multilingual search resolves the words to menu-item ids.
        var hits = await api.SearchAsync(query, realOnly: true);
        var hitIds = hits?.Dishes.Select(d => d.MenuItemId).ToHashSet() ?? [];
        var line = cart.Lines.FirstOrDefault(l => hitIds.Contains(l.Item.Id));
        if (line is not null)
        {
            RemoveLine(line.Item.Id, qty);
            return;
        }

        Bot(TT("bot.notInCart", query),
            cart.Lines.Take(6).Select(l => Chip($"🗑️ {Truncate(l.Item.Name, 20)}", $"rm:{l.Item.Id}")).ToArray());
    }

    /// <summary>Reduce a cart line by <paramref name="qty"/>, or drop it entirely.</summary>
    private void RemoveLine(int menuItemId, int? qty)
    {
        var line = cart.Lines.FirstOrDefault(l => l.Item.Id == menuItemId);
        if (line is null) { ShowCart(); return; }
        var name = PN(line.Item.Name);

        if (qty is { } q && q < line.Quantity)
        {
            for (var i = 0; i < q; i++) cart.Decrement(line);
            Bot(TT("bot.decreased", line.Quantity, name, cart.Count, Cash(cart.Subtotal)),
                Chip(TT("bot.chipCheckout"), "act:checkout"),
                Chip(TT("bot.chipCart"), "act:cart"));
            return;
        }

        while (line.Quantity > 0) cart.Decrement(line);
        _step = cart.Lines.Count == 0 ? Step.AwaitQuery : Step.Idle;
        Bot(cart.Lines.Count == 0
                ? $"{TT("bot.removed", name, 0, Cash(0))}\n{TT("bot.cartEmpty")}"
                : TT("bot.removed", name, cart.Count, Cash(cart.Subtotal)),
            Chip(TT("bot.chipCart"), "act:cart"),
            Chip(TT("bot.chipMore"), "act:more"));
    }

    private void ConfirmClearCart()
    {
        if (cart.Lines.Count == 0)
        {
            Bot(TT("bot.cartEmpty"));
            return;
        }
        _step = Step.ConfirmClear;
        Bot(TT("bot.clearConfirm", cart.Count),
            Chip(TT("bot.chipYes"), "sys:yes"), Chip(TT("bot.chipNo"), "sys:no"));
    }

    // ───────────────────────────── Checkout ─────────────────────────────

    private async Task CheckoutFlowAsync()
    {
        // Every checkout starts with a clean coupon: the cart may have changed
        // since a previous attempt, and a stale discount corrupts the summary
        // (Checkout.razor re-validates on every cart change for the same reason).
        _couponCode = null;
        _discount = 0;

        if (cart.Lines.Count == 0)
        {
            _step = Step.AwaitQuery;
            Bot(TT("bot.cartEmpty"));
            return;
        }
        // Each restaurant has to reach its own minimum before any of it can be sent.
        if (cart.BelowMinimum.FirstOrDefault() is { } short_)
        {
            _step = Step.Idle;
            Bot($"{SN(short_.Restaurant.Name)}: {TT("rest.minWarn", Cash(short_.Restaurant.MinOrder), Cash(short_.Missing))}",
                Chip(TT("bot.chipMore"), "act:more"));
            return;
        }

        var addresses = await api.GetAddressesAsync() ?? [];
        if (addresses.Count == 0)
        {
            _step = Step.Idle;
            Bot(TT("bot.noAddresses"), Chip(TT("bot.chipAddAddress"), "nav:/addresses"));
            return;
        }

        if (addresses.Count == 1)
        {
            await PickAddressAsync(addresses[0], announce: true);
            return;
        }

        _options = addresses
            .Select(a => new Option(OptKind.Address, a.Label, $"{a.Label} {a.Area} {a.Street}", a))
            .ToList();
        _step = Step.PickAddress;
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = TT("bot.chooseAddress"),
            OptionCards = addresses.Select((a, i) => AddressCard(i + 1, a)).ToList(),
        });
    }

    /// <summary>A tappable address tile — far easier to read than a numbered list.</summary>
    private BotOptionView AddressCard(int number, AddressDto a) =>
        new(number, AddressEmoji(a.Label), null, a.Label,
            string.Join(", ", new[] { a.Building, a.Street }.Where(s => !string.IsNullOrWhiteSpace(s))), "")
        {
            Venue = a.Area,
            VenueEmoji = "📍",
            Hue = HueOf(a.Area),
        };

    /// <summary>Matches the label a customer typed, in any of the app's languages.</summary>
    private static string AddressEmoji(string label) => label.ToLowerInvariant() switch
    {
        var l when Mentions(l, "home", "house", "منزل", "بيت", "خانه", "گھر", "घर", "ev", "casa", "maison", "haus", "дом", "家", "自宅") => "🏠",
        var l when Mentions(l, "work", "office", "عمل", "مكتب", "دفتر", "کام", "काम", "iş", "ofis", "oficina", "bureau", "büro", "ufficio", "работа", "офис", "公司", "会社") => "🏢",
        var l when Mentions(l, "hotel", "فندق", "هتل", "ہوٹل", "होटल", "otel", "酒店", "ホテル") => "🏨",
        var l when Mentions(l, "shop", "store", "متجر", "محل", "مغازه", "दुकान", "dükkan", "tienda", "магазин", "商店", "店") => "🏪",
        _ => "📍"
    };

    private static bool Mentions(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

    private async Task PickAddressAsync(AddressDto address, bool announce = false)
    {
        _addressId = address.Id;
        _addressLabel = $"{address.Label} — {address.Building}, {address.Street}, {address.Area}";

        var cards = await api.GetCardsAsync() ?? [];
        _options =
        [
            new Option(OptKind.Payment, $"💵 {TT("pay.CashOnDelivery")}", $"{TT("pay.CashOnDelivery")} cash نقد كاش", PaymentMethod.CashOnDelivery),
            new Option(OptKind.Payment, $"💳 {TT("pay.CardOnDelivery")}", $"{TT("pay.CardOnDelivery")} card بطاقه", PaymentMethod.CardOnDelivery),
        ];
        var views = new List<BotOptionView>
        {
            new(1, "💵", null, TT("pay.CashOnDelivery"), TT("bot.payCashSub"), "") { Hue = 96 },
            new(2, "💳", null, TT("pay.CardOnDelivery"), TT("bot.payCardSub"), "") { Hue = 210 },
        };
        if (cards.Count > 0)
        {
            _options.Add(new Option(OptKind.Payment, $"⚡ {TT("checkout.payOnlineTest")}", $"{TT("checkout.payOnlineTest")} online اونلاين", PaymentMethod.CardOnline));
            views.Add(new BotOptionView(3, "⚡", null, TT("checkout.payOnlineTest"), TT("bot.payOnlineSub"), "") { Hue = 276 });
        }

        var lines = new List<string>();
        if (announce) lines.Add(TT("bot.address", _addressLabel));
        lines.Add(TT("bot.choosePayment"));
        _step = Step.PickPayment;
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = string.Join('\n', lines),
            OptionCards = views,
        });
    }

    private async Task PickPaymentAsync(PaymentMethod method)
    {
        _payment = method;
        _cardId = null;
        if (method == PaymentMethod.CardOnline)
        {
            var cards = await api.GetCardsAsync() ?? [];
            if (cards.Count == 0)
            {
                Bot(TT("bot.noCards"));
                _step = Step.PickPayment;
                return;
            }
            if (cards.Count > 1)
            {
                _options = cards
                    .Select(c => new Option(OptKind.Card, $"💳 {c.Brand} •••• {c.Last4}", $"{c.Brand} {c.Last4}", c))
                    .ToList();
                _step = Step.PickCard;
                _messages.Add(new BotMessage
                {
                    FromBot = true,
                    Text = TT("bot.chooseCard"),
                    OptionCards = cards.Select((c, i) => new BotOptionView(
                        i + 1, "💳", null, $"{c.Brand} •••• {c.Last4}",
                        $"{c.HolderName} · {c.ExpMonth:00}/{c.ExpYear % 100:00}", "")
                    {
                        Hue = HueOf(c.Brand),
                    }).ToList(),
                });
                return;
            }
            _cardId = cards[0].Id;
        }
        ShowOrderSummary();
    }

    private Task PickCardAsync(CardDto card)
    {
        _cardId = card.Id;
        ShowOrderSummary();
        return Task.CompletedTask;
    }

    private void ShowOrderSummary()
    {
        if (cart.Lines.Count == 0)
        {
            // Emptied from the main UI mid-checkout — never dead-end silently.
            _step = Step.AwaitQuery;
            Bot(TT("bot.cartEmpty"));
            return;
        }
        var groups = cart.Groups;
        // Every restaurant is billed separately, so every one adds its own fees.
        var total = cart.Subtotal + cart.DeliveryFees + ServiceFee * groups.Count - _discount;

        var charges = new List<BotBillLine>
        {
            new(TT("common.subtotal"), Fmt.Money(cart.Subtotal)),
            new(TT("common.deliveryFee"), Fmt.Money(cart.DeliveryFees)),
            new(TT("common.serviceFee"), Fmt.Money(ServiceFee * groups.Count)),
        };
        if (_discount > 0)
            charges.Add(new BotBillLine($"🏷️ {TT("checkout.coupon")} · {_couponCode!.ToUpperInvariant()}",
                $"−{Fmt.Money(_discount)}", "off"));

        // The address label is stored as "Home — Villa 1, Street 5, Al Khuwair";
        // split it so the card can show the name apart from the street.
        var dash = _addressLabel.IndexOf('—');
        var addressName = dash > 0 ? _addressLabel[..dash].Trim() : _addressLabel;
        var addressRest = dash > 0 ? _addressLabel[(dash + 1)..].Trim() : "";

        _step = Step.ConfirmOrder;
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = TT("bot.confirmTitle"),
            Bill = new BotBill(
                // With several kitchens the header names them all — the bill below is the
                // sum, and each restaurant is still charged and delivered on its own.
                groups.Count == 1 ? groups[0].Restaurant.LogoEmoji : "🧾",
                groups.Count == 1
                    ? SN(groups[0].Restaurant.Name)
                    : string.Join(" + ", groups.Select(g => SN(g.Restaurant.Name))),
                HueOf(groups[0].Restaurant.Name),
                groups.SelectMany(g => g.Lines)
                      .Select(l => new BotBillLine($"{l.Quantity} × {PN(l.Item.Name)}", Fmt.Money(l.LineTotal))).ToList(),
                charges,
                TT("common.total"), Fmt.Money(total),
                AddressEmoji(addressName), addressName, addressRest,
                PaymentEmoji(_payment), TT("checkout.payment"), PaymentLabel(_payment),
                TT("bot.confirmFooter")),
            Chips = [Chip(TT("bot.chipYes"), "sys:yes"), Chip(TT("bot.chipNo"), "sys:no")],
        });
    }

    private static string PaymentEmoji(PaymentMethod m) => m switch
    {
        PaymentMethod.CashOnDelivery => "💵",
        PaymentMethod.CardOnDelivery => "💳",
        _ => "⚡",
    };

    private string PaymentLabel(PaymentMethod m) => m switch
    {
        PaymentMethod.CashOnDelivery => TT("pay.CashOnDelivery"),
        PaymentMethod.CardOnDelivery => TT("pay.CardOnDelivery"),
        _ => TT("checkout.payOnlineTest"),
    };

    private async Task<bool> TryCouponAsync(string text)
    {
        var folded = BotNlu.Fold(text).Replace(" ", "");
        if (folded.Length is < 3 or > 15 || !folded.All(char.IsLetterOrDigit)) return false;
        var (result, error) = await api.ValidateCouponAsync(folded, cart.Subtotal);
        if (result is null) { Bot(error ?? TT("common.wentWrong")); return true; }
        if (!result.Valid)
        {
            Bot($"🏷️ {result.Message}");
            return true;
        }
        _couponCode = folded;
        _discount = result.Discount;
        Bot(TT("bot.couponApplied", folded.ToUpperInvariant(), Cash(result.Discount)));
        ShowOrderSummary();
        return true;
    }

    private async Task PlaceOrderAsync()
    {
        if (cart.Lines.Count == 0)
        {
            _step = Step.AwaitQuery;
            Bot(TT("bot.cartEmpty"));
            return;
        }

        // One order per restaurant, each with its own bill. A coupon belongs to a single
        // order, so it is spent on the biggest one.
        var groups = cart.Groups;
        var couponGroupId = groups.OrderByDescending(g => g.Subtotal).First().Restaurant.Id;
        var placed = new List<OrderDto>();

        foreach (var group in groups)
        {
            var request = new PlaceOrderRequest(
                group.Restaurant.Id,
                _addressId,
                _payment,
                _discount > 0 && group.Restaurant.Id == couponGroupId ? _couponCode : null,
                null,
                cart.ToOrderItems(group.Restaurant.Id),
                _payment == PaymentMethod.CardOnline ? _cardId : null);

            var (order, error) = await api.PlaceOrderAsync(request);
            if (order is null)
            {
                // Keep what already went through; only the failed restaurant is left to fix.
                foreach (var done in placed) cart.ClearGroup(done.RestaurantId);
                _step = Step.ConfirmOrder;
                Bot(TT("bot.orderFailed", $"{group.Restaurant.Name}: {lang.ServerError(error) ?? TT("common.orderFail")}"));
                return;
            }
            placed.Add(order);
        }

        cart.Clear();
        _couponCode = null;
        _discount = 0;
        _step = Step.Idle;

        if (placed.Count == 1)
        {
            Bot(TT("bot.orderPlaced", placed[0].Number, placed[0].EstimatedMinutes),
                Chip(TT("bot.chipLive"), $"nav:/track/{placed[0].Id}"),
                Chip(TT("bot.chipTrack"), "act:track"));
            return;
        }

        var lines = new List<string> { TT("bot.ordersPlaced", placed.Count) };
        lines.AddRange(placed.Select(o => $"🧾 {o.Number} — {o.RestaurantLogoEmoji} {o.RestaurantName} · {Cash(o.Total)}"));
        Bot(string.Join('\n', lines),
            placed.Select(o => Chip($"🧾 {o.Number}", $"nav:/track/{o.Id}"))
                  .Append(Chip(TT("bot.chipTrack"), "act:track")).ToArray());
    }

    // ───────────────────────────── Tracking ─────────────────────────────

    private async Task TrackFlowAsync(string? explicitNumber)
    {
        if (!SignedInOr()) return;
        var fetched = await api.GetMyOrdersAsync(0, 20);
        if (fetched is null) { Bot(TT("common.wentWrong")); return; }   // network down ≠ no orders
        var orders = fetched.OrderByDescending(o => o.PlacedAt).ToList();

        if (explicitNumber is not null && FindByNumber(orders, explicitNumber) is { } match)
        {
            await ShowOrderStatusAsync(match.Id);
            return;
        }

        var active = orders.Where(o => o.Status < OrderStatus.Delivered).ToList();
        if (active.Count == 0)
        {
            _step = Step.Idle;
            if (orders.Count == 0)
            {
                Bot(TT("bot.reorderNone"));
                return;
            }
            var last = orders[0];
            Bot($"{TT("bot.trackNone")}\n{TT("bot.lastWas", last.Number, TT($"status.{last.Status}"))}",
                Chip(TT("bot.chipReorder"), "act:reorder"),
                Chip(TT("bot.chipOrder"), "act:order"));
            return;
        }
        if (active.Count == 1)
        {
            await ShowOrderStatusAsync(active[0].Id);
            return;
        }

        _options = active
            .Select(o => new Option(OptKind.Order, $"{o.Number} · {o.RestaurantName}", $"{o.Number} {o.RestaurantName}", o))
            .ToList();
        _step = Step.PickTrackOrder;
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = $"{TT("bot.pickOrder")}\n{TT("bot.pickHint")}",
            OptionCards = active.Select((o, i) => OrderCard(i + 1, o)).ToList(),
        });
    }

    /// <summary>An order as a colour-coded card: number, its restaurant's pill, status badge.</summary>
    private BotOptionView OrderCard(int number, OrderDto o) =>
        new(number, "🧾", null, o.Number, null, "")
        {
            Venue = SN(o.RestaurantName),
            VenueEmoji = o.RestaurantLogoEmoji,
            Hue = HueOf(o.RestaurantName),
            BadgeText = $"{StatusEmoji(o.Status)} {TT($"status.{o.Status}")}",
            BadgeTone = StatusTone(o.Status),
        };

    private async Task ShowOrderStatusAsync(int orderId)
    {
        var order = await api.GetOrderAsync(orderId);
        if (order is null) { Bot(TT("common.wentWrong")); return; }

        // The banner above the bubble already names the order, its restaurant and its
        // status in colour, so the text starts at the story instead of repeating them.
        var lines = new List<string> { StatusStory(order) };

        if (order.Status < OrderStatus.Delivered)
        {
            var eta = order.PlacedAt.AddMinutes(order.EstimatedMinutes) - DateTime.Now;
            lines.Add(eta.TotalMinutes >= 1.5
                ? TT("bot.etaMin", (int)Math.Round(eta.TotalMinutes))
                : TT("bot.etaSoon"));
            if (order.DriverName is { Length: > 0 } driver && order.Status >= OrderStatus.PickedUp)
                lines.Add(TT("bot.driverLine", driver));
        }

        _step = Step.Idle;
        var chips = new List<BotChip> { Chip(TT("bot.chipLive"), $"nav:/track/{order.Id}") };
        if (order.Status == OrderStatus.Pending)
            chips.Add(Chip(TT("track.cancelOrder"), $"cancel:{order.Id}"));
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = string.Join('\n', lines),
            Chips = chips,
            Banner = OrderCard(0, order),
        });
    }

    private string StatusStory(OrderDto o) => o.Status switch
    {
        OrderStatus.Pending => TT("track.hPending"),
        OrderStatus.Accepted => TT("track.hAccepted", SN(o.RestaurantName)),
        OrderStatus.Preparing => TT("track.hPreparing"),
        OrderStatus.Ready => TT("track.hReady"),
        OrderStatus.PickedUp => TT("track.hPickedUp", o.DriverName ?? ""),
        OrderStatus.OnTheWay => TT("track.hOnTheWay", o.DriverName ?? ""),
        OrderStatus.Delivered => TT("track.hDelivered"),
        OrderStatus.Cancelled => TT("track.hCancelled"),
        _ => TT("track.hRejected"),
    };

    private static string StatusEmoji(OrderStatus s) => s switch
    {
        OrderStatus.Pending => "🕐",
        OrderStatus.Accepted => "✅",
        OrderStatus.Preparing => "👨‍🍳",
        OrderStatus.Ready => "📦",
        OrderStatus.PickedUp or OrderStatus.OnTheWay => "🛵",
        OrderStatus.Delivered => "🎉",
        _ => "❌",
    };

    // ───────────────────────────── Cancel ─────────────────────────────

    /// <summary>Exact "MF-1023" match first, then suffix, then substring.</summary>
    private static OrderDto? FindByNumber(List<OrderDto> orders, string digits) =>
        orders.FirstOrDefault(o => o.Number.Equals($"MF-{digits}", StringComparison.OrdinalIgnoreCase))
        ?? orders.FirstOrDefault(o => o.Number.EndsWith(digits, StringComparison.OrdinalIgnoreCase))
        ?? orders.FirstOrDefault(o => o.Number.Contains(digits, StringComparison.OrdinalIgnoreCase));

    private async Task CancelFlowAsync(string? explicitNumber = null)
    {
        if (!SignedInOr()) return;
        var fetched = await api.GetMyOrdersAsync(0, 20);
        if (fetched is null) { Bot(TT("common.wentWrong")); return; }
        var orders = fetched
            .Where(o => o.Status == OrderStatus.Pending)
            .OrderByDescending(o => o.PlacedAt)
            .ToList();
        if (explicitNumber is not null)
            orders = FindByNumber(orders, explicitNumber) is { } m ? [m] : [];

        if (orders.Count == 0)
        {
            _step = Step.Idle;
            Bot(TT("bot.cancelNone"));
            return;
        }
        if (orders.Count == 1)
        {
            await StartCancelAsync(orders[0].Id);
            return;
        }

        _options = orders
            .Select(o => new Option(OptKind.Order, $"{o.Number} · {o.RestaurantName}", $"{o.Number} {o.RestaurantName}", o))
            .ToList();
        _step = Step.PickCancelOrder;
        _messages.Add(new BotMessage
        {
            FromBot = true,
            Text = $"{TT("bot.pickOrder")}\n{TT("bot.pickHint")}",
            OptionCards = orders.Select((o, i) => OrderCard(i + 1, o)).ToList(),
        });
    }

    private async Task StartCancelAsync(int orderId)
    {
        var order = await api.GetOrderAsync(orderId);
        if (order is null || order.Status != OrderStatus.Pending)
        {
            _step = Step.Idle;
            Bot(TT("bot.cancelNone"));
            return;
        }
        _cancelOrderId = order.Id;
        _cancelOrderNumber = order.Number;
        _step = Step.ConfirmCancel;
        Bot(TT("bot.cancelConfirm", order.Number, SN(order.RestaurantName)),
            Chip(TT("bot.chipYes"), "sys:yes"), Chip(TT("bot.chipNo"), "sys:no"));
    }

    // ───────────────────────────── Reorder ─────────────────────────────

    private async Task ReorderFlowAsync()
    {
        if (!SignedInOr()) return;
        var fetched = await api.GetMyOrdersAsync(0, 10);
        if (fetched is null) { Bot(TT("common.wentWrong")); return; }
        var orders = fetched.OrderByDescending(o => o.PlacedAt).ToList();
        if (orders.Count == 0)
        {
            _step = Step.AwaitQuery;
            Bot(TT("bot.reorderNone"));
            return;
        }

        // The basket holds several restaurants now, so a reorder just adds to it.
        await DoReorderAsync(orders[0]);
    }

    private async Task DoReorderAsync(OrderDto last)
    {
        var detail = await api.GetRestaurantAsync(last.RestaurantId);
        if (detail is null || !detail.Info.IsOpen)
        {
            Bot(TT("bot.closedNow", SN(last.RestaurantName)), Chip(TT("bot.chipOrder"), "act:order"));
            return;
        }

        var menu = detail.Categories.SelectMany(c => c.Items).ToList();
        var missing = new List<string>();
        var added = 0;
        foreach (var oi in last.Items)
        {
            var item = menu.FirstOrDefault(m => BotNlu.Fold(m.Name) == BotNlu.Fold(oi.Name) && m.IsAvailable);
            if (item is null)
            {
                // The dish may have been renamed since the order — resolve the
                // old name through the keyword search instead of giving up.
                var hits = await api.SearchAsync(oi.Name, realOnly: true);
                var ids = hits?.Dishes
                    .Where(d => d.RestaurantId == last.RestaurantId)
                    .Select(d => d.MenuItemId)
                    .ToHashSet() ?? [];
                item = menu.FirstOrDefault(m => ids.Contains(m.Id) && m.IsAvailable);
            }
            if (item is null) { missing.Add(oi.Name); continue; }
            cart.Add(detail.Info, item);
            var line = cart.Lines.First(l => l.Item.Id == item.Id);
            for (var i = 1; i < oi.Quantity; i++) cart.Increment(line);
            added++;
        }

        if (added == 0)
        {
            Bot(TT("bot.noResults", SN(last.RestaurantName)));
            return;
        }
        _step = Step.Idle;
        var text = TT("bot.reordered", last.Number, cart.Count, Cash(cart.Subtotal));
        if (missing.Count > 0) text += "\n" + TT("bot.reorderMissing", string.Join(", ", missing));
        Bot(text,
            Chip(TT("bot.chipCheckout"), "act:checkout"),
            Chip(TT("bot.chipCart"), "act:cart"));
    }

    // ───────────────────────────── Yes / No ─────────────────────────────

    private async Task HandleYesNoAsync(bool yes)
    {
        switch (_step)
        {
            case Step.ConfirmSwitch:
                if (yes)
                {
                    cart.Clear();
                    _couponCode = null;              // validated against the old basket
                    _discount = 0;
                    if (_pendingReorder is { } reorder)
                    {
                        _pendingReorder = null;
                        _step = Step.Idle;
                        await DoReorderAsync(reorder);
                    }
                    else if (_pendingItem is not null && _pendingRestaurant is not null)
                    {
                        if (_pendingQty is { } q) await HandleQuantityAsync(q);
                        else
                        {
                            _step = Step.PickQuantity;
                            _messages.Add(new BotMessage
                            {
                                FromBot = true,
                                Text = TT("bot.howMany", PN(_pendingItem.Name)),
                                QtyPicker = true,
                            });
                        }
                    }
                }
                else
                {
                    _pendingItem = null;
                    _pendingQty = null;
                    _pendingReorder = null;
                    _step = Step.PickResult;
                    Bot(TT("bot.keptCart", cart.Restaurant?.Name ?? ""),
                        Chip(TT("bot.chipCheckout"), "act:checkout"),
                        Chip(TT("bot.chipCart"), "act:cart"));
                }
                break;

            case Step.ConfirmOrder:
                if (yes) await PlaceOrderAsync();
                else
                {
                    _couponCode = null;              // next checkout revalidates
                    _discount = 0;
                    _step = Step.Idle;
                    Bot(TT("bot.orderKept"),
                        Chip(TT("bot.chipCart"), "act:cart"),
                        Chip(TT("bot.chipMore"), "act:more"));
                }
                break;

            case Step.ConfirmCancel:
                if (yes)
                {
                    var (ok, error) = await api.CancelOrderAsync(_cancelOrderId);
                    Bot(ok ? TT("bot.cancelled", _cancelOrderNumber) : TT("bot.cancelFailed", error ?? ""));
                }
                else Bot(TT("bot.orderKept"));
                _step = Step.Idle;
                break;

            case Step.ConfirmClear:
                if (yes)
                {
                    cart.Clear();
                    _couponCode = null;
                    _discount = 0;
                    Bot(TT("bot.cleared"), Chip(TT("bot.chipOrder"), "act:order"));
                }
                else ShowCart();
                _step = Step.Idle;
                break;

            default:
                Bot(TT("bot.fallback"), Chip(TT("bot.chipHelp"), "act:help"));
                break;
        }
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private void ShowHelp() =>
        Bot(TT("bot.help"),
            Chip(TT("bot.chipOrder"), "act:order"),
            Chip(TT("bot.chipTrack"), "act:track"),
            Chip(TT("bot.chipCart"), "act:cart"),
            Chip(TT("bot.chipReorder"), "act:reorder"));

    private void Bot(string text, params BotChip[] chips) =>
        _messages.Add(new BotMessage { FromBot = true, Text = text, Chips = [.. chips] });

    /// <summary>
    /// Changes the whole app's language on request. The confirmation is written in the
    /// NEW language — the first proof the change landed — and this conversation's sticky
    /// reply language is pinned to it, so the next message stays where the customer just
    /// put it even if it carries no language signal of its own.
    /// </summary>
    private async Task SwitchLanguageAsync(string code)
    {
        var already = lang.Locale == code;
        if (!already) await lang.SetLocaleAsync(code);
        _replyLocale = code;
        Bot(string.Format(
            LanguageService.Translate(code, already ? "bot.langAlready" : "bot.langNow"),
            LanguagePick.NativeName(code)));
    }

    private static BotChip Chip(string label, string payload) => new(label, payload);

    /// <summary>
    /// Orders belong to an account. A guest asking "where is my order?" used to get
    /// "Something went wrong" (the API answers 401); now they are told to sign in.
    /// </summary>
    private bool SignedInOr()
    {
        if (state.IsAuthenticated) return true;
        _step = Step.Idle;
        Bot(TT("auth.signInToContinue"), Chip(TT("bot.chipOrder"), "act:order"), Chip(TT("bot.chipHelp"), "act:help"));
        return false;
    }

    /// <summary>
    /// A product name in the language THIS conversation is answering in — which is the
    /// language the customer types, not the one the app is set to.
    /// </summary>
    private string PN(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var key = $"product.{name.Trim()}";
        var translated = LanguageService.Translate(_replyLocale ?? lang.Locale, key);
        return translated == key ? name : translated;
    }

    /// <summary>A store name in the conversation's language (see <see cref="PN"/>).</summary>
    private string SN(string? name) => StoreText.Name(name, _replyLocale ?? lang.Locale);

    /// <summary>A cuisine name in the conversation's language (see <see cref="PN"/>).</summary>
    private string CN(string? cuisine)
    {
        if (string.IsNullOrWhiteSpace(cuisine)) return "";
        var key = $"cuisine.{cuisine.Trim()}";
        var translated = LanguageService.Translate(_replyLocale ?? lang.Locale, key);
        return translated == key ? cuisine : translated;
    }

    /// <summary>
    /// Money for a chat SENTENCE. The isolate characters keep "0.400 OMR" reading
    /// left-to-right inside an Arabic or Persian line; without them the bidi algorithm
    /// drags the bracket or full stop to the wrong end — "(OMR 0.400)." came out
    /// with the stop leading the line.
    /// </summary>
    private static string Cash(decimal value) => $"⁨{Fmt.Money(value)}⁩";

    /// <summary>Localized text in the user's typing language, formatted safely.</summary>
    private string TT(string key, params object[] args)
    {
        var template = LanguageService.Translate(_replyLocale ?? lang.Locale, key);
        if (args.Length == 0) return template;
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }

    /// <summary>A message as the model should read it: the text plus its cards, numbered.</summary>
    private static string HistoryText(BotMessage m)
    {
        if (m.OptionCards.Count == 0) return m.Text;
        var cards = m.OptionCards.Select(c =>
            $"{c.Number}. {c.Title}{(string.IsNullOrWhiteSpace(c.Subtitle) ? "" : " (" + c.Subtitle + ")")}{(string.IsNullOrWhiteSpace(c.Price) ? "" : " — " + c.Price)}{(c.Venue is null ? "" : " @ " + c.Venue)}");
        return (m.Text + "\n" + string.Join("\n", cards)).Trim();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
