using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The language model behind the customer assistant. The rule-based bot in the browser
/// handles ordering itself; when it cannot make sense of a line it asks here, and Grok
/// answers with the platform and the store's real menu in front of it.
/// </summary>
public class AssistantController(AppDbContext db, CatalogStore catalog, GrokAssistant grok, IMemoryCache cache, IConfiguration config)
    : ApiControllerBase
{
    private int RealStoreFloor => config.GetValue("Seo:RealStoreMinId", 5_000_200);

    /// <summary>
    /// Anonymous, like the bot itself — nearly nobody asking it has signed in. 204 means
    /// "no answer from the model" and the browser falls back to its own wording, so a
    /// missing key or an outage never shows as a broken assistant.
    /// </summary>
    [HttpPost("reply")]
    [AllowAnonymous]
    public async Task<IActionResult> Reply(AssistantAskRequest req, CancellationToken ct)
    {
        if (!grok.Enabled) return NoContent();
        var text = (req.Text ?? "").Trim();
        if (text.Length is < 1 or > 1000) return NoContent();

        var session = (req.Session ?? "").Trim();
        if (session.Length is 0 or > 64) session = Guid.NewGuid().ToString("N");

        // One question at a time per sitting, and a ceiling per address per hour: the
        // endpoint is open and every call costs money.
        var gate = $"ask:{session}";
        if (cache.TryGetValue(gate, out _)) return NoContent();
        cache.Set(gate, true, TimeSpan.FromSeconds(1.5));
        var ip = ClientIp(req.Ip);
        var quotaKey = $"ask-quota:{ip}";
        var used = cache.GetOrCreate(quotaKey, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); return new Counter(); })!;
        if (Interlocked.Increment(ref used.Value) > config.GetValue("Grok:MaxPerIpPerHour", 60)) return NoContent();

        var store = await ResolveStoreAsync(req.StoreId, req.Page, ct);
        var (system, perQuestion) = await BuildSystemPromptAsync(store, req.Lang, text, ct);
        if (req.Agent) system += AgentProtocol();
        var now = DateTime.Now;
        var volatileContext = $"Current local date and time: {now:yyyy-MM-dd HH:mm} ({now.DayOfWeek})."
                              + (perQuestion.Length > 0 ? "\n" + perQuestion : "")
                              + (req.Agent ? "\n" + AgentState(req) : "");

        var history = (req.History ?? [])
            .Where(h => !string.IsNullOrWhiteSpace(h.Text))
            .TakeLast(10)
            .Select(h => new AssistantTurnDto(h.Role == "assistant" ? "assistant" : "user", Cap(h.Text, 600)))
            .ToList();

        var reply = await grok.ReplyAsync(system, history, text, ct, volatileContext);
        if (reply is null) return NoContent();
        if (!req.Agent) return Ok(new AssistantReplyDto(Cap(reply, 1500), grok.ModelName));
        var parsed = GrokAssistant.ParseAgentReply(reply, grok.ModelName);
        return Ok(parsed with { Reply = Cap(parsed.Reply, 1500), Query = parsed.Query is null ? null : Cap(parsed.Query, 120) });
    }

    private sealed class Counter { public int Value; }

    /// <summary>
    /// The administrators' data analyst: a question in plain language, answered by Grok
    /// with the platform's own MCP tools in hand (live SQL + Mongo, read-only). Admin only —
    /// the tools see every store's sales and customers.
    /// </summary>
    [HttpPost("analyst")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult<AnalystReplyDto>> Analyst(AnalystAskRequest req, CancellationToken ct)
    {
        var question = (req.Question ?? "").Trim();
        if (question.Length is < 2 or > 4000) return BadRequest(new { message = "Ask a question (2–4000 characters)." });
        var token = (config["Mcp:Token"] ?? "").Trim();
        if (!grok.Enabled) return Ok(new AnalystReplyDto(null, grok.ModelName, [], "Grok is not configured (Grok:ApiKey)."));
        if (token.Length == 0) return Ok(new AnalystReplyDto(null, grok.ModelName, [], "The MCP server is not configured (Mcp:Token)."));

        // Where xAI must reach our MCP server from the internet. Staging sits behind
        // Cloudflare, which carries 443 and 8443 but not 8600 — hence an explicit URL.
        var mcpUrl = string.IsNullOrWhiteSpace(config["Mcp:PublicUrl"]) ? PublicBase() + "/mcp" : config["Mcp:PublicUrl"]!.Trim();
        var now = DateTime.Now;
        var system =
            "You are the OrderOrange data analyst for the platform's administrators. OrderOrange is a food-ordering and shop platform in Oman; money is in Omani rial (OMR, 3 decimals). " +
            "You have the 'orderorange' MCP tools: ALWAYS use them to get real numbers — never guess or invent figures. Start with platform_overview, list_stores or schema when unsure; prefer the typed tools; use sql_query / mongo_find for anything else (read-only). " +
            $"Store ids >= {RealStoreFloor} are real stores; smaller ids are generated demo stores and should be ignored unless asked. Today is {now:yyyy-MM-dd} ({now.DayOfWeek}), local time {now:HH:mm}. " +
            "Answer in the language of the question" + (string.IsNullOrWhiteSpace(req.Lang) ? "" : $" (the admin app is set to '{req.Lang}')") + ". Be concrete and brief: lead with the answer, then the key numbers (a compact table when there are several rows), then one or two observations. Say which period you used. Mention when data is thin or missing instead of padding.";

        var history = (req.History ?? []).Where(h => !string.IsNullOrWhiteSpace(h.Text)).TakeLast(10)
            .Select(h => new AssistantTurnDto(h.Role == "assistant" ? "assistant" : "user", Cap(h.Text, 3000))).ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (text, calls, error) = await grok.AnalystAsync(system, history, question, mcpUrl, token, ct);
        return Ok(new AnalystReplyDto(text, grok.ModelName,
            calls.Select(k => new AnalystToolCallDto(k.Tool, Cap(k.Arguments, 2000), Cap(k.Output, 6000), k.Error)).ToList(),
            error, (int)sw.ElapsedMilliseconds));
    }

    /// <summary>
    /// The partner assistant: a store owner or team member asks about THEIR business, and
    /// Grok answers with store-scoped MCP tools — a short-lived bearer minted here that lets
    /// the model see this one store and act on it (open/closed sign, item availability).
    /// Majed 2026-10-01: "also ai in partner, to grok, and can do everything".
    /// </summary>
    [HttpPost("partner")]
    [Authorize]
    public async Task<ActionResult<AnalystReplyDto>> Partner(AnalystAskRequest req, CancellationToken ct)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        var question = (req.Question ?? "").Trim();
        if (question.Length is < 1 or > 4000) return BadRequest(new { message = "Ask a question (1–4000 characters)." });
        if (!grok.Enabled) return Ok(new AnalystReplyDto(null, grok.ModelName, [], "Grok is not configured (Grok:ApiKey)."));
        if (string.IsNullOrWhiteSpace(config["Mcp:Token"])) return Ok(new AnalystReplyDto(null, grok.ModelName, [], "The MCP server is not configured (Mcp:Token)."));

        var store = await db.Restaurants.Include(r => r.Cuisine).AsNoTracking().FirstOrDefaultAsync(r => r.Id == storeId, ct);
        if (store is null) return Forbid();

        var writes = config.GetValue("Mcp:PartnerWrites", true);
        var token = Mcp.McpEndpoint.MintStoreToken(config, storeId, writes, TimeSpan.FromMinutes(10));
        var mcpUrl = string.IsNullOrWhiteSpace(config["Mcp:PublicUrl"]) ? PublicBase() + "/mcp" : config["Mcp:PublicUrl"]!.Trim();
        var now = DateTime.Now;
        var p = PartnerBase();
        var system =
            $"You are the OrderOrange partner assistant working for {store.Name} (store id {storeId}, {store.Cuisine?.Name}, {store.Area}; currently {(store.IsOpen ? "OPEN" : "CLOSED")} for orders). " +
            "The person talking to you runs this store (owner or team). You have the 'orderorange' MCP tools, scoped to this store only: get_store, get_menu, search_dishes, list_orders, get_order, sales_report, top_items, customers (includeContact allowed — it is their own customer list), reservations, inventory, staff, assistant_chats" +
            (writes ? ", and two ACTIONS: set_store_open (open/close the store for orders) and set_item_availability (mark a menu item sold out or available). When they ask you to do one of these, DO IT with the tool — do not just describe how — then confirm exactly what changed. For anything else that changes data (prices, new items, orders, bookings, staff) you cannot do it yourself: say so and point to the page." : ". You cannot change anything; point to the page where they can.") +
            " ALWAYS use the tools for numbers — never guess. Money is in Omani rial (OMR, 3 decimals). " +
            $"Today is {now:yyyy-MM-dd} ({now.DayOfWeek}), local time {now:HH:mm}. Answer in the language of the message" + (string.IsNullOrWhiteSpace(req.Lang) ? "" : $" (the app is set to '{req.Lang}')") +
            ". Be brief and concrete: the answer first, then the key numbers (a compact table when there are several rows), then one useful observation. Say which period you used.\n\n" +
            PartnerGuide(p);

        var history = (req.History ?? []).Where(h => !string.IsNullOrWhiteSpace(h.Text)).TakeLast(8)
            .Select(h => new AssistantTurnDto(h.Role == "assistant" ? "assistant" : "user", Cap(h.Text, 2000))).ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var reasoning = string.IsNullOrWhiteSpace(config["Grok:PartnerReasoning"]) ? "low" : config["Grok:PartnerReasoning"]!.Trim();
        var (text, calls, error) = await grok.AnalystAsync(system, history, question, mcpUrl, token, ct, reasoning);
        return Ok(new AnalystReplyDto(text, grok.ModelName,
            calls.Select(k => new AnalystToolCallDto(k.Tool, Cap(k.Arguments, 1000), Cap(k.Output, 3000), k.Error)).ToList(),
            error, (int)sw.ElapsedMilliseconds));
    }

    private static readonly string[] NameLanguages = ["en", "ar", "fa", "ur", "hi", "tr", "de", "fr", "es", "it", "pt", "ru", "ja", "zh"];

    /// <summary>
    /// Translates one short name (a menu group, a dish) into the app's languages (Majed
    /// 2026-10-13: "in product group we need all language"). Partner sign-in only, at most
    /// 120 calls an hour per store; returns only the languages it was asked for.
    /// </summary>
    [HttpPost("translate")]
    [Authorize]
    public async Task<ActionResult<Dictionary<string, string>>> Translate(TranslateNamesRequest req, CancellationToken ct)
    {
        var storeId = CurrentRestaurantId;
        if (storeId <= 0) return Forbid();
        var text = (req.Text ?? "").Trim();
        if (text.Length is < 1 or > 200) return BadRequest(new { message = "Enter a name first (up to 200 characters)." });
        var targets = (req.To ?? []).Select(c => (c ?? "").Trim().ToLowerInvariant()).Where(NameLanguages.Contains).Distinct().ToList();
        if (targets.Count == 0) return Ok(new Dictionary<string, string>());
        if (!grok.Enabled) return BadRequest(new { message = "Translation is not available right now." });

        var key = $"translate:{storeId}:{DateTime.UtcNow:yyyyMMddHH}";
        var used = cache.GetOrCreate(key, e => { e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1); return new int[1]; })!;
        if (Interlocked.Increment(ref used[0]) > 120) return StatusCode(429, new { message = "Too many translations this hour. Try again later." });

        var from = NameLanguages.Contains((req.From ?? "").Trim().ToLowerInvariant()) ? req.From!.Trim().ToLowerInvariant() : "auto";
        var system =
            "You translate short names for a restaurant / shop menu (a menu section or a product). " +
            "Reply with ONLY one JSON object, no prose and no code fence, mapping each requested language code to the translation. " +
            "Rules: keep it as short as a menu heading; keep brand names and dish names that are normally not translated (pizza, shawarma, latte, karak) " +
            "in the natural local form or script used on menus in that language; use the correct script for each language " +
            "(fa = Persian letters ی ک, ur = Urdu, ar = Arabic, hi = Devanagari, ja = Japanese, zh = Simplified Chinese); no quotes inside values.";
        var prompt = $"Source language: {from}. Name: \"{text}\". Languages: {string.Join(", ", targets)}.";
        var reply = await grok.ReplyAsync(system, [], prompt, ct);
        if (string.IsNullOrWhiteSpace(reply)) return BadRequest(new { message = "Translation is not available right now." });

        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start) return BadRequest(new { message = "Translation is not available right now." });
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(reply[start..(end + 1)]);
            var result = new Dictionary<string, string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var code = prop.Name.Trim().ToLowerInvariant();
                if (!targets.Contains(code) || prop.Value.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                var value = (prop.Value.GetString() ?? "").Trim();
                if (value.Length is > 0 and <= 120) result[code] = value;
            }
            return Ok(result);
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(new { message = "Translation is not available right now." });
        }
    }

    /// <summary>The partner app, page by page, so "where do I change my hours?" gets a link.</summary>
    private static string PartnerGuide(string p) => $"""
        PARTNER APP GUIDE (the app this person is using, {p}; give exact links when they ask where something is):
        - {p}/ Dashboard: today's sales, live orders, quick links. {p}/orders live order board (accept, prepare, ready, hand over, cancel). {p}/pos the till (tables, walk-ins, pay, print); {p}/pos-tablet for tablets; {p}/kds kitchen display.
        - Tables & dine-in: {p}/tables floor plan and salons; {p}/table-qr printable table QR cards; {p}/qr the store's own QR/short link; {p}/table-chats messages from guests at tables; {p}/reservations bookings (confirm, seat, cancel).
        - Menu: {p}/menu products and categories (add/edit items, prices, photos, availability, in-store-only, ordering windows, discounts); {p}/labels price labels.
        - Inventory: {p}/materials raw materials and stock; {p}/materials/purchases purchases and supplier invoices; {p}/materials/payments; {p}/materials/suppliers; {p}/materials/recipes (ingredients per dish); {p}/materials/report; {p}/materials/capacity; {p}/waste write-offs; {p}/warehouses stock map between branches.
        - People: {p}/staff team and salaries; {p}/staff/payroll; {p}/staff/salary-report; {p}/attendance clock in/out and leave; {p}/attendance/report; {p}/team-users logins; {p}/team-roles permissions; {p}/team-activity; {p}/chat team chat.
        - Money & reports: {p}/invoices all invoices (cancel, edit-as-replace, PDF); {p}/invoice-design receipt design; {p}/bills platform bills; {p}/daily-report; {p}/reports sales reports; {p}/reports/products; {p}/reports/customers; {p}/customers customer list; {p}/reviews ratings.
        - Marketing: {p}/discounts promo codes and rules; {p}/loyalty points and rewards; {p}/surveys questionnaires; {p}/instagram connect and post, {p}/instagram/schedule planner; {p}/contracts supply agreements; {p}/visits store-page visits.
        - {p}/settings store profile, open/closed sign, opening hours, delivery fee and minimum, pickup, self-delivery, online table booking, tax, logo, languages, currency symbol, kitchen language, print routing ({p}/print-routing). {p}/support help desk tickets. {p}/add-business another branch.

        {PrintingGuide(p)}
        """;

    /// <summary>
    /// Printers and the two till apps (Majed 2026-10-24: "update ai about new printer in
    /// android and windows and also about two app when user ask about print"). Shared by the
    /// partner assistant and the public chat, where shop owners ask too.
    /// </summary>
    private static string PrintingGuide(string p) => $"""
        PRINTING (when someone asks about a printer, receipts, kitchen or bar tickets, or "how do I print"):
        - The web till {p}/pos prints the customer's receipt through the device's normal print dialog. Automatic printing (the bill and the kitchen/bar tickets come out by themselves the moment an order arrives, from the app, the website, table QR or the till) needs one of the TWO free OrderOrange till apps. Both are downloaded from {p}/settings (the "Till software for Windows" and "Till app for Android" cards, which also show the current version), and both sign in with the partner account.
        - WINDOWS TILL (for a counter PC, Windows 10 or later): run the downloaded setup; it installs itself with Start-menu and desktop shortcuts and updates itself. It uses any printer installed in Windows (USB, network or Bluetooth with its Windows driver). Under "Print stations" add a station per printer, give it a role (Cashier for bills, Kitchen, Bar) and the number of copies, then "Test print". "Products → printers" decides which dishes go to which printer (tick a whole category at once). Switch on "Automatic printing" so new orders print by themselves.
        - ANDROID TILL (for an Android POS terminal such as Sunmi/E-POS, or an Android tablet/phone, Android 8 or later): download the APK, allow installing from this source, open it and sign in. Printers → "Find printers" finds the terminal's built-in printer (shown as "InnerPrinter" or Built-in; best for the cashier receipt, 58 mm), Bluetooth printers paired in Android Settings, USB printers and Wi-Fi/LAN printers on port 9100 (best for kitchen and bar, 80 mm); "Use" fills in the printer, then "Test print". "Product printers" on the home screen routes dishes to printers. It keeps running in the background: when the app asks, allow it to ignore battery optimisation, and on some phones also mark it "auto-start".
        - Both apps use the same designs and rules set on the website: the receipt design at {p}/invoice-design, the kitchen-ticket language in {p}/settings, and Kitchen / Bar / both per product at {p}/print-routing. Paper is 80 mm or 58 mm thermal; ESC/POS receipt printers work best.
        - Nothing prints? Check: the till app is open and signed in to the right store; the printer is on, has paper and passes "Test print"; for a Wi-Fi printer, the till and the printer are on the same network; on Android, the app is allowed to run in the background (the Android till retries a failed print by itself and shows a red banner until it works). If it still fails, open a ticket at {p}/support with the printer model and a photo.
        """;

    private string PublicBase()
    {
        var pub = config["Api:PublicBase"] ?? config["Media:PublicBase"];
        return string.IsNullOrWhiteSpace(pub) ? $"{Request.Scheme}://{Request.Host}" : pub.TrimEnd('/');
    }

    /// <summary>
    /// Agent mode: the model steers the ordering engine. It answers with one JSON object
    /// naming an action; the browser runs the action (search cards, pick, quantity,
    /// tracking…) and shows the reply text.
    /// </summary>
    private static string AgentProtocol()
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("You work together with a small ordering engine in this chat. Respond ONLY with one JSON object, no other text, of this shape:");
        sb.AppendLine("{\"reply\": string, \"action\": \"none\"|\"search\"|\"menu\"|\"stores\"|\"near\"|\"pick\"|\"qty\"|\"remove\"|\"keep\"|\"track\"|\"cancel\"|\"cart\"|\"checkout\"|\"reorder\"|\"clear\"|\"help\", \"query\": string, \"scope\": \"store\"|\"all\", \"option\": number, \"qty\": number, \"url\": string, \"lang\": string}");
        sb.AppendLine("Basket editing — you CAN do it: \"remove\" takes one item (or a quantity of it, e.g. query \"1 doogh\") out of the basket; \"keep\" removes everything EXCEPT the item named in \"query\" ('keep only the doogh', 'just the kabab'); \"clear\" empties it; \"cart\" shows it. Put the item words in \"query\" in the language the basket shows; never say you cannot edit the basket.");
        sb.AppendLine("\"stores\": the customer asks for the list of restaurants/stores, 'other restaurants', 'what else is there', or stores of a kind or area — the engine shows tappable STORE cards (tap opens that store's menu); put a filter word in \"query\" (an area, a cuisine like 'pizza' or 'Iranian', a store type) or leave it empty for all; keep \"reply\" to one line. Use it even on a store's page when they explicitly ask for other stores.");
        sb.AppendLine("\"near\" (no store in context): the customer asks for nearby / close / around-me restaurants or what to eat near them — the engine asks the browser for their location (or for their area) and lists the nearest stores; keep \"reply\" to one line. If they NAME an area ('Al Ghubrah', 'Seeb'), use \"search\" with scope \"all\" and the area name as the query instead. Never answer a 'near me' request with a random list of dishes.");
        sb.AppendLine("\"menu\" (store page only): shows this store's menu as PHOTO cards the customer can tap — put a category name in \"query\" to show one category, leave it empty for the highlights. Use it for 'show me the menu', 'with pictures', 'what's in the drinks section', 'show me your kababs'; your \"reply\" is printed above the cards.");
        sb.AppendLine("\"scope\" applies to search: \"store\" (default) searches only the store whose page the customer is on; use \"all\" ONLY when the customer explicitly asks about other restaurants/stores or the whole platform.");
        sb.AppendLine("\"url\": optional — when your answer points the customer to a page of this site (sign-in, orders, checkout, a store page, the partner site…), put that full link or site path here and the chat shows an 'Open' button for it.");
        sb.AppendLine("\"lang\": ALWAYS set — the ISO 639-1 code of the language the customer wrote THIS message in (fa for Persian, ar for Arabic, en, ur, hi, tr, de, fr, es, it, pt, ru, ja, zh). Persian and Arabic share letters: decide from words and digits (e.g. «تا», «عدد», «می‌خوام», «چی» are Persian; «كم», «أبغى», «شو» are Arabic); a bare number keeps the conversation's current language. The engine prints its own lines in this language.");
        sb.AppendLine("\"pick\" may carry \"qty\" too: 'the second one, 2 of them' or «۲ عدد» when one item is on screen → pick option 1 with qty 2.");
        sb.AppendLine("Actions: \"search\" when the customer wants to see, find or order a dish, product, store or category — put the words to look up in \"query\" (just the dish/store words, no quantity, no filler; use the English or canonical menu name when you know it); the engine shows tappable cards, so keep \"reply\" to one short line or empty. \"pick\" when they choose one item from the numbered list on screen (by number, name, description, 'the cheapest', 'the second one') — put its number in \"option\". \"qty\" when the engine asked how many and they answered — put the number in \"qty\". \"track\" for where-is-my-order, \"cancel\" to cancel an order, \"cart\" to show the basket, \"checkout\" to pay/finish, \"reorder\" to repeat the last order, \"clear\" to empty the basket, \"help\" for help. \"none\" for questions, recommendations and conversation — then \"reply\" carries the whole answer, in the customer's language, one to three sentences.");
        sb.AppendLine("Never claim an order was placed; the engine does that after the customer confirms. If the customer names a dish AND a quantity, use \"search\" with the dish words only (the engine will ask how many).");
        sb.AppendLine("You are the voice of this chat: with \"search\", your \"reply\" is printed as the sentence ABOVE the cards, so write it naturally in the customer's language and tone (e.g. 'Here are our kababs — tap one to add it'). For 'what do you have?', 'what's on the menu?', 'what do you recommend?' on a store page use action \"menu\" (cards with photos) with a one-line reply naming the categories; off a store page, answer YOURSELF (action \"none\") with the stores and 3–6 concrete dishes with prices from the data, then invite them to name one. Understand typos and dialect (e.g. Persian 'جی داری' = 'چی داری' = what do you have). Never answer a broad question with a single random item.");
        return sb.ToString();
    }

    /// <summary>The part that changes every turn — kept out of the cacheable block.</summary>
    private static string AgentState(AssistantAskRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Engine step now: {Describe(req.Step)}.");
        if (req.Options is { Count: > 0 })
        {
            sb.AppendLine("Numbered list on screen:");
            foreach (var (o, i) in req.Options.Take(12).Select((o, i) => (o, i))) sb.AppendLine($"{i + 1}. {Cap(o, 120)}");
        }
        sb.AppendLine(string.IsNullOrWhiteSpace(req.Cart) ? "Basket: empty." : "Basket:\n" + Cap(req.Cart, 800));
        return sb.ToString();
    }

    private static string Describe(string? step) => (step ?? "").ToLowerInvariant() switch
    {
        "pick-result" => "a numbered list of search results is on screen and the engine waits for the customer to pick one (or ask something else)",
        "pick-quantity" => "the engine asked how many of the chosen item they want",
        "await-query" => "the engine asked what they would like to eat",
        "confirm-order" => "the engine showed the final bill and waits for yes/no",
        "" or "idle" => "idle",
        var s => s,
    };

    // ───────────────────────────── Context ─────────────────────────────

    /// <summary>
    /// The store the customer is looking at: an explicit id, or read off the page path —
    /// /restaurant/5000226, /reserve/5000216-…, or the short /slug link.
    /// </summary>
    private async Task<Models.Restaurant?> ResolveStoreAsync(int? storeId, string? page, CancellationToken ct)
    {
        var id = storeId ?? 0;
        var path = (page ?? "").Trim();
        if (id == 0 && path.Length > 1)
        {
            var m = Regex.Match(path, @"^/(?:restaurant|store|shop|reserve-table|reserve|p|r)/(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            else
            {
                var slug = Regex.Match(path, @"^/([a-z0-9][a-z0-9-]{1,60})(?:[/?#]|$)", RegexOptions.IgnoreCase);
                if (slug.Success)
                {
                    var handle = slug.Groups[1].Value.ToLowerInvariant();
                    var bySlug = await db.Restaurants.Include(r => r.Cuisine).AsNoTracking()
                        .FirstOrDefaultAsync(r => r.IsApproved && r.Slug == handle, ct);
                    if (bySlug is not null) return bySlug;
                }
            }
        }
        if (id <= 0) return null;
        return await db.Restaurants.Include(r => r.Cuisine).AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.IsApproved, ct);
    }

    /// <returns>
    /// The STABLE prompt (identical from turn to turn for the same store and language, so
    /// the model's prefix cache hits) and the per-question extras that depend on the text.
    /// </returns>
    private async Task<(string Stable, string PerQuestion)> BuildSystemPromptAsync(Models.Restaurant? store, string? lang, string text, CancellationToken ct)
    {
        var extra = new StringBuilder();
        var sb = new StringBuilder();
        sb.AppendLine("You are the OrderOrange assistant, a friendly, concise helper inside the OrderOrange food-ordering website (orderorange.com) in Oman.");
        sb.AppendLine("OrderOrange lets customers browse restaurants and shops, order food for delivery or pickup, pay by cash on delivery or card, track and cancel orders, book tables and scan a table QR to order at the table. Prices are in Omani rial (OMR).");
        sb.AppendLine("Rules: answer in the language the customer writes in" + (string.IsNullOrWhiteSpace(lang) ? "" : $" (the site is currently set to '{lang}')") + ". Keep answers short: one to three sentences, plain text, no markdown, no headings, no bullet lists. Never invent dishes, prices, phone numbers or opening hours — use only the data below; if something is not listed, say you are not sure and suggest searching the menu or contacting the store. Do not repeat the customer's question back. Do not claim to have placed, changed or cancelled an order: ordering is done by tapping the dishes in this chat or on the menu page.");
        sb.AppendLine("The chat has buttons for ordering, tracking and the basket; you may tell the customer to type a dish name to see it, or to use the 'Order' button.");

        sb.AppendLine();
        sb.AppendLine(SiteGuide());

        if (store is not null)
        {
            sb.AppendLine();
            sb.AppendLine("The customer is on this store's page. This store is the MAIN and only store for this conversation: answer from its menu, recommend its dishes and prices, and never bring in other stores unless the customer explicitly asks about other restaurants or the whole platform.");
            sb.AppendLine($"Store: {store.Name} ({store.Cuisine?.Name}), area: {store.Area}, {(store.IsOpen ? "OPEN now" : "CLOSED right now")}, delivery fee {Money(store.DeliveryFee)}, minimum order {Money(store.MinOrder)}, typical preparation {store.AvgPrepMinutes} min, pickup {(store.AllowsPickup ? "allowed" : "not offered")}, online table booking {(store.OnlineReservations ? "available" : "not offered")}.");
            if (!string.IsNullOrWhiteSpace(store.Description)) sb.AppendLine($"About: {Cap(store.Description, 400)}");
            if (!string.IsNullOrWhiteSpace(store.Phone)) sb.AppendLine($"Phone: {store.Phone}");
            if (!string.IsNullOrWhiteSpace(store.Whatsapp)) sb.AppendLine($"WhatsApp: {store.Whatsapp}");

            var menu = await catalog.PublicMenuAsync(store.Id);
            var count = 0;
            sb.AppendLine("Menu (category: item — price OMR; 'off' = not available today):");
            foreach (var cat in menu)
            {
                foreach (var item in cat.Items)
                {
                    if (count++ >= 160) break;
                    var price = item.DiscountPercent > 0
                        ? $"{Money(item.Price * (1 - item.DiscountPercent / 100m))} (was {Money(item.Price)})"
                        : Money(item.Price);
                    sb.Append(cat.Name).Append(": ").Append(item.Name);
                    var alt = item.Names?.Values.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && n != item.Name);
                    if (alt is not null) sb.Append(" / ").Append(alt);
                    sb.Append(" — ").Append(price);
                    if (!item.IsAvailable) sb.Append(" off");
                    if (item.IsPopular) sb.Append(" ★popular");
                    sb.AppendLine();
                }
                if (count >= 160) { sb.AppendLine("(menu truncated)"); break; }
            }
            if (count == 0) sb.AppendLine("(this store has no public menu yet)");

            // The rest of the platform, for the one case the customer EXPLICITLY asks for it
            // ("list the restaurants", "what other places are there") — Majed 2026-10-01: the
            // assistant on TTalk's page claimed it had no such list.
            var others = await db.Restaurants.Include(r => r.Cuisine).AsNoTracking()
                .Where(r => r.IsApproved && r.Id >= RealStoreFloor && r.Id != store.Id)
                .OrderBy(r => r.SuggestedOrder == null).ThenBy(r => r.SuggestedOrder).ThenBy(r => r.Name)
                .Take(40).ToListAsync(ct);
            sb.AppendLine();
            sb.AppendLine("Other stores on the platform — mention them ONLY when the customer explicitly asks for other restaurants/stores or the full list (name — type — area — link); otherwise stay with this store:");
            foreach (var r in others)
                sb.AppendLine($"{r.Name} — {r.Cuisine?.Name} — {r.Area} — {(string.IsNullOrWhiteSpace(r.Slug) ? $"/restaurant/{r.Id}" : "/" + r.Slug)}");
        }
        else
        {
            // No store page: the platform's real, approved shops — the catalogue is small
            // enough to hand over whole, so "which restaurants do you have?" has an answer.
            var stores = await db.Restaurants.Include(r => r.Cuisine).AsNoTracking()
                .Where(r => r.IsApproved && r.Id >= RealStoreFloor)
                .OrderBy(r => r.SuggestedOrder == null).ThenBy(r => r.SuggestedOrder).ThenBy(r => r.Name)
                .Take(40).ToListAsync(ct);
            sb.AppendLine();
            sb.AppendLine("Stores on the platform (name — type — area — open/closed — short link):");
            foreach (var r in stores)
                sb.AppendLine($"{r.Name} — {r.Cuisine?.Name} — {r.Area} — {(r.IsOpen ? "open" : "closed")} — {(string.IsNullOrWhiteSpace(r.Slug) ? $"/restaurant/{r.Id}" : "/" + r.Slug)}");

            var names = stores.ToDictionary(s => s.Id, s => s.Name);
            var snapshot = await MenuSnapshotAsync(stores.Select(s => s.Id).ToList());

            // A taste of every menu, so "what do you sell?" and "what can I get for 5
            // rials?" have real names and prices behind them.
            sb.AppendLine("Sample of each store's menu (item — price OMR — store), popular first then cheapest:");
            foreach (var group in snapshot.GroupBy(i => i.StoreId))
                foreach (var i in group.OrderByDescending(i => i.Popular).ThenBy(i => i.Price).Take(8))
                    sb.AppendLine($"{i.Name} — {Money(i.Price)} — {names.GetValueOrDefault(i.StoreId, "")}");

            // A number in the question is most likely a budget: list what fits under it.
            // Persian and Arabic digits ("۵ ریال") must count as numbers too.
            var ascii = new string(text.Select(ch => ch is >= '۰' and <= '۹' ? (char)('0' + (ch - '۰')) : ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch).ToArray());
            var budget = Regex.Matches(ascii, @"[0-9]+(?:[.,][0-9]+)?").Select(m => decimal.TryParse(m.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m).FirstOrDefault(v => v > 0);
            if (budget > 0)
            {
                extra.AppendLine($"Items priced at or under {Money(budget)} OMR (item — price — store), cheapest first:");
                foreach (var i in snapshot.Where(i => i.Price <= budget).OrderBy(i => i.Price).Take(50))
                    extra.AppendLine($"{i.Name} — {Money(i.Price)} — {names.GetValueOrDefault(i.StoreId, "")}");
            }

            // Dishes that match what was typed, from the real stores only.
            var hits = await catalog.SearchAsync(Cap(text, 60), 40);
            var real = hits.Where(h => h.RestaurantId >= RealStoreFloor && h.IsAvailable).Take(20).ToList();
            if (real.Count > 0)
            {
                extra.AppendLine("Dishes matching the question (item — price OMR — store):");
                foreach (var h in real)
                    extra.AppendLine($"{h.Name} — {Money(h.Price)} — {names.GetValueOrDefault(h.RestaurantId, "store " + h.RestaurantId)}");
            }
        }
        return (sb.ToString(), extra.ToString().TrimEnd());
    }

    private sealed record SnapItem(int StoreId, string Name, decimal Price, bool Popular);

    /// <summary>
    /// Every real store's public, available menu as flat name/price rows — a dozen Mongo
    /// reads, so it is kept for five minutes rather than rebuilt on every question.
    /// </summary>
    private async Task<List<SnapItem>> MenuSnapshotAsync(List<int> storeIds)
    {
        var key = "assistant-menu-snapshot";
        if (cache.TryGetValue(key, out List<SnapItem>? cached) && cached is not null) return cached;
        var rows = new List<SnapItem>();
        foreach (var id in storeIds)
        {
            var menu = await catalog.PublicMenuAsync(id);
            foreach (var cat in menu)
                foreach (var item in cat.Items.Where(i => i.IsAvailable))
                {
                    var price = item.DiscountPercent > 0 ? item.Price * (1 - item.DiscountPercent / 100m) : item.Price;
                    rows.Add(new SnapItem(id, item.Name, price, item.IsPopular));
                }
        }
        cache.Set(key, rows, TimeSpan.FromMinutes(5));
        return rows;
    }

    private string ClientBase() => (config["ClientUrl"] ?? "https://www.orderorange.com").TrimEnd('/');

    private string PartnerBase()
    {
        var client = ClientBase();
        // Staging has no partner hostname: the partner app sits on the next port.
        return client.Contains(":9443") ? client.Replace(":9443", ":9444") : "https://partner.orderorange.com";
    }

    /// <summary>
    /// Everything the assistant must know about the site itself — pages, how accounts
    /// work, how ordering, tracking, booking and table QR work — so "where is the sign-up
    /// page?" gets the page, not a shrug (Majed 2026-10-01: "make it know everything").
    /// </summary>
    private string SiteGuide()
    {
        var c = ClientBase();
        var p = PartnerBase();
        return $"""
            SITE GUIDE (customer website {c}; give these exact links when the customer asks where something is or how to do it):
            - {c}/ Home: real restaurants and shops, the Suggested strip, today's Offers, "Near me" sorting, the search box, and the language button (14 languages: English, Arabic, Persian, Urdu, Hindi, Turkish, German, French, Spanish, Italian, Portuguese, Russian, Japanese, Chinese).
            - {c}/food-delivery browse every store; {c}/cuisines/<cuisine> and {c}/areas/<area> filter by type or area.
            - Store page {c}/p/<storeId> (also /restaurant/<id> and the store's short link {c}/<slug>): menu by category with photos and prices, add to basket, hours, area, phone/WhatsApp, reviews, a "Book a table" button when the store allows online booking, and this chat.
            - Ordering: add dishes to the basket (here in the chat by tapping a card, or on the menu page), then {c}/checkout: choose Delivery or Pickup, the delivery address (saved ones at {c}/addresses), the payment method — cash on delivery, card on delivery, or card online (test mode) — an optional coupon/promo code, then place the order. Each store sets its minimum order and delivery fee; a 0.300 OMR service fee applies.
            - ACCOUNTS / SIGN UP / SIGN IN: there is NO separate registration form. Sign in with Google (the "Continue with Google" button in the sign-in dialog), or with email at {c}/signin/email: type the email, a one-time code is emailed, enter it at {c}/signin/code — a new email is registered automatically the first time. ({c}/signin/password exists only for old accounts.) Signing in is needed to place an order, see orders, book a table or save addresses.
            - {c}/orders my orders; {c}/track/<orderId> live tracking with the status timeline (Pending → Accepted → Preparing → Ready → Picked up → On the way → Delivered) and the courier on a map; an order can be cancelled while it is still Pending.
            - {c}/reservations my table bookings; {c}/reserve-table/<storeId> book a table (date, time, guests, note); the store confirms and the customer is notified.
            - Table QR: at a restaurant table, scanning the QR card opens {c}/reserve/<code> — the table menu to order straight to the table, see the running tab, chat with the house and download the invoice as PDF.
            - {c}/profile name and avatar; {c}/addresses saved delivery addresses; {c}/survey/<storeId> a store's questionnaire; {c}/bill/<code> verifies a printed receipt; {c}/contract/<code> shows a signed supply agreement.
            - Help and contact: the partner guide with a Contact-us form at {c}/guide; each store's own phone/WhatsApp is on its page; the chat buttons are Order, Track, Basket and Help.
            - FOR BUSINESSES (restaurants, cafes, shops): they join free on the partner site {p} ("Join free" / sign up with Google or email), get a free online menu, QR table ordering, POS/till software ({c}/pos), kitchen display, inventory, staff and attendance, loyalty, promotions, Instagram posting and support tickets. Customers cannot register a business from this chat — send them to {p}.
            {PrintingGuide(p)}
            Delivery is by the platform's riders or the store's own courier; pickup is available where the store enables it. Prices are in Omani rial (OMR). If something is truly not on this list, say you are not sure rather than inventing a page.
            """;
    }

    private static string Money(decimal v) => v.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Cap(string? text, int max)
    {
        var s = (text ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>Same reading as the visitor log: Cloudflare, then the forwarded header, then the socket.</summary>
    private string ClientIp(string? reported)
    {
        var socket = HttpContext.Connection.RemoteIpAddress;
        if (socket is null || System.Net.IPAddress.IsLoopback(socket))
        {
            if (!string.IsNullOrWhiteSpace(reported)) return reported.Trim()[..Math.Min(reported.Trim().Length, 60)];
            foreach (var header in new[] { "CF-Connecting-IP", "X-Client-IP", "X-Forwarded-For" })
            {
                var first = Request.Headers[header].ToString().Split(',')[0].Trim();
                if (first.Length > 0) return first[..Math.Min(first.Length, 60)];
            }
        }
        return socket?.ToString() ?? "";
    }
}
