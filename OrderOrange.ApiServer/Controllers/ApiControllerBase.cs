using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected int CurrentUserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    protected string CurrentUserName =>
        User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

    protected bool IsAdmin => User.IsInRole("Administrator");

    /// <summary>The owner's restaurant id, carried in the token. 0 when the caller isn't an owner.</summary>
    protected int CurrentRestaurantId =>
        int.Parse(User.FindFirstValue("restaurantId") ?? "0");

    /// <summary>
    /// "owner", "manager", "cashier", "kitchen" or "waiter". Tokens minted before
    /// team roles existed carry nothing — those are owners.
    /// </summary>
    protected string CurrentStoreRole =>
        User.FindFirstValue("storeRole") ?? "owner";

    protected bool CanManageStore => CurrentStoreRole is "owner" or "manager";

    /// <summary>A custom role's exact permission list, when the session carries one.</summary>
    protected string? CurrentPerms => User.FindFirstValue("perms");

    /// <summary>Does this session's key open that door? See <see cref="Perm"/>.</summary>
    protected bool Can(string permission) => Perm.Allows(CurrentStoreRole, CurrentPerms, permission);

    /// <summary>
    /// Refuses the call when the key does not fit. Returning 403 (not 404) is honest:
    /// the thing exists, this person may not have it.
    /// </summary>
    protected IActionResult? Deny(string permission) =>
        Can(permission) ? null : StatusCode(403, new { message = "Your role does not allow this." });
}
