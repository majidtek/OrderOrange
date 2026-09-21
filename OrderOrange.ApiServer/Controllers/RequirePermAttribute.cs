using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OrderOrange.Shared;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// Guards an endpoint (or a whole controller) behind one of <see cref="Perm"/>'s
/// doors. Hiding a button in the portal is courtesy; this is the lock — a cashier's
/// token calling the payroll endpoint by hand gets 403, not a payslip.
///
/// Owners and platform administrators always pass.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequirePermAttribute(string permission, string? alt = null) : Attribute, IAuthorizationFilter
{
    public string Permission { get; } = permission;

    /// <summary>A second key that also opens this door — "menu" OR "menu.view".</summary>
    public string? Alt { get; } = alt;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.IsInRole("Administrator")) return;

        var storeRole = user.FindFirst("storeRole")?.Value ?? "owner";
        var perms = user.FindFirst("perms")?.Value;    // a custom role's own list, when it has one
        if (Perm.Allows(storeRole, perms, Permission)) return;
        if (Alt is not null && Perm.Allows(storeRole, perms, Alt)) return;

        // Coded, so the portal can refuse in the reader's own language.
        context.Result = new ObjectResult(new { code = "perm.denied", message = "Your role does not allow this." })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
