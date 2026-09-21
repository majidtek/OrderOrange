using System.Text.Json;
using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Standing supply agreements the store writes for its regulars — "10 trays, 10
/// deliveries a month, at this price". Paperwork the store keeps on file and emails
/// to the customer as a lettered document wearing the store's own logo and details.
/// It books no orders by itself; the kitchen still works from real orders.
/// </summary>
[Authorize(Roles = "RestaurantOwner")]
[RequirePerm(Perm.Contracts)]
public class ContractsController(AppDbContext db, EmailSender email, PushSender push, IConfiguration config) : ApiControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // The line items ride in one JSON column; these keep the shape in one place.
    private sealed record Line(string N, int Q, decimal P);

    private static List<ContractLineDto> LinesOf(StoreContract c)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<Line>>(c.ItemsJson, Json) ?? [])
                .Select(l => new ContractLineDto(l.N, l.Q, l.P)).ToList();
        }
        catch (JsonException) { return []; }
    }

    private string ClientBase => (config["ClientUrl"] ?? "https://www.orderorange.com").TrimEnd('/');

    /// <summary>The API's own public origin — where the pdf link lives. Empty = no link.</summary>
    private string PublicApiBase => (config["Media:PublicBase"] ?? "").TrimEnd('/');

    private string PdfUrlOf(int contractId) =>
        PublicApiBase.Length > 0 ? $"{PublicApiBase}/api/contract/{ContractCode.For(contractId)}/pdf" : "";

    // Rendering a PDF costs a Chrome run (~15 s), so a fresh copy is kept for a few
    // minutes — the customer opening the emailed link right after the partner previewed
    // it must not pay for a second print of the same letter.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, (byte[] Data, DateTime At)> PdfCache = new();

    /// <summary>
    /// "97006290, 79107022" → "9700 6290 · 7910 7022". The Phone column carries every
    /// number the store typed, comma-joined; read out loud they need air between the
    /// digits and a clean dot between the numbers.
    /// </summary>
    internal static string PrettyPhones(string raw) =>
        string.Join(" · ", raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Length == 8 && p.All(char.IsDigit) ? $"{p[..4]} {p[4..]}" : p));

    /// <summary>
    /// The phone line as typography, not decoration: a small tracked TEL label in the
    /// letter's accent colour, then each number bold and tappable. No glyphs anywhere —
    /// Gmail turns ☎ and its kin into cartoon emoji, which is what sank two designs.
    /// </summary>
    private static string PhoneChips(string raw)
    {
        var numbers = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (numbers.Length == 0) return "";
        var links = numbers.Select(p =>
        {
            var pretty = p.Length == 8 && p.All(char.IsDigit) ? $"{p[..4]} {p[4..]}" : p;
            var dial = new string(p.Where(ch => char.IsDigit(ch) || ch == '+').ToArray());
            return $"""<a href="tel:{dial}" style="color:#241F1B;font-weight:700;text-decoration:none;white-space:nowrap;">{System.Net.WebUtility.HtmlEncode(pretty)}</a>""";
        });
        return $"""
            <div style="direction:ltr;font-size:13px;">
              <span style="font-size:10px;font-weight:800;letter-spacing:.12em;color:#E07A3E;">TEL</span>&nbsp;&nbsp;{string.Join("""&nbsp;&nbsp;<span style="color:#D9CBBB;">|</span>&nbsp;&nbsp;""", links)}
            </div>
            """;
    }

    private ContractDto ToDto(StoreContract c) => new(
        c.Id, c.CustomerName, c.CustomerEmail, c.CustomerPhone, LinesOf(c),
        c.TimesPerMonth, c.Months, c.MonthlyPrice, c.StartDate, c.Note, c.SentAt, c.CreatedAt,
        ContractCode.LinkFor(c.Id, ClientBase), PdfUrlOf(c.Id),
        c.AcceptedAt, c.AcceptedBy, RepliesOf(c));

    private static List<ContractReplyDto> RepliesOf(StoreContract c)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<Reply>>(c.RepliesJson, Json) ?? [])
                .Select(r => new ContractReplyDto(r.T, r.At)).ToList();
        }
        catch (JsonException) { return []; }
    }

    private sealed record Reply(string T, DateTime At);

    private static string? Validate(SaveContractRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CustomerName)) return "The customer needs a name.";
        if (req.Lines is not { Count: > 0 }) return "Add at least one product.";
        if (req.Lines.Count > 40) return "That is more lines than one contract can hold.";
        if (req.Lines.Any(l => string.IsNullOrWhiteSpace(l.Name))) return "Every line needs a product name.";
        if (req.Lines.Any(l => l.Quantity is < 1 or > 10_000)) return "Quantities must be 1–10,000.";
        if (req.Lines.Any(l => l.UnitPrice < 0)) return "Prices cannot be negative.";
        if (req.TimesPerMonth is < 1 or > 31) return "Deliveries per month must be 1–31.";
        if (req.Months is < 0 or > 60) return "The term must be 0 (open) to 60 months.";
        if (req.MonthlyPrice < 0) return "The monthly price cannot be negative.";
        return null;
    }

    [HttpGet]
    public async Task<ActionResult<List<ContractDto>>> List()
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var list = await db.StoreContracts
            .Where(c => c.RestaurantId == CurrentRestaurantId)
            .OrderByDescending(c => c.Id).Take(200).ToListAsync();
        return Ok(list.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ContractDto>> Create(SaveContractRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (Validate(req) is { } wrong) return BadRequest(new { message = wrong });

        var contract = new StoreContract
        {
            RestaurantId = CurrentRestaurantId,
            CustomerName = req.CustomerName.Trim(),
            CustomerEmail = (req.CustomerEmail ?? "").Trim(),
            CustomerPhone = (req.CustomerPhone ?? "").Trim(),
            ItemsJson = JsonSerializer.Serialize(
                req.Lines.Select(l => new Line(l.Name.Trim(), l.Quantity, l.UnitPrice)).ToList(), Json),
            TimesPerMonth = req.TimesPerMonth,
            Months = req.Months,
            MonthlyPrice = req.MonthlyPrice,
            StartDate = req.StartDate.Date,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            CreatedAt = DateTime.Now,
        };
        db.StoreContracts.Add(contract);
        await db.SaveChangesAsync();
        return Ok(ToDto(contract));
    }

    /// <summary>Rewrite a contract in place — it has not been signed, only drafted.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ContractDto>> Update(int id, SaveContractRequest req)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        if (Validate(req) is { } wrong) return BadRequest(new { message = wrong });
        var contract = await db.StoreContracts.FirstOrDefaultAsync(
            c => c.Id == id && c.RestaurantId == CurrentRestaurantId);
        if (contract is null) return NotFound();
        if (contract.AcceptedAt is not null)
            return BadRequest(new { message = "The customer already accepted this contract — use it as a draft for a new one instead." });

        contract.CustomerName = req.CustomerName.Trim();
        contract.CustomerEmail = (req.CustomerEmail ?? "").Trim();
        contract.CustomerPhone = (req.CustomerPhone ?? "").Trim();
        contract.ItemsJson = JsonSerializer.Serialize(
            req.Lines.Select(l => new Line(l.Name.Trim(), l.Quantity, l.UnitPrice)).ToList(), Json);
        contract.TimesPerMonth = req.TimesPerMonth;
        contract.Months = req.Months;
        contract.MonthlyPrice = req.MonthlyPrice;
        contract.StartDate = req.StartDate.Date;
        contract.Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        await db.SaveChangesAsync();
        PdfCache.TryRemove(contract.Id, out _);   // the letter changed; the warm copy lies
        return Ok(ToDto(contract));
    }

    /// <summary>
    /// The contract as its PUBLIC page shows it. No account: the signature in the code
    /// is the key, exactly like a receipt's verification QR — only someone holding the
    /// emailed link can open it, and ids cannot be walked.
    /// </summary>
    [HttpGet("~/api/contract/{code}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicContractDto>> View(string code)
    {
        if (!ContractCode.TryRead(code, out var id)) return NotFound();
        var contract = await db.StoreContracts.Include(c => c.Restaurant)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (contract is null) return NotFound();

        var store = contract.Restaurant;
        var storeUrl = store.Slug is { Length: > 0 } slug ? $"{ClientBase}/{slug}" : $"{ClientBase}/p/{store.Id}";
        return Ok(new PublicContractDto(
            store.Name, RestaurantsController.NamesFromJson(store.NameLocalized),
            store.LogoData, store.LogoEmoji,
            store.Area, store.Street, PrettyPhones(store.Phone), store.Email,
            store.CrNumber, store.VatNumber, storeUrl,
            contract.CustomerName, LinesOf(contract),
            contract.TimesPerMonth, contract.Months, contract.MonthlyPrice,
            contract.StartDate, contract.Note, contract.CreatedAt, PdfUrlOf(contract.Id),
            contract.AcceptedAt, contract.AcceptedBy, RepliesOf(contract)));
    }

    /// <summary>
    /// The customer says YES, from the public page — the signed code in the link is
    /// their key, exactly as it is for reading. Idempotent: a second tap changes
    /// nothing. The store's bell rings.
    /// </summary>
    [HttpPost("~/api/contract/{code}/accept")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicContractDto>> Accept(string code, AcceptContractRequest req)
    {
        if (!ContractCode.TryRead(code, out var id)) return NotFound();
        var contract = await db.StoreContracts.Include(c => c.Restaurant).FirstOrDefaultAsync(c => c.Id == id);
        if (contract is null) return NotFound();

        if (contract.AcceptedAt is null)
        {
            contract.AcceptedAt = DateTime.Now;
            contract.AcceptedBy = string.IsNullOrWhiteSpace(req.Name) ? contract.CustomerName : req.Name.Trim();
            await db.SaveChangesAsync();
            PdfCache.TryRemove(contract.Id, out _);   // the letter wears the stamp now
            push.SendToStore(contract.RestaurantId, $"📄✅ {contract.AcceptedBy}",
                $"Accepted the supply agreement · {contract.MonthlyPrice:0.000} OMR/month", "/contracts");
        }
        return await View(code);
    }

    /// <summary>
    /// The customer's counter-offer: extra products, a different term — allowed only
    /// while the contract is still unaccepted, written straight onto it so both sides
    /// look at ONE document. The store's bell says what changed.
    /// </summary>
    [HttpPost("~/api/contract/{code}/propose")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicContractDto>> Propose(string code, ProposeContractRequest req)
    {
        if (!ContractCode.TryRead(code, out var id)) return NotFound();
        var contract = await db.StoreContracts.FirstOrDefaultAsync(c => c.Id == id);
        if (contract is null) return NotFound();
        if (contract.AcceptedAt is not null)
            return BadRequest(new { message = "This contract is already accepted — write to the restaurant instead." });

        var adding = (req.AddLines ?? []).Where(l => !string.IsNullOrWhiteSpace(l.Name)).ToList();
        if (adding.Count == 0 && req.Months is null)
            return BadRequest(new { message = "Nothing to change yet." });
        if (adding.Any(l => l.Quantity is < 1 or > 10_000)) return BadRequest(new { message = "Quantities must be 1–10,000." });
        if (adding.Any(l => l.UnitPrice < 0)) return BadRequest(new { message = "Prices cannot be negative." });
        if (req.Months is < 0 or > 60) return BadRequest(new { message = "The term must be 0 (open) to 60 months." });

        var lines = LinesOf(contract);
        if (lines.Count + adding.Count > 40)
            return BadRequest(new { message = "That is more lines than one contract can hold." });
        lines.AddRange(adding.Select(l => new ContractLineDto(l.Name.Trim(), l.Quantity, l.UnitPrice)));
        contract.ItemsJson = JsonSerializer.Serialize(
            lines.Select(l => new Line(l.Name, l.Quantity, l.UnitPrice)).ToList(), Json);
        var said = new List<string>();
        if (adding.Count > 0) said.Add($"+{adding.Count} product(s)");
        if (req.Months is { } months && months != contract.Months)
        {
            contract.Months = months;
            said.Add(months == 0 ? "term: open-ended" : $"term: {months} month(s)");
        }
        await db.SaveChangesAsync();
        PdfCache.TryRemove(contract.Id, out _);
        push.SendToStore(contract.RestaurantId, $"📄✏️ {contract.CustomerName}",
            $"Asked for changes — {string.Join(", ", said)}", "/contracts");
        return await View(code);
    }

    /// <summary>The customer writes back; the store hears it on its bell.</summary>
    [HttpPost("~/api/contract/{code}/reply")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicContractDto>> ReplyTo(string code, ReplyContractRequest req)
    {
        if (!ContractCode.TryRead(code, out var id)) return NotFound();
        var contract = await db.StoreContracts.FirstOrDefaultAsync(c => c.Id == id);
        if (contract is null) return NotFound();
        var text = (req.Text ?? "").Trim();
        if (text.Length is 0 or > 500)
            return BadRequest(new { message = "Write something — up to 500 characters." });

        var replies = RepliesOf(contract);
        if (replies.Count >= 30)
            return BadRequest(new { message = "This contract already carries 30 replies." });
        replies.Add(new ContractReplyDto(text, DateTime.Now));
        contract.RepliesJson = JsonSerializer.Serialize(
            replies.Select(r => new Reply(r.Text, r.At)).ToList(), Json);
        await db.SaveChangesAsync();
        push.SendToStore(contract.RestaurantId, $"📄💬 {contract.CustomerName}",
            text.Length > 80 ? text[..80] + "…" : text, "/contracts");
        return await View(code);
    }

    /// <summary>
    /// The letter AS A PDF, straight from its link — for whoever wants the file, not a
    /// web page. Same signed code as the page; rendered by printing the page, kept warm
    /// for a few minutes because a Chrome run is not free.
    /// </summary>
    [HttpGet("~/api/contract/{code}/pdf")]
    [AllowAnonymous]
    public async Task<IActionResult> Pdf(string code)
    {
        if (!ContractCode.TryRead(code, out var id)) return NotFound();
        if (await db.StoreContracts.CountAsync(c => c.Id == id) == 0) return NotFound();

        if (!PdfCache.TryGetValue(id, out var hit) || DateTime.Now - hit.At > TimeSpan.FromMinutes(10))
        {
            var fresh = await RenderPdfAsync(ContractCode.LinkFor(id, ClientBase));
            if (fresh is null) return StatusCode(503, new { message = "The PDF could not be produced right now." });
            hit = (fresh, DateTime.Now);
            PdfCache[id] = hit;
        }
        Response.Headers.ContentDisposition = $"inline; filename=supply-agreement-{id}.pdf";
        return File(hit.Data, "application/pdf");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var contract = await db.StoreContracts.FirstOrDefaultAsync(
            c => c.Id == id && c.RestaurantId == CurrentRestaurantId);
        if (contract is null) return NotFound();
        db.StoreContracts.Remove(contract);
        await db.SaveChangesAsync();
        PdfCache.TryRemove(contract.Id, out _);
        return NoContent();
    }

    /// <summary>
    /// Email the contract to the customer as a lettered document: the store's logo and
    /// details at the head, the goods and terms in the middle, the price at the foot.
    /// Sent from the store's OWN mailbox when Settings carries one; the platform's
    /// otherwise. The send is stamped, so the list shows what already went out.
    /// </summary>
    [HttpPost("{id:int}/email")]
    public async Task<ActionResult<ContractDto>> Email(int id)
    {
        if (CurrentRestaurantId == 0) return Forbid();
        var contract = await db.StoreContracts.FirstOrDefaultAsync(
            c => c.Id == id && c.RestaurantId == CurrentRestaurantId);
        if (contract is null) return NotFound();
        // One address or several — the field takes a comma-separated list.
        var recipients = contract.CustomerEmail
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => a.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (recipients.Count == 0)
            return BadRequest(new { message = "The customer has no email address on the contract." });

        var store = await db.Restaurants.FirstAsync(r => r.Id == CurrentRestaurantId);

        // The store's own mailbox first; the platform's only as the fallback.
        var box = store.SmtpHost.Length > 0
            ? new EmailSender.Mailbox(store.SmtpHost, store.SmtpPort,
                store.SmtpUser.Length > 0 ? store.SmtpUser : null,
                store.SmtpPassword.Length > 0 ? store.SmtpPassword : null,
                store.SmtpFrom.Length > 0 ? store.SmtpFrom : store.SmtpUser,
                store.Name)
            : null;
        if (box is null && !email.IsConfigured)
            return BadRequest(new { message = "No mailbox is set up. Add your email details in Settings." });

        var (subject, text, html, inline) = Compose(store, contract);

        // The letter as a real PDF, printed from its own public page — the same file
        // "Download PDF" saves. Best effort: a mail without the attachment still goes.
        var pdf = await RenderPdfAsync(ContractCode.LinkFor(contract.Id, ClientBase));
        var file = pdf is null ? null
            : new EmailSender.FileAttachment(pdf, $"supply-agreement-{contract.Id}.pdf", "application/pdf");

        // Each address gets its own envelope; one bad mailbox must not starve the rest.
        var delivered = 0;
        foreach (var to in recipients)
            if (await email.SendFromAsync(box, to, subject, text, html, inline, file)) delivered++;
        if (delivered == 0)
            return BadRequest(new { message = "The email could not be sent — check the email details in Settings." });

        contract.SentAt = DateTime.Now;
        await db.SaveChangesAsync();
        return Ok(ToDto(contract));
    }

    /// <summary>
    /// Prints a page to PDF with the Chrome on this server — the public contract page
    /// carries print CSS, so what comes out is the letter alone on one clean A4.
    /// --timeout forces the print: a Blazor page holds its websocket open, so Chrome
    /// never sees "network idle" on its own. Null = no Chrome, or the print failed;
    /// the caller sends without the attachment rather than not at all.
    /// </summary>
    private async Task<byte[]?> RenderPdfAsync(string url)
    {
        var chrome = config["Chrome:Path"] ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        if (!System.IO.File.Exists(chrome)) return null;
        var output = Path.Combine(Path.GetTempPath(), $"oo-contract-{Guid.NewGuid():N}.pdf");
        var profile = Path.Combine(Path.GetTempPath(), $"oo-pdfp-{Guid.NewGuid():N}");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = chrome,
                Arguments = string.Join(' ',
                    "--headless=new", $"\"--user-data-dir={profile}\"", "--no-first-run",
                    "--disable-gpu", "--timeout=30000", "--no-pdf-header-footer",   // 15s printed blank pages (a Blazor page needs its circuit first)
                    $"\"--print-to-pdf={output}\"", $"\"{url}\""),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(40)).Token);
            return System.IO.File.Exists(output) ? await System.IO.File.ReadAllBytesAsync(output) : null;
        }
        catch (Exception)
        {
            return null;   // the letter still goes as HTML; the attachment is a bonus
        }
        finally
        {
            try { System.IO.File.Delete(output); } catch (IOException) { }
            try { Directory.Delete(profile, true); } catch (IOException) { }
        }
    }

    private (string Subject, string Text, string Html, EmailSender.InlineImage? Inline) Compose(
        Restaurant store, StoreContract contract)
    {
        var lines = LinesOf(contract);
        var term = contract.Months == 0 ? "Open-ended" : $"{contract.Months} month(s)";
        var perDelivery = lines.Sum(l => l.Quantity * l.UnitPrice);

        // Where the customer finds the store online — the slug link when the owner
        // chose one, the plain store page otherwise. Shown as a button AND as a bare
        // address, because a bare address is the thing people copy and share.
        var storeUrl = store.Slug is { Length: > 0 } slug ? $"{ClientBase}/{slug}" : $"{ClientBase}/p/{store.Id}";
        var contractUrl = ContractCode.LinkFor(contract.Id, ClientBase);

        // The logo rides as a plain https URL, NOT as an embedded MIME part: Gmail
        // lists cid: images in the attachment strip, where only the PDF belongs.
        EmailSender.InlineImage? inline = null;
        var logoUrl = MediaLinks.Logo(config, store.Id, store.LogoData);
        if (logoUrl is not null && !logoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            logoUrl = null;   // no public media base — the emoji stands in

        var text = $"""
            SUPPLY AGREEMENT — {store.Name}

            Customer: {contract.CustomerName}
            Starts: {contract.StartDate:dd MMM yyyy}   Term: {term}
            Deliveries: {contract.TimesPerMonth} per month

            Goods per delivery:
            {string.Join("\n", lines.Select(l => $"  {l.Quantity} × {l.Name} @ {l.UnitPrice:0.000} OMR"))}

            Monthly price: {contract.MonthlyPrice:0.000} OMR
            {(contract.Note is { } n ? $"\nNotes: {n}\n" : "")}
            {store.Name} · {store.Area} {store.Street} · {store.Phone}
            View online: {contractUrl}
            Menu: {storeUrl}
            {(store.CrNumber.Length > 0 ? $"CR {store.CrNumber}" : "")} {(store.VatNumber.Length > 0 ? $"VAT {store.VatNumber}" : "")}
            """;

        // The store's real logo, linked — the customer opens a letter, not a template.
        var logo = logoUrl is not null
            ? $"<img src=\"{logoUrl}\" width=\"56\" height=\"56\" style=\"border-radius:14px;display:block;\" alt=\"\"/>"
            : $"<div style=\"font-size:40px;line-height:56px;\">{store.LogoEmoji}</div>";

        var rows = string.Join("", lines.Select(l => $"""
            <tr>
              <td style="padding:8px 10px;border-bottom:1px solid #F1E7DC;border-right:1px solid #F1E7DC;color:#241F1B;">{System.Net.WebUtility.HtmlEncode(l.Name)}</td>
              <td align="center" style="padding:8px 10px;border-bottom:1px solid #F1E7DC;border-right:1px solid #F1E7DC;color:#241F1B;">{l.Quantity}</td>
              <td align="right" style="padding:8px 10px;border-bottom:1px solid #F1E7DC;color:#241F1B;">{l.UnitPrice:0.000}</td>
            </tr>
            """));

        var chips = PhoneChips(store.Phone);
        var phoneRow = chips.Length > 0
            ? $"""<tr><td style="padding:8px 32px 0;">{chips}</td></tr>"""
            : "";

        var html = $"""
            <!doctype html>
            <html><body style="margin:0;padding:0;background:#FFF4EC;font-family:'Segoe UI',Tahoma,Arial,sans-serif;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#FFF4EC;"><tr><td align="center" style="padding:36px 12px;">
              <table role="presentation" cellpadding="0" cellspacing="0" style="max-width:560px;width:100%;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 10px 30px rgba(36,31,27,.10);">
                <tr><td style="background:#FF5A00;height:6px;font-size:0;line-height:6px;">&nbsp;</td></tr>
                <tr><td style="padding:26px 32px 6px;">
                  <table role="presentation" cellpadding="0" cellspacing="0" width="100%"><tr>
                    <td width="66" valign="top">{logo}</td>
                    <td valign="middle" style="padding-inline-start:8px;">
                      <div style="font-size:21px;font-weight:800;color:#241F1B;">{System.Net.WebUtility.HtmlEncode(store.Name)}</div>
                      <div style="font-size:12.5px;color:#8a7c6c;">{System.Net.WebUtility.HtmlEncode($"{store.Area} {store.Street}".Trim())}</div>
                    </td>
                  </tr></table>
                </td></tr>
                {phoneRow}
                <tr><td style="padding:14px 32px 0;">
                  <div style="font-size:11px;font-weight:800;letter-spacing:.12em;color:#E07A3E;">SUPPLY AGREEMENT</div>
                  <div style="font-size:17px;font-weight:800;color:#241F1B;padding-top:2px;">{System.Net.WebUtility.HtmlEncode(contract.CustomerName)}</div>
                </td></tr>
                <tr><td style="padding:14px 32px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="font-size:13px;border:1px solid #F1E7DC;border-radius:12px;">
                    <tr style="background:#FBF3EA;">
                      <td style="padding:8px 10px;font-weight:700;color:#8a7c6c;border-bottom:1px solid #F1E7DC;border-right:1px solid #F1E7DC;">Starts</td>
                      <td style="padding:8px 10px;font-weight:700;color:#8a7c6c;border-bottom:1px solid #F1E7DC;border-right:1px solid #F1E7DC;">Term</td>
                      <td style="padding:8px 10px;font-weight:700;color:#8a7c6c;border-bottom:1px solid #F1E7DC;">Deliveries / month</td>
                    </tr>
                    <tr>
                      <td style="padding:8px 10px;color:#241F1B;font-weight:700;border-right:1px solid #F1E7DC;">{contract.StartDate:dd MMM yyyy}</td>
                      <td style="padding:8px 10px;color:#241F1B;font-weight:700;border-right:1px solid #F1E7DC;">{term}</td>
                      <td style="padding:8px 10px;color:#241F1B;font-weight:700;">{contract.TimesPerMonth}</td>
                    </tr>
                  </table>
                </td></tr>
                <tr><td style="padding:16px 32px 0;">
                  <table role="presentation" cellpadding="0" cellspacing="0" width="100%" style="font-size:13px;border:1px solid #F1E7DC;border-radius:12px;">
                    <tr style="background:#FBF3EA;">
                      <td style="padding:8px 10px;font-weight:700;color:#8a7c6c;border-bottom:1px solid #F1E7DC;border-right:1px solid #F1E7DC;">Product</td>
                      <td align="center" style="padding:8px 10px;font-weight:700;color:#8a7c6c;border-bottom:1px solid #F1E7DC;border-right:1px solid #F1E7DC;">Qty / delivery</td>
                      <td align="right" style="padding:8px 10px;font-weight:700;color:#8a7c6c;border-bottom:1px solid #F1E7DC;">Unit OMR</td>
                    </tr>
                    {rows}
                    <tr><td colspan="2" style="padding:8px 10px;color:#8a7c6c;border-bottom:1px solid #F1E7DC;border-right:1px solid #F1E7DC;">Per delivery</td>
                        <td align="right" style="padding:8px 10px;color:#241F1B;border-bottom:1px solid #F1E7DC;">{perDelivery:0.000}</td></tr>
                    <tr style="background:#FFF4EC;">
                      <td colspan="2" style="padding:12px 10px;font-size:14px;color:#8a7c6c;border-right:1px solid #F1E7DC;">Monthly price</td>
                      <td align="right" style="padding:12px 10px;font-size:17px;font-weight:800;color:#E04E00;white-space:nowrap;">{contract.MonthlyPrice:0.000} OMR</td>
                    </tr>
                  </table>
                </td></tr>
                {(contract.Note is { } note ? $"""<tr><td style="padding:14px 32px 0;font-size:12.5px;color:#8a7c6c;line-height:1.6;">{System.Net.WebUtility.HtmlEncode(note)}</td></tr>""" : "")}
                <tr><td align="center" style="padding:20px 32px 0;">
                  <a href="{contractUrl}" style="display:inline-block;background:#FF5A00;color:#ffffff;text-decoration:none;font-size:14px;font-weight:800;padding:12px 30px;border-radius:12px;">View the contract online</a>
                  <div style="padding-top:8px;font-size:12px;color:#8a7c6c;">Share or save this link:
                    <a href="{contractUrl}" style="color:#E04E00;text-decoration:none;font-weight:700;direction:ltr;">{contractUrl}</a>
                  </div>
                  {(PdfUrlOf(contract.Id) is { Length: > 0 } pdfLink ? $"""<div style="padding-top:6px;font-size:12px;color:#8a7c6c;">PDF: <a href="{pdfLink}" style="color:#E04E00;text-decoration:none;font-weight:700;direction:ltr;">{pdfLink}</a></div>""" : "")}
                  <div style="padding-top:6px;font-size:12px;color:#8a7c6c;">Our menu:
                    <a href="{storeUrl}" style="color:#E04E00;text-decoration:none;font-weight:700;direction:ltr;">{storeUrl}</a>
                  </div>
                </td></tr>
                <tr><td style="padding:22px 32px 26px;font-size:11.5px;color:#a2937f;line-height:1.7;">
                  {System.Net.WebUtility.HtmlEncode(store.Name)}
                  {(store.CrNumber.Length > 0 ? $" · CR {System.Net.WebUtility.HtmlEncode(store.CrNumber)}" : "")}
                  {(store.VatNumber.Length > 0 ? $" · VAT {System.Net.WebUtility.HtmlEncode(store.VatNumber)}" : "")}
                  {(store.Email.Length > 0 ? $" · {System.Net.WebUtility.HtmlEncode(store.Email)}" : "")}
                </td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;

        return ($"Supply agreement — {store.Name}", text, html, inline);
    }
}
