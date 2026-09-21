using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

public class AuthController(
    AppDbContext db,
    TokenService tokens,
    GoogleTokenVerifier google,
    LoginCodeStore codes,
    EmailSender email,
    IConfiguration config,
    RegistrationThrottle throttle,
    Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
    AdminAlerts alerts,
    CatalogStore catalog) : ApiControllerBase
{
    /// <summary>
    /// The partner app trades an admin-minted code for a session as the target user.
    /// The code was created by an administrator seconds ago and dies on first use, so
    /// this endpoint being anonymous is safe: possession of a live code IS the proof.
    /// </summary>
    [HttpPost("impersonate")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> RedeemImpersonation(ImpersonationRedeemRequest req)
    {
        var key = $"impersonate:{(req.Code ?? "").Trim()}";
        if (!cache.TryGetValue(key, out object? cached) || cached is not int userId)
            return Unauthorized(new { message = "This link has expired. Open it again from the admin panel." });
        cache.Remove(key);                      // single use, even if the sign-in below fails

        var user = await db.Users.FindAsync(userId);
        if (user is null || !user.IsActive) return Unauthorized(new { message = "This account is not available." });

        return await BuildResponse(user);
    }

    /// <summary>
    /// Passwords are no longer offered on any screen. The endpoint stays reachable behind
    /// this switch as a way back in if Google or email delivery ever fails — losing the
    /// admin panel because a mail server is down is a worse outcome than the switch.
    /// Set Auth:AllowPasswordLogin to false to close it for good.
    /// </summary>
    private bool PasswordLoginAllowed => !string.Equals(config["Auth:AllowPasswordLogin"], "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The administrator allowlist. An ADMIN session may only be minted for the emails
    /// named in Admin:AllowedEmails — checked on every route in (password, Google, email
    /// code), because a gate on one door is decoration if the others stay open. Role
    /// checks say what an account may do; this says which accounts may BE administrators
    /// at sign-in, so even a row edited straight in the database cannot walk in unlisted.
    /// Empty setting = no restriction (tests, development).
    /// </summary>
    private string? AdminGate(User user)
    {
        if (user.Role != UserRole.Administrator) return null;
        var allowed = (config["Admin:AllowedEmails"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowed.Length == 0) return null;
        return allowed.Any(e => string.Equals(e, user.Email, StringComparison.OrdinalIgnoreCase))
            ? null
            : "This account is not authorised for the admin panel.";
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        if (!PasswordLoginAllowed)
            return StatusCode(410, new { code = "auth.off", message = "Password sign-in is switched off. Use Google or an email code." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email.Trim().ToLower());
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { code = "auth.bad", message = "Invalid username or password." });
        if (!user.IsActive)
            return Unauthorized(new { code = user.Role == UserRole.Driver ? "auth.driverPending" : "auth.disabled", message = user.Role == UserRole.Driver
                ? "Your rider application is awaiting approval — you'll be able to sign in once the MajidFood team activates your account."
                : "This account has been deactivated. Contact support." });

        if (AdminGate(user) is { } refusal) return Unauthorized(new { code = "auth.notAdmin", message = refusal });
        return await BuildResponse(user, longLived: req.Remember);
    }

    /// <summary>Tells an app whether to show the Google button, and which client id to use.</summary>
    [HttpGet("google/config")]
    [AllowAnonymous]
    public ActionResult<GoogleAuthConfig> GoogleConfig() => Ok(new GoogleAuthConfig(google.ClientId));

    /// <summary>Which sign-in methods this deployment can actually offer right now.</summary>
    [HttpGet("methods")]
    [AllowAnonymous]
    public ActionResult<AuthMethods> Methods() =>
        Ok(new AuthMethods(google.ClientId, email.IsConfigured, PasswordLoginAllowed));

    // ---------- Sign in with a code sent to your email ----------

    /// <summary>
    /// Sends a one-time code. The reply says the same thing whether or not an account
    /// exists: answering honestly would let anyone type addresses into the sign-in box
    /// and learn which of your customers are registered.
    /// </summary>
    [HttpPost("otp/request")]
    [AllowAnonymous]
    public async Task<ActionResult<OtpRequestResult>> RequestCode(OtpRequest req)
    {
        if (!email.IsConfigured)
            return StatusCode(503, new OtpRequestResult(false, 0, "Email sign-in is not configured on this server."));

        var address = (req.Email ?? "").Trim().ToLower();
        if (address.Length < 5 || !address.Contains('@') || !address.Contains('.'))
            return BadRequest(new OtpRequestResult(false, 0, "Enter a valid email address."));

        // Throttle first, and per address — otherwise "resend" is a way to fill someone
        // else's inbox, and a way to keep minting codes until one is guessed.
        var wait = await codes.RetryAfterAsync(address);
        if (wait > TimeSpan.Zero)
            return Ok(new OtpRequestResult(false, (int)Math.Ceiling(wait.TotalSeconds), null));

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address);

        // Staff portals never create accounts, so there is nothing to send to a stranger.
        // The reply is identical either way, so this leaks nothing.
        var mayReceive = user is not null || !req.StaffOnly;
        if (mayReceive)
        {
            var code = LoginCodeStore.NewCode();
            await codes.IssueAsync(address, code);
            await email.SendLoginCodeAsync(address, code, (int)LoginCodeStore.Lifetime.TotalMinutes);
        }

        return Ok(new OtpRequestResult(true, (int)LoginCodeStore.ResendInterval.TotalSeconds, null));
    }

    /// <summary>Checks the code and signs the person in, creating a customer if needed.</summary>
    [HttpPost("otp/verify")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> VerifyCode(OtpVerifyRequest req)
    {
        var address = (req.Email ?? "").Trim().ToLower();
        // Belt and braces to the client's own normalisation: a code typed on a Farsi or
        // Arabic keyboard arrives as Eastern digits — the same six digits, other glyphs.
        var normalized = new string((req.Code ?? "")
            .Where(char.IsDigit)
            .Select(c => (char)('0' + (int)char.GetNumericValue(c)))
            .ToArray());
        var result = await codes.VerifyAsync(address, normalized);

        if (result != LoginCodeStore.Result.Ok)
            return Unauthorized(new
            {
                code = result switch
                {
                    LoginCodeStore.Result.Expired => "auth.codeExpired",
                    LoginCodeStore.Result.TooManyAttempts => "auth.codeTooMany",
                    LoginCodeStore.Result.NoCode => "auth.codeNone",
                    _ => "auth.codeWrong",
                },
                message = result switch
                {
                    LoginCodeStore.Result.Expired => "That code has expired. Ask for a new one.",
                    LoginCodeStore.Result.TooManyAttempts => "Too many wrong codes. Ask for a new one.",
                    LoginCodeStore.Result.NoCode => "Ask for a code first.",
                    _ => "That code is not right.",
                },
            });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address);
        if (user is null)
        {
            if (!throttle.TryRegister(HttpContext))
                return StatusCode(429, new { code = "auth.tooMany", message = "Too many new accounts from this network today. Try again tomorrow." });
            if (req.StaffOnly)
                return Unauthorized(new { code = "auth.noAccount", message = "There is no account for this email. Ask an administrator to create one." });

            user = new User
            {
                FullName = address.Split('@')[0],
                Email = address,
                Phone = "",
                // Nobody chose a password here. The marker keeps the account safe and
                // lets the profile page offer choosing one later.
                PasswordHash = UnusablePassword(),
                Role = UserRole.Customer,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            alerts.NewUser(user.Id, user.FullName, "email code");
        }

        if (!user.IsActive)
            return Unauthorized(new { message = user.Role == UserRole.Driver
                ? "Your rider application is awaiting approval — you'll be able to sign in once the team activates your account."
                : "This account has been deactivated. Contact support." });

        if (AdminGate(user) is { } refusal) return Unauthorized(new { code = "auth.notAdmin", message = refusal });
        return await BuildResponse(user, longLived: req.Remember);
    }

    /// <summary>
    /// "Sign in with Google". Google has already established who this person is; our job
    /// is to prove the token is genuinely Google's, then hand back the same session token
    /// a password sign-in would have produced, so nothing downstream knows the difference.
    /// </summary>
    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Google(GoogleLoginRequest req)
    {
        if (!google.IsConfigured)
            return StatusCode(503, new { message = "Google sign-in is not configured on this server." });

        var payload = await google.VerifyAsync(req.IdToken);
        if (payload is null)
            return Unauthorized(new { code = "auth.googleFail", message = "Google sign-in could not be verified. Please try again." });

        // An unverified address proves nothing: anyone can create a Google account naming
        // an address they do not own, and honouring it would hand them the matching account.
        if (payload.EmailVerified != true || string.IsNullOrWhiteSpace(payload.Email))
            return Unauthorized(new { message = "That Google account has no verified email address." });

        var email = payload.Email.Trim().ToLower();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null)
        {
            if (!throttle.TryRegister(HttpContext))
                return StatusCode(429, new { code = "auth.tooMany", message = "Too many new accounts from this network today. Try again tomorrow." });
            // Staff portals never create accounts from a Google login — otherwise anyone
            // with a Google account could walk into the partner, rider or admin app.
            if (req.StaffOnly)
                return Unauthorized(new { code = "auth.noAccount", message = "There is no account for this email. Ask an administrator to create one." });

            user = new User
            {
                FullName = string.IsNullOrWhiteSpace(payload.Name) ? email.Split('@')[0] : payload.Name.Trim(),
                Email = email,
                Phone = "",
                // No password was ever chosen. They sign in with Google; a password can
                // be set later from the profile page.
                PasswordHash = UnusablePassword(),
                Role = UserRole.Customer,
                IsActive = true,
                CreatedAt = DateTime.Now
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            alerts.NewUser(user.Id, user.FullName, "Google");
        }

        if (!user.IsActive)
            return Unauthorized(new { message = user.Role == UserRole.Driver
                ? "Your rider application is awaiting approval — you'll be able to sign in once the team activates your account."
                : "This account has been deactivated. Contact support." });

        if (AdminGate(user) is { } refusal) return Unauthorized(new { code = "auth.notAdmin", message = refusal });
        return await BuildResponse(user, longLived: req.Remember);
    }

    /// <summary>
    /// Google as the FIRST step of the partner wizard: the verified email becomes the
    /// username, the account and a bookmarked store are born, and the wizard walks on.
    /// A returning owner simply signs in and resumes at their bookmark.
    /// </summary>
    [HttpPost("google-partner")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> GooglePartner(GoogleLoginRequest req)
    {
        if (!google.IsConfigured)
            return StatusCode(503, new { message = "Google sign-in is not configured on this server." });

        var payload = await google.VerifyAsync(req.IdToken);
        if (payload is null)
            return Unauthorized(new { code = "auth.googleFail", message = "Google sign-in could not be verified. Please try again." });
        if (payload.EmailVerified != true || string.IsNullOrWhiteSpace(payload.Email))
            return Unauthorized(new { message = "That Google account has no verified email address." });

        var email = payload.Email.Trim().ToLower();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is not null)
        {
            // A shopper opening a shop is the SAME person — Google just proved they own
            // the address, so the customer account is upgraded, history and all. Only
            // this Google-verified path may do that: the password application form
            // proves nothing and could hijack someone else's account.
            if (user.Role == UserRole.Customer)
                user.Role = UserRole.RestaurantOwner;
            else if (user.Role != UserRole.RestaurantOwner)
                return Unauthorized(new { message = "This email already belongs to a rider or staff account." });
            if (!user.IsActive)
                return Unauthorized(new { code = "auth.disabled", message = "This account has been deactivated. Contact support." });

            // An upgraded shopper has no store yet — give them the wizard's first store,
            // exactly like a brand-new partner. A returning owner just resumes.
            if (!await db.Restaurants.AnyAsync(r => r.OwnerUserId == user.Id))
            {
                var firstCuisine = await db.Cuisines.OrderBy(c => c.Id).FirstAsync();
                var newStore = new Restaurant
                {
                    Owner = user,
                    Name = email.Split('@')[0],
                    CuisineId = firstCuisine.Id,
                    SetupStep = 1,
                    Description = "",
                    Area = "",
                    Phone = "",
                    IsOpen = false,
                    IsApproved = false,
                    CreatedAt = DateTime.Now
                };
                db.Restaurants.Add(newStore);
                await db.SaveChangesAsync();
                await catalog.SeedDefaultMenuAsync(newStore.Id);
                await DefaultFloor.SeedAsync(db, newStore.Id);
                await StoreWordIndex.ReindexAsync(db, newStore); // findable from day one
                alerts.NewBusiness(newStore.Id, newStore.Name, user.FullName, "Google, upgraded shopper");
            }
            else await db.SaveChangesAsync(); // the role upgrade alone still needs saving

            return await BuildResponse(user);
        }

        if (!throttle.TryRegister(HttpContext))
            return StatusCode(429, new { code = "auth.tooMany", message = "Too many new accounts from this network today. Try again tomorrow." });

        user = new User
        {
            FullName = string.IsNullOrWhiteSpace(payload.Name) ? email.Split('@')[0] : payload.Name.Trim(),
            Email = email,
            Phone = "",
            PasswordHash = UnusablePassword(),
            Role = UserRole.RestaurantOwner,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        db.Users.Add(user);

        var cuisine = await db.Cuisines.OrderBy(c => c.Id).FirstAsync();
        var store = new Restaurant
        {
            Owner = user,
            Name = email.Split('@')[0],
            CuisineId = cuisine.Id,
            SetupStep = 1, // the wizard's bookmark: account done, everything else ahead
            Description = "",
            Area = "",
            Phone = "",
            IsOpen = false,
            IsApproved = false,
            CreatedAt = DateTime.Now
        };
        db.Restaurants.Add(store);
        await db.SaveChangesAsync();
        await catalog.SeedDefaultMenuAsync(store.Id); // the house Drinks/Water shelf
        await DefaultFloor.SeedAsync(db, store.Id);          // two salons, sixteen tables
        await StoreWordIndex.ReindexAsync(db, store);        // findable from day one
        alerts.NewBusiness(store.Id, store.Name, user.FullName, "Google");
        return await BuildResponse(user);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Register(RegisterRequest req)
    {
        // Only OUR pages hold a form pass — a cold script does not get to mint stores.
        // Every refusal carries a code: the sign-in screen shows it in the reader's
        // own language (ServerError), and the message tail is only the English fallback.
        if (!FormToken.Validate(req.Gate))
            return BadRequest(new { code = "auth.gate", message = "The page has expired — reload it and try again." });
        var email = req.Email.Trim().ToLower();
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(email))
            return BadRequest(new { code = "auth.missing", message = "Name and username are required." });
        if (req.Password.Length < 6)
            return BadRequest(new { code = "auth.shortPw", message = "Password must be at least 6 characters." });
        if (await db.Users.AnyAsync(u => u.Email == email))
            return BadRequest(new { code = "auth.exists", message = "An account with this username already exists." });
        if (!throttle.TryRegister(HttpContext))
            return StatusCode(429, new { code = "auth.tooMany", message = "Too many new accounts from this network today. Try again tomorrow." });

        var user = new User
        {
            FullName = req.FullName.Trim(),
            Email = email,
            Phone = req.Phone.Trim(),
            PasswordHash = string.IsNullOrEmpty(req.Password) ? UnusablePassword() : PasswordHasher.Hash(req.Password),
            Role = UserRole.Customer,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        alerts.NewUser(user.Id, user.FullName, "sign-up form");

        return await BuildResponse(user);
    }

    [HttpPost("register-driver")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterDriver(RegisterDriverRequest req)
    {
        // Only OUR pages hold a form pass — a cold script does not get to mint stores.
        if (!FormToken.Validate(req.Gate))
            return BadRequest(new { message = "The page has expired — reload it and try again." });
        var email = req.Email.Trim().ToLower();
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "Name and email are required." });
        if (string.IsNullOrWhiteSpace(req.Phone))
            return BadRequest(new { message = "A phone number is required — dispatch needs to reach you." });
        if (req.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });
        if (await db.Users.AnyAsync(u => u.Email == email))
            return BadRequest(new { message = "An account with this email already exists." });
        if (!throttle.TryRegister(HttpContext))
            return StatusCode(429, new { code = "auth.tooMany", message = "Too many new accounts from this network today. Try again tomorrow." });

        // Applications start INACTIVE — an admin flips the switch on the Users page,
        // exactly like restaurant approval.
        var user = new User
        {
            FullName = req.FullName.Trim(),
            Email = email,
            Phone = req.Phone.Trim(),
            PasswordHash = string.IsNullOrEmpty(req.Password) ? UnusablePassword() : PasswordHasher.Hash(req.Password),
            Role = UserRole.Driver,
            IsActive = false,
            CreatedAt = DateTime.Now
        };
        db.Users.Add(user);
        db.DriverProfiles.Add(new DriverProfile { User = user, VehicleType = req.VehicleType, IsOnline = false });
        await db.SaveChangesAsync();
        alerts.NewDriver(user.Id, user.FullName, "rider form");

        return Ok(new { message = "Application received." });
    }

    [HttpPost("register-partner")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterPartner(RegisterPartnerRequest req)
    {
        // Only OUR pages hold a form pass — a cold script does not get to mint stores.
        if (!FormToken.Validate(req.Gate))
            return BadRequest(new { message = "The page has expired — reload it and try again." });
        var email = req.Email.Trim().ToLower();
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "Name and username are required." });
        if (string.IsNullOrWhiteSpace(req.RestaurantName))
            return BadRequest(new { message = "The store name is required." });
        if (req.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });
        if (await db.Users.AnyAsync(u => u.Email == email))
            return BadRequest(new { message = "An account with this username already exists." });
        if (!throttle.TryRegister(HttpContext))
            return StatusCode(429, new { code = "auth.tooMany", message = "Too many new accounts from this network today. Try again tomorrow." });

        // The owner can sign in right away to prepare the menu; the store itself
        // stays invisible to customers until an admin approves it.
        var user = new User
        {
            FullName = req.FullName.Trim(),
            Email = email,
            Phone = req.Phone.Trim(),
            PasswordHash = string.IsNullOrEmpty(req.Password) ? UnusablePassword() : PasswordHasher.Hash(req.Password),
            Role = UserRole.RestaurantOwner,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        db.Users.Add(user);

        var cuisine = await db.Cuisines.OrderBy(c => c.Id).FirstAsync();
        var names = RestaurantsController.CleanNames(req.Names);
        if (req.Names is not null && !names.ContainsKey("ar"))
            return BadRequest(new { message = "The Arabic name is required." });
        var store = new Restaurant
        {
            Owner = user,
            Name = RestaurantsController.CanonicalName(names) ?? req.RestaurantName.Trim(),
            NameLocalized = RestaurantsController.NamesToJson(names),
            // A wizard birth (no names yet) leaves a bookmark; a one-shot form is complete.
            SetupStep = req.Names is null ? 1 : -1,
            Description = "",
            CuisineId = cuisine.Id,
            StoreType = req.StoreType,
            Area = req.Area.Trim(),
            Phone = req.Phone.Trim(),
            Lat = req.Lat,
            Lng = req.Lng,
            IsOpen = false,
            IsApproved = false,
            CreatedAt = DateTime.Now
        };
        db.Restaurants.Add(store);
        await db.SaveChangesAsync();
        await catalog.SeedDefaultMenuAsync(store.Id); // the house Drinks/Water shelf
        await DefaultFloor.SeedAsync(db, store.Id);          // two salons, sixteen tables
        await StoreWordIndex.ReindexAsync(db, store);        // findable from day one
        alerts.NewBusiness(store.Id, store.Name, user.FullName, "partner form");

        return Ok(new { message = "Application received." });
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest req)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(req.FullName))
            return BadRequest(new { message = "Name is required." });
        user.FullName = req.FullName.Trim();
        user.Phone = req.Phone.Trim();
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();
        // Accounts born from Google or an emailed code never chose a password — their
        // FIRST one is set without being asked for a "current" that does not exist.
        var neverSet = user.PasswordHash == NeverSetPassword;
        if (!neverSet && !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { code = "auth.wrongCurrent", message = "Current password is incorrect." });
        if (req.NewPassword.Length < 6)
            return BadRequest(new { message = "New password must be at least 6 characters." });
        user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// The owner card on the team page: change the email you sign in with, set a new
    /// password, or both. Self-service only — it always edits the CALLER's account,
    /// so no team permission can point it at anyone else.
    /// </summary>
    [HttpPut("credentials")]
    public async Task<IActionResult> UpdateCredentials(UpdateCredentialsRequest req)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var email = req.Email.Trim().ToLowerInvariant();
            // Same door policy as team members: a plain typeable USERNAME (owner1,
            // kitchen.ali) is as welcome as a full email address.
            var validUsername = System.Text.RegularExpressions.Regex.IsMatch(email, @"^[a-z0-9][a-z0-9._-]{2,39}$");
            var validEmail = email.Contains('@') && email.Length is >= 5 and <= 120;
            if (!validUsername && !validEmail)
                return BadRequest(new { code = "auth.badEmail", message = "Enter a valid email or username." });
            if (await db.Users.AnyAsync(u => u.Id != user.Id && u.Email == email))
                return BadRequest(new { code = "auth.emailTaken", message = "That email already signs in to another account." });
            user.Email = email;
        }

        if (!string.IsNullOrWhiteSpace(req.NewPassword))
        {
            if (req.NewPassword.Length < 6)
                return BadRequest(new { code = "auth.pwShort", message = "New password must be at least 6 characters." });
            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// The mark of "no password was ever chosen". Verify() refuses it (no salt.key shape),
    /// so it can never be signed in with — but unlike a random hash it is RECOGNISABLE,
    /// which lets the profile page offer "set a password" without asking for the current
    /// one these accounts never had.
    /// </summary>
    private static string UnusablePassword() => NeverSetPassword;

    internal const string NeverSetPassword = "!";

    /// <summary>
    /// A partner may own several businesses, but a session works in ONE at a time — the
    /// token carries a single store id and every owner endpoint scopes by it. This swaps
    /// the session to another store the same account owns; anything else is refused, so
    /// a token can never be minted for somebody else's business.
    /// </summary>
    [HttpPost("switch-store/{restaurantId:int}")]
    [Authorize(Roles = "RestaurantOwner")]
    public async Task<ActionResult<LoginResponse>> SwitchStore(int restaurantId)
    {
        var restaurant = await db.Restaurants
            .FirstOrDefaultAsync(r => r.Id == restaurantId && r.OwnerUserId == CurrentUserId);
        // Not the owner? A team member with an active key may still enter.
        if (restaurant is null &&
            await db.StoreMembers.AnyAsync(m => m.RestaurantId == restaurantId && m.UserId == CurrentUserId && m.IsActive))
        {
            restaurant = await db.Restaurants.FindAsync(restaurantId);
        }
        if (restaurant is null) return NotFound(new { message = "That store is not yours." });

        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null || !user.IsActive) return Unauthorized();

        return await BuildResponse(user, restaurant);
    }

    /// <summary>
    /// A member on a custom role carries that role's exact permission list; a member
    /// on a preset carries nothing extra and is judged by the preset's name.
    /// </summary>
    private async Task<string?> PermsOf(StoreMember membership)
    {
        if (membership.RoleDefId is not { } id)
        {
            // On a preset — but the store may have REWRITTEN that preset. If it has,
            // the rewrite rides in the token exactly like a custom role's list.
            var over = await db.StoreRoleDefs.FirstOrDefaultAsync(r =>
                r.RestaurantId == membership.RestaurantId && r.PresetRole == (int)membership.Role);
            return string.IsNullOrWhiteSpace(over?.Perms) ? null : over!.Perms;
        }
        var def = await db.StoreRoleDefs
            .FirstOrDefaultAsync(r => r.Id == id && r.RestaurantId == membership.RestaurantId);
        return string.IsNullOrWhiteSpace(def?.Perms) ? null : def!.Perms;
    }

    private async Task<LoginResponse> BuildResponse(User user, Restaurant? chosen = null, bool longLived = false)
    {
        // Owners carry their restaurant in the token so every owner endpoint can scope
        // by it without an extra lookup. With several businesses the session opens in
        // the first; switch-store re-issues the token for a sibling. Team members own
        // nothing — their store and role come from the StoreMembers key ring.
        var restaurant = chosen;
        string? storeRole = null;
        // A custom role's exact permissions ride in the token, so every request is
        // judged without another trip to the database.
        string? perms = null;

        if (user.Role == UserRole.RestaurantOwner)
        {
            restaurant ??= await db.Restaurants
                .Where(r => r.OwnerUserId == user.Id).OrderBy(r => r.Id).FirstOrDefaultAsync();

            if (restaurant is null)
            {
                var membership = await db.StoreMembers
                    .Where(m => m.UserId == user.Id && m.IsActive)
                    .OrderBy(m => m.Id).FirstOrDefaultAsync();
                if (membership is not null)
                {
                    restaurant = await db.Restaurants.FindAsync(membership.RestaurantId);
                    storeRole = membership.Role.ToString().ToLowerInvariant();
                    perms = await PermsOf(membership);
                }
            }
            else if (restaurant.OwnerUserId == user.Id)
            {
                storeRole = "owner";
            }
            else
            {
                var membership = await db.StoreMembers.FirstOrDefaultAsync(
                    m => m.RestaurantId == restaurant.Id && m.UserId == user.Id && m.IsActive);
                storeRole = membership?.Role.ToString().ToLowerInvariant() ?? "owner";
                if (membership is not null) perms = await PermsOf(membership);
            }
        }

        var token = tokens.CreateToken(user, restaurant?.Id, storeRole, perms, longLived);
        return new LoginResponse(token, user.Id, user.FullName, user.Email, user.Phone,
            user.Role, restaurant?.Id, restaurant?.Name, user.AvatarIcon, storeRole, user.AvatarPhoto, perms);
    }

    [HttpPut("avatar")]
    public async Task<IActionResult> UpdateAvatar(UpdateAvatarRequest req)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();
        var icon = req.Avatar?.Trim();
        if (icon is { Length: > 16 })
            return BadRequest(new { message = "Invalid avatar." });
        user.AvatarIcon = string.IsNullOrWhiteSpace(icon) ? null : icon;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// A real profile picture. The browser has already squared and squeezed it, so
    /// what lands here is a small data URI — the cap below is a backstop against a
    /// client that did not, not the normal path.
    /// </summary>
    [HttpPut("avatar/photo")]
    public async Task<IActionResult> UpdateAvatarPhoto(UpdateAvatarPhotoRequest req)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return NotFound();

        var photo = req.Photo?.Trim();
        if (string.IsNullOrWhiteSpace(photo))
        {
            user.AvatarPhoto = null;          // "remove my picture"
        }
        else
        {
            if (!photo.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "That is not an image." });
            if (photo.Length > 400_000)       // ≈300KB of pixels; a squeezed square is far less
                return BadRequest(new { message = "That picture is too large." });
            user.AvatarPhoto = photo;
        }

        await db.SaveChangesAsync();
        return NoContent();
    }
}
