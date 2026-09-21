using Google.Apis.Auth;

namespace OrderOrange.ApiServer.Services;

/// <summary>
/// Checks that a "Sign in with Google" credential really came from Google.
///
/// The browser hands us a token that merely CLAIMS to be someone's Google identity.
/// Believing it as-is would be the same as letting anyone sign in by typing an email
/// address, so every field is worthless until the signature is checked against Google's
/// public keys. Google.Apis.Auth does that, plus the expiry and issuer checks, and
/// caches the key set so this is not an HTTP call per sign-in.
///
/// The audience check is the part people forget: without it a token minted for some
/// OTHER site's Google app would be accepted here. It has to be OUR client id.
/// </summary>
public sealed class GoogleTokenVerifier(IConfiguration config, ILogger<GoogleTokenVerifier> logger)
{
    /// <summary>From Google Cloud Console → Credentials → OAuth client ID (Web application).</summary>
    public string? ClientId => config["Google:ClientId"] is { Length: > 0 } id ? id : null;

    /// <summary>False until a client id is configured — the button stays hidden until then.</summary>
    public bool IsConfigured => ClientId is not null;

    /// <summary>The verified identity, or null if the token is forged, expired or not ours.</summary>
    public async Task<GoogleJsonWebSignature.Payload?> VerifyAsync(string idToken)
    {
        if (ClientId is not { } clientId || string.IsNullOrWhiteSpace(idToken)) return null;
        try
        {
            return await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [clientId],
            });
        }
        catch (InvalidJwtException ex)
        {
            // Expected for anything tampered with or stale — not worth an error-level log.
            logger.LogInformation("Rejected a Google credential: {Reason}", ex.Message);
            return null;
        }
    }
}
