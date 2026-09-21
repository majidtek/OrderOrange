using OrderOrange.Shared;

namespace OrderOrange.ClientCore.Services;

/// <summary>Holds the signed-in session for the running app (one instance per circuit).</summary>
public class AppState
{
    public string? Token { get; private set; }
    public int UserId { get; private set; }
    public string FullName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string Phone { get; private set; } = "";
    public UserRole Role { get; private set; }

    /// <summary>
    /// POS lines tapped but not yet saved. Lives HERE, not in the page, so switching
    /// the till onto a table (a navigation) can never throw away what was just rung up.
    /// </summary>
    public Dictionary<int, int> PosPending { get; } = [];

    /// <summary>
    /// A note the cashier typed for a pending POS line — product id → note for the kitchen
    /// (allergies, "no onion"). Rides beside <see cref="PosPending"/> so it survives the same
    /// navigations, and is sent per line exactly like the customer app's per-dish note.
    /// </summary>
    public Dictionary<int, string> PosNotes { get; } = [];

    /// <summary>
    /// Where the POS tour should resume after a navigation recreates the page
    /// (picking a table moves the till to /pos/{id}). -1 = nothing to resume.
    /// </summary>
    public int PosTourResume { get; set; } = -1;

    /// <summary>
    /// Which face of the till (/pos vs /pos-tablet) was on screen last. Lives here,
    /// not on the page: route changes re-create the component, and the switch must
    /// still be seen so the unsent ring-up can be dropped.
    /// </summary>
    public bool? PosLastTablet { get; set; }

    /// <summary>Set only for RestaurantOwner sessions.</summary>
    public int? RestaurantId { get; private set; }
    public string? RestaurantName { get; private set; }

    /// <summary>"owner", "manager", "cashier", "kitchen" or "waiter" — scopes the portal UI.</summary>
    public string? StoreRole { get; private set; }

    /// <summary>A custom role's exact permission list; null when a preset governs.</summary>
    public string? Perms { get; private set; }

    /// <summary>Emoji profile icon; null shows the initial letter.</summary>
    public string? Avatar { get; private set; }

    /// <summary>The uploaded picture, when this person has one; the emoji stands in otherwise.</summary>
    public string? AvatarPhoto { get; private set; }

    /// <summary>The login response this session was built from, so a browser host can
    /// stash it and rebuild the session after a page reload. Null when signed out.</summary>
    public LoginResponse? Session { get; private set; }

    /// <summary>
    /// "Show me everything again": the Restaurants pill or the logo was pressed while the
    /// home page was already open, where a link to the same route is not a navigation and
    /// would leave the filters exactly as they were.
    /// </summary>
    public event Action<bool>? HomeReset;
    /// <param name="restaurantsOnly">True from the Restaurants pill (food places only); false from the logo (everything).</param>
    public void AskHomeReset(bool restaurantsOnly = false) => HomeReset?.Invoke(restaurantsOnly);

    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    /// <summary>Unread guest messages waiting on the table-chat threads.</summary>
    public int TableChatUnread { get; private set; }

    /// <summary>Unread direct messages from teammates.</summary>
    public int TeamChatUnread { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// The layout polls these once for the whole circuit and parks them here, so the
    /// dashboard shortcut and the command palette can show the same badge as the sidebar
    /// without each starting a poll of its own. Fires <see cref="Changed"/> only on a real
    /// change — this runs every few seconds and must not re-render the app for nothing.
    /// </summary>
    public void SetChatUnread(int table, int team)
    {
        if (TableChatUnread == table && TeamChatUnread == team) return;
        TableChatUnread = table;
        TeamChatUnread = team;
        Changed?.Invoke();
    }

    public void SignIn(LoginResponse r)
    {
        Session = r;
        Token = r.Token;
        UserId = r.UserId;
        FullName = r.FullName;
        Email = r.Email;
        Phone = r.Phone;
        Role = r.Role;
        RestaurantId = r.RestaurantId;
        RestaurantName = r.RestaurantName;
        StoreRole = r.StoreRole;
        Perms = r.Perms;
        Avatar = r.Avatar;
        AvatarPhoto = r.AvatarPhoto;
        Changed?.Invoke();
    }

    /// <summary>Keeps the local copy in step after an avatar change — no re-login needed.</summary>
    /// <summary>The picture changed — keep the session and any stashed copy in step.</summary>
    public void UpdateAvatarPhoto(string? photo)
    {
        AvatarPhoto = string.IsNullOrWhiteSpace(photo) ? null : photo;
        if (Session is not null) Session = Session with { AvatarPhoto = AvatarPhoto };
        Changed?.Invoke();
    }

    public void UpdateAvatar(string? avatar)
    {
        Avatar = string.IsNullOrWhiteSpace(avatar) ? null : avatar;
        if (Session is not null)
            Session = Session with { Avatar = Avatar };
        Changed?.Invoke();
    }

    /// <summary>Keeps the local copy in step after a profile edit — no re-login needed.</summary>
    public void UpdateProfile(string fullName, string phone)
    {
        FullName = fullName;
        Phone = phone;
        if (Session is not null)
            Session = Session with { FullName = fullName, Phone = phone };
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Session = null;
        Token = null;
        PosPending.Clear();
        PosNotes.Clear();
        PosTourResume = -1;
        UserId = 0;
        FullName = "";
        Email = "";
        Phone = "";
        Role = UserRole.Customer;
        RestaurantId = null;
        RestaurantName = null;
        StoreRole = null;
        Perms = null;
        Avatar = null;
        Changed?.Invoke();
    }
}
