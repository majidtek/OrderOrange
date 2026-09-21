namespace OrderOrange.Shared;

/// <summary>Remember = "keep me signed in": a 30-day token instead of 12 hours.</summary>
public record LoginRequest(string Email, string Password, bool Remember = false);

/// <summary>
/// "Sign in with Google". <paramref name="IdToken"/> is the credential Google's script
/// hands the browser — it is signed by Google and proves who the person is, so no
/// password crosses the wire. The server verifies the signature before trusting a word
/// of it; anything else would let a caller sign in as anyone by typing their address.
///
/// Only staff apps send <paramref name="StaffOnly"/>: it forbids creating an account on
/// the spot, so a stranger's Google account cannot walk into the partner or admin portal.
/// </summary>
public record GoogleLoginRequest(string IdToken, bool StaffOnly = false, bool Remember = false);

/// <summary>
/// Lets the four apps discover the Google client id from the API instead of each carrying
/// its own copy — one setting to change, and no app can drift out of step with the others.
/// The client id is public by design; it ends up in the page for Google's script to read.
/// Null means Google sign-in is switched off, and the button is not rendered at all.
/// </summary>
public record GoogleAuthConfig(string? ClientId);

// ---------- Signing in with a code emailed to you ----------

/// <summary>Step one: "send a code to this address".</summary>
public record OtpRequest(string Email, bool StaffOnly = false);

/// <summary>
/// The answer to step one. It deliberately does NOT say whether an account exists — that
/// would turn the sign-in box into a tool for checking who has an account here. It only
/// reports whether a code could be sent at all, and when another may be asked for.
/// </summary>
public record OtpRequestResult(bool Sent, int RetryAfterSeconds, string? Message);

/// <summary>Step two: the code the person typed in.</summary>
public record OtpVerifyRequest(string Email, string Code, bool StaffOnly = false, bool Remember = false);

/// <summary>
/// Which ways in an app should offer. Read from the API so the four apps cannot drift
/// apart, and so turning a method off is one setting rather than a redeploy of each.
/// </summary>
public record AuthMethods(string? GoogleClientId, bool EmailCode, bool Password);

// ---------- Admin signing in AS a partner ("open as partner") ----------

/// <summary>
/// A single-use code the admin panel exchanges for entry into another app as another
/// user. A code rather than a token, because the admin's own token must never be
/// planted into the partner app's storage — and the code is worthless twice.
/// </summary>
public record ImpersonationCodeDto(string Code, int ExpiresInSeconds);

public record ImpersonationRedeemRequest(string Code);

/// <summary>Self-service registration — always creates a Customer account.</summary>
public record RegisterRequest(string FullName, string Email, string Phone, string Password, string? Gate = null);

/// <summary>Rider application — creates a Driver account that stays inactive until an admin approves it.</summary>
public record RegisterDriverRequest(string FullName, string Email, string Phone, string Password, VehicleType VehicleType, string? Gate = null);

public record LoginResponse(
    string Token,
    int UserId,
    string FullName,
    string Email,
    string Phone,
    UserRole Role,
    int? RestaurantId,
    string? RestaurantName,
    string? Avatar = null,
    string? StoreRole = null,
    /// <summary>An uploaded profile picture, already squeezed to a small data URI.</summary>
    string? AvatarPhoto = null,
    /// <summary>A custom role's exact permission list, comma-joined. Null = preset role.</summary>
    string? Perms = null);

public record UpdateProfileRequest(string FullName, string Phone);

/// <summary>Partner application — creates an owner account plus an unapproved store.</summary>
public record RegisterPartnerRequest(string FullName, string Email, string Phone, string Password, string RestaurantName, string Area,
    StoreType StoreType = StoreType.Restaurant, double? Lat = null, double? Lng = null,
    Dictionary<string, string>? Names = null,
    string? Gate = null);
public record UpdateAvatarRequest(string? Avatar);

/// <summary>A real profile picture, already squared and squeezed by the browser.</summary>
public record UpdateAvatarPhotoRequest(string? Photo);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// The signed-in user rewriting their OWN sign-in: the email they log in with
/// and/or a new password. Either half may be omitted to leave it untouched.
/// </summary>
public record UpdateCredentialsRequest(string? Email = null, string? NewPassword = null);
