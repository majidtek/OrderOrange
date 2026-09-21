using System.Net;
using System.Net.Mail;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Sends the sign-in code. Nothing else uses email yet, so this stays deliberately small.
///
/// Configured under "Smtp". For Google Workspace that is smtp.gmail.com:587 with an app
/// password — a normal account password will not work, Google refuses plain passwords for
/// SMTP. Leave the host empty and <see cref="IsConfigured"/> is false, which the sign-in
/// endpoint reports honestly rather than pretending a code went out.
/// </summary>
public sealed class EmailSender(IConfiguration config, ILogger<EmailSender> logger)
{
    private string? Host => config["Smtp:Host"] is { Length: > 0 } h ? h : null;
    private int Port => int.TryParse(config["Smtp:Port"], out var p) ? p : 587;
    private string? User => config["Smtp:User"];
    private string? Password => config["Smtp:Password"];
    private string From => config["Smtp:From"] is { Length: > 0 } f ? f : User ?? "no-reply@orderorange.com";
    private string FromName => config["Smtp:FromName"] is { Length: > 0 } n ? n : "OrderOrange";

    public bool IsConfigured => Host is not null;

    /// <summary>
    /// A mailbox other than the platform's — a store's own, typed in its Settings.
    /// Null fields fall back to the platform values, so a store that filled in only
    /// the host still sends.
    /// </summary>
    public sealed record Mailbox(string Host, int Port, string? User, string? Password, string From, string FromName);

    /// <summary>
    /// A picture embedded IN the mail rather than linked from it. Gmail (and most
    /// webmail) strips data: URIs out of img tags, so an inline logo must travel as a
    /// MIME part and be referenced as cid:{Cid} from the HTML.
    /// </summary>
    public sealed record InlineImage(byte[] Data, string ContentType, string Cid);

    /// <summary>A real file riding with the mail — the contract's PDF, most of all.</summary>
    public sealed record FileAttachment(byte[] Data, string Name, string ContentType);

    public Task<bool> SendAsync(string to, string subject, string body, string? html = null) =>
        SendCoreAsync(null, to, subject, body, html, null, null);

    /// <summary>Send from a specific mailbox — the store's paperwork wears the store's address.</summary>
    public Task<bool> SendFromAsync(Mailbox? box, string to, string subject, string body,
        string? html = null, InlineImage? inline = null, FileAttachment? file = null) =>
        SendCoreAsync(box, to, subject, body, html, inline, file);

    private async Task<bool> SendCoreAsync(Mailbox? box, string to, string subject, string body,
        string? html, InlineImage? inline, FileAttachment? file)
    {
        var host = box?.Host ?? Host;
        var port = box?.Port ?? Port;
        var user = box is null ? User : box.User;
        var password = box is null ? Password : box.Password;
        var from = box?.From ?? From;
        var fromName = box?.FromName ?? FromName;
        if (host is null) return false;
        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(from, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false,
            };
            // The plain text stays as the base body; capable clients pick the HTML
            // alternative, ancient ones still get a readable code.
            if (html is not null)
            {
                var view = AlternateView.CreateAlternateViewFromString(
                    html, System.Text.Encoding.UTF8, "text/html");
                if (inline is not null)
                {
                    var resource = new LinkedResource(new MemoryStream(inline.Data), inline.ContentType)
                    {
                        ContentId = inline.Cid,
                        TransferEncoding = System.Net.Mime.TransferEncoding.Base64,
                    };
                    view.LinkedResources.Add(resource);
                }
                message.AlternateViews.Add(view);
            }
            if (file is not null)
                message.Attachments.Add(new Attachment(new MemoryStream(file.Data), file.Name, file.ContentType));
            message.To.Add(to);

            using var client = new SmtpClient(host, port) { EnableSsl = true };

            // Order matters and is easy to get wrong: assigning UseDefaultCredentials
            // AFTER Credentials silently throws the credentials away, and the server then
            // answers "Authentication Required" as though none were ever configured.
            client.UseDefaultCredentials = false;

            // App passwords are shown in groups of four for readability; the spaces are
            // not part of the secret, and leaving them in fails authentication.
            if (!string.IsNullOrWhiteSpace(user))
                client.Credentials = new NetworkCredential(user, (password ?? "").Replace(" ", ""));

            await client.SendMailAsync(message);
            return true;
        }
        catch (SmtpException ex)
        {
            // The status code is the difference between "wrong password" and "wrong host",
            // and without it every failure looks identical from the outside.
            logger.LogError(ex, "Could not send via {Host}:{Port} — {Status}. {Message}",
                host, port, ex.StatusCode, ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            // Never let the address or the code reach the log — the whole point is that
            // only the mailbox owner ever sees it.
            logger.LogError(ex, "Could not send a sign-in code via {Host}:{Port}.", host, port);
            return false;
        }
    }

    /// <summary>
    /// The sign-in code email — a branded card with the code front and centre, and a
    /// plain-text twin underneath so even the oldest client still shows the digits.
    /// Table layout and inline styles on purpose: that is the only CSS email clients
    /// reliably honour.
    /// </summary>
    public Task<bool> SendLoginCodeAsync(string to, string code, int minutes) => SendAsync(
        to,
        $"{code} is your OrderOrange sign-in code",
        $"""
         Your OrderOrange sign-in code is:

             {code}

         It expires in {minutes} minutes and can be used once.

         If you did not try to sign in, you can ignore this email — nobody can get into
         your account without this code.
         """,
        $"""
         <!doctype html>
         <html><body style="margin:0;padding:0;background:#FFF4EC;font-family:'Segoe UI',Tahoma,Arial,sans-serif;">
         <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#FFF4EC;"><tr><td align="center" style="padding:36px 12px;">
           <table role="presentation" cellpadding="0" cellspacing="0" style="max-width:440px;width:100%;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 10px 30px rgba(36,31,27,.10);">
             <tr><td style="background:#FF5A00;height:6px;font-size:0;line-height:6px;">&nbsp;</td></tr>
             <tr><td align="center" style="padding:30px 32px 4px;">
               <div style="font-size:26px;font-weight:800;color:#241F1B;">Order<span style="color:#FF5A00;">Orange</span></div>
             </td></tr>
             <tr><td align="center" style="padding:2px 32px 0;color:#8a7c6c;font-size:14px;">Your sign-in code</td></tr>
             <tr><td align="center" style="padding:20px 32px 14px;">
               <div style="display:inline-block;background:#FFF4EC;border:2px dashed #FFB48A;border-radius:16px;padding:16px 26px;font-size:34px;font-weight:800;letter-spacing:10px;color:#241F1B;font-family:Consolas,Menlo,monospace;">{code}</div>
             </td></tr>
             <tr><td align="center" style="padding:0 32px;color:#241F1B;font-size:14px;">It expires in <b>{minutes} minutes</b> and can be used once.</td></tr>
             <tr><td align="center" style="padding:18px 32px 28px;color:#a2937f;font-size:12px;line-height:1.7;">
               If you did not try to sign in, you can ignore this email —<br>nobody can get into your account without this code.
             </td></tr>
             <tr><td align="center" style="background:#FBF3EA;padding:14px;color:#a2937f;font-size:12px;">OrderOrange &middot; MajidTek</td></tr>
           </table>
         </td></tr></table>
         </body></html>
         """);
}
