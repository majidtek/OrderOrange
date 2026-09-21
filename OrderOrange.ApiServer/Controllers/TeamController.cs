using OrderOrange.ApiServer.Data;
using OrderOrange.ApiServer.Models;
using OrderOrange.ApiServer.Services;
using OrderOrange.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace OrderOrange.ApiServer.Controllers;

/// <summary>
/// The store's key ring: the owner (or a manager) hands out portal logins with a
/// role — Manager, Cashier, Kitchen, Waiter — and can change or withdraw them.
/// Every account created here is scoped to THIS store and nothing else.
/// </summary>
[RequirePerm(Perm.Team)]
[Authorize(Roles = "RestaurantOwner")]
public class TeamController(AppDbContext db) : ApiControllerBase
{
    private async Task<bool> MayManageAsync() =>
        CurrentRestaurantId != 0 && (
            await db.Restaurants.AnyAsync(r => r.Id == CurrentRestaurantId && r.OwnerUserId == CurrentUserId) ||
            (CurrentStoreRole == "manager" &&
             await db.StoreMembers.AnyAsync(m => m.RestaurantId == CurrentRestaurantId && m.UserId == CurrentUserId && m.IsActive)));

    [HttpGet]
    public async Task<ActionResult<List<StoreMemberDto>>> List()
    {
        if (!await MayManageAsync()) return Forbid();

        var owner = await db.Restaurants.Where(r => r.Id == CurrentRestaurantId)
            .Select(r => r.Owner).FirstAsync();
        var members = await db.StoreMembers
            .Where(m => m.RestaurantId == CurrentRestaurantId)
            .Include(m => m.User)
            .Include(m => m.RoleDef)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var result = new List<StoreMemberDto>
        {
            new(0, owner.Id, owner.FullName, owner.Email, owner.Phone,
                StoreRole.Manager, true, owner.CreatedAt, IsOwner: true),
        };
        result.AddRange(members.Select(m => new StoreMemberDto(
            m.Id, m.UserId, m.User.FullName, m.User.Email, m.User.Phone,
            m.Role, m.IsActive, m.CreatedAt, false,
            m.RoleDefId, m.RoleDef?.Name, m.RoleDef?.Icon)));
        return Ok(result);
    }

    // ---------- Roles the owner writes themselves ----------

    [HttpGet("roles")]
    public async Task<ActionResult<List<StoreRoleDefDto>>> Roles()
    {
        if (!await MayManageAsync()) return Forbid();
        var defs = await db.StoreRoleDefs
            .Where(r => r.RestaurantId == CurrentRestaurantId && r.PresetRole == null)
            .OrderBy(r => r.Name).ToListAsync();
        var counts = await db.StoreMembers
            .Where(m => m.RestaurantId == CurrentRestaurantId && m.RoleDefId != null)
            .GroupBy(m => m.RoleDefId!.Value)
            .Select(g => new { Id = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.N);

        return Ok(defs.Select(d => new StoreRoleDefDto(
            d.Id, d.Name, d.Icon, d.PermSet().ToList(),
            counts.TryGetValue(d.Id, out var n) ? n : 0)).ToList());
    }

    /// <summary>
    /// A custom role id is only accepted if it really belongs to THIS store — a
    /// forged id from another shop must never travel across the wall.
    /// </summary>
    private async Task<int?> ValidRoleDefAsync(int? id) =>
        id is { } value && await db.StoreRoleDefs
            .AnyAsync(r => r.Id == value && r.RestaurantId == CurrentRestaurantId && r.PresetRole == null)
            ? id : null;

    // So a saved member answers with the role they now hold, not a stale blank.
    private async Task<string?> RoleNameAsync(int? id) =>
        id is { } v ? await db.StoreRoleDefs.Where(r => r.Id == v).Select(r => r.Name).FirstOrDefaultAsync() : null;
    private async Task<string?> RoleIconAsync(int? id) =>
        id is { } v ? await db.StoreRoleDefs.Where(r => r.Id == v).Select(r => r.Icon).FirstOrDefaultAsync() : null;

    /// <summary>Keeps only permissions this catalog actually knows, in a stable order.</summary>
    private static string CleanPerms(IEnumerable<string>? wanted)
    {
        var set = new HashSet<string>(wanted ?? []);
        return string.Join(',', Perm.All.Where(set.Contains));
    }

    [HttpPost("roles")]
    public async Task<ActionResult<StoreRoleDefDto>> CreateRole(SaveStoreRoleDefRequest req)
    {
        if (!await MayManageAsync()) return Forbid();
        var name = (req.Name ?? "").Trim();
        if (name.Length is < 2 or > 60) return BadRequest(new { message = "Give the role a name." });
        if (await db.StoreRoleDefs.AnyAsync(r => r.RestaurantId == CurrentRestaurantId && r.PresetRole == null && r.Name == name))
            return BadRequest(new { message = "A role with that name already exists." });
        if (await db.StoreRoleDefs.CountAsync(r => r.RestaurantId == CurrentRestaurantId && r.PresetRole == null) >= 20)
            return BadRequest(new { message = "That is as many custom roles as one store may hold." });

        var def = new StoreRoleDef
        {
            RestaurantId = CurrentRestaurantId,
            Name = name,
            Icon = string.IsNullOrWhiteSpace(req.Icon) ? "🔑" : req.Icon.Trim(),
            Perms = CleanPerms(req.Perms),
            CreatedAt = DateTime.Now,
        };
        db.StoreRoleDefs.Add(def);
        await db.SaveChangesAsync();
        return Ok(new StoreRoleDefDto(def.Id, def.Name, def.Icon, def.PermSet().ToList(), 0));
    }

    [HttpPut("roles/{id:int}")]
    public async Task<ActionResult<StoreRoleDefDto>> UpdateRole(int id, SaveStoreRoleDefRequest req)
    {
        if (!await MayManageAsync()) return Forbid();
        var def = await db.StoreRoleDefs
            .FirstOrDefaultAsync(r => r.Id == id && r.RestaurantId == CurrentRestaurantId && r.PresetRole == null);
        if (def is null) return NotFound();

        var name = (req.Name ?? "").Trim();
        if (name.Length is < 2 or > 60) return BadRequest(new { message = "Give the role a name." });
        def.Name = name;
        def.Icon = string.IsNullOrWhiteSpace(req.Icon) ? "🔑" : req.Icon.Trim();
        def.Perms = CleanPerms(req.Perms);
        await db.SaveChangesAsync();

        var n = await db.StoreMembers.CountAsync(m => m.RoleDefId == def.Id);
        return Ok(new StoreRoleDefDto(def.Id, def.Name, def.Icon, def.PermSet().ToList(), n));
    }

    [HttpDelete("roles/{id:int}")]
    public async Task<IActionResult> DeleteRole(int id)
    {
        if (!await MayManageAsync()) return Forbid();
        var def = await db.StoreRoleDefs
            .FirstOrDefaultAsync(r => r.Id == id && r.RestaurantId == CurrentRestaurantId && r.PresetRole == null);
        if (def is null) return NotFound();
        // Nobody is left holding a key to a door that no longer exists: the people on
        // this role fall back to the preset already stored beside it.
        var holders = await db.StoreMembers.Where(m => m.RoleDefId == def.Id).ToListAsync();
        foreach (var m in holders) m.RoleDefId = null;
        db.StoreRoleDefs.Remove(def);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ---------- The presets, rewritten to fit this shop ----------

    /// <summary>The presets this store has rewritten. A role not in the list still opens its built-in doors.</summary>
    [HttpGet("presets")]
    public async Task<ActionResult<List<PresetRolePermsDto>>> Presets()
    {
        if (!await MayManageAsync()) return Forbid();
        var rows = await db.StoreRoleDefs
            .Where(r => r.RestaurantId == CurrentRestaurantId && r.PresetRole != null)
            .ToListAsync();
        return Ok(rows.Select(r => new PresetRolePermsDto((StoreRole)r.PresetRole!.Value, r.PermSet().ToList())).ToList());
    }

    /// <summary>
    /// Rewrites what a preset opens IN THIS STORE. Everyone holding the preset gets
    /// the new key at their next sign-in — the row rides in their token like a
    /// custom role's does.
    /// </summary>
    [HttpPut("presets/{role}")]
    public async Task<ActionResult<PresetRolePermsDto>> SavePreset(string role, SavePresetPermsRequest req)
    {
        if (!await MayManageAsync()) return Forbid();
        if (!Enum.TryParse<StoreRole>(role, true, out var preset)) return NotFound();
        var perms = CleanPerms(req.Perms);
        if (perms.Length == 0)
            return BadRequest(new { code = "roles.pickOne", message = "Pick at least one permission." });

        var row = await db.StoreRoleDefs
            .FirstOrDefaultAsync(r => r.RestaurantId == CurrentRestaurantId && r.PresetRole == (int)preset);
        if (row is null)
        {
            row = new StoreRoleDef
            {
                RestaurantId = CurrentRestaurantId,
                // Never shown anywhere — the preset keeps its own localized name.
                Name = "",
                Icon = "🔑",
                PresetRole = (int)preset,
                CreatedAt = DateTime.Now,
            };
            db.StoreRoleDefs.Add(row);
        }
        row.Perms = perms;
        await db.SaveChangesAsync();
        return Ok(new PresetRolePermsDto(preset, row.PermSet().ToList()));
    }

    /// <summary>Throws the rewrite away — the preset opens its built-in doors again.</summary>
    [HttpDelete("presets/{role}")]
    public async Task<IActionResult> ResetPreset(string role)
    {
        if (!await MayManageAsync()) return Forbid();
        if (!Enum.TryParse<StoreRole>(role, true, out var preset)) return NotFound();
        var row = await db.StoreRoleDefs
            .FirstOrDefaultAsync(r => r.RestaurantId == CurrentRestaurantId && r.PresetRole == (int)preset);
        if (row is not null)
        {
            db.StoreRoleDefs.Remove(row);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<StoreMemberDto>> Create(SaveStoreMemberRequest req)
    {
        if (!await MayManageAsync()) return Forbid();

        var name = (req.FullName ?? "").Trim();
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (name.Length < 2) return BadRequest(new { message = "The member needs a name." });
        // The sign-in name is a USERNAME (cashier1, kitchen.ali) — plain and typeable
        // on a till. A full email is accepted too, for whoever prefers one.
        var validUsername = System.Text.RegularExpressions.Regex.IsMatch(email, @"^[a-z0-9][a-z0-9._-]{2,39}$");
        var validEmail = email.Contains('@') && email.Length is >= 5 and <= 120;
        if (!validUsername && !validEmail)
            return BadRequest(new { message = "Username: 3-40 letters, digits, dots or dashes." });
        if (await db.StoreMembers.CountAsync(m => m.RestaurantId == CurrentRestaurantId) >= 50)
            return BadRequest(new { message = "This store already has the maximum team size." });

        // The same person can hold keys to SEVERAL stores: an email that already
        // belongs to a team account gets a new membership here, not a refusal.
        // Real partner accounts and customer/driver accounts are never hijackable.
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existing is not null)
        {
            var isTeamAccount = existing.Role == UserRole.RestaurantOwner &&
                !await db.Restaurants.AnyAsync(r => r.OwnerUserId == existing.Id);
            if (!isTeamAccount)
                return BadRequest(new { message = "That email already has an account." });
            if (await db.StoreMembers.AnyAsync(m => m.RestaurantId == CurrentRestaurantId && m.UserId == existing.Id))
                return BadRequest(new { message = "That person is already on this store's team." });

            existing.IsActive = true; // a fresh key revives a parked account
            var attach = new StoreMember
            {
                RestaurantId = CurrentRestaurantId,
                UserId = existing.Id,
                Role = req.Role,
                RoleDefId = await ValidRoleDefAsync(req.RoleDefId),
                IsActive = req.IsActive,
                CreatedAt = DateTime.Now,
            };
            db.StoreMembers.Add(attach);
            await db.SaveChangesAsync();
            return Ok(new StoreMemberDto(attach.Id, existing.Id, existing.FullName, existing.Email,
                existing.Phone, attach.Role, attach.IsActive, attach.CreatedAt, false, attach.RoleDefId, await RoleNameAsync(attach.RoleDefId), await RoleIconAsync(attach.RoleDefId)));
        }

        if (string.IsNullOrEmpty(req.Password) || req.Password.Length < 6)
            return BadRequest(new { message = "Password must be at least 6 characters." });

        var user = new User
        {
            FullName = name,
            Email = email,
            Phone = (req.Phone ?? "").Trim(),
            PasswordHash = PasswordHasher.Hash(req.Password),
            Role = UserRole.RestaurantOwner, // opens the partner portal's door; the member row scopes the rooms
            IsActive = true,
            CreatedAt = DateTime.Now,
        };
        db.Users.Add(user);
        var member = new StoreMember
        {
            RestaurantId = CurrentRestaurantId,
            User = user,
            Role = req.Role,
            RoleDefId = await ValidRoleDefAsync(req.RoleDefId),
            IsActive = req.IsActive,
            CreatedAt = DateTime.Now,
        };
        db.StoreMembers.Add(member);
        await db.SaveChangesAsync();

        return Ok(new StoreMemberDto(member.Id, user.Id, user.FullName, user.Email, user.Phone,
            member.Role, member.IsActive, member.CreatedAt, false, member.RoleDefId, await RoleNameAsync(member.RoleDefId), await RoleIconAsync(member.RoleDefId)));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<StoreMemberDto>> Update(int id, SaveStoreMemberRequest req)
    {
        if (!await MayManageAsync()) return Forbid();

        var member = await db.StoreMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == id && m.RestaurantId == CurrentRestaurantId);
        if (member is null) return NotFound();

        var name = (req.FullName ?? "").Trim();
        if (name.Length >= 2) member.User.FullName = name;
        member.User.Phone = (req.Phone ?? "").Trim();
        member.Role = req.Role;
        member.RoleDefId = await ValidRoleDefAsync(req.RoleDefId);
        member.IsActive = req.IsActive;

        if (!string.IsNullOrEmpty(req.Password))
        {
            if (req.Password.Length < 6)
                return BadRequest(new { message = "Password must be at least 6 characters." });
            member.User.PasswordHash = PasswordHasher.Hash(req.Password);
        }

        // A switched-off key also locks the account itself — unless that person
        // owns businesses of their own, which this store has no right to touch.
        if (!await db.Restaurants.AnyAsync(r => r.OwnerUserId == member.UserId))
            member.User.IsActive = member.IsActive ||
                await db.StoreMembers.AnyAsync(m => m.UserId == member.UserId && m.Id != member.Id && m.IsActive);

        await db.SaveChangesAsync();
        return Ok(new StoreMemberDto(member.Id, member.UserId, member.User.FullName, member.User.Email,
            member.User.Phone, member.Role, member.IsActive, member.CreatedAt, false, member.RoleDefId, await RoleNameAsync(member.RoleDefId), await RoleIconAsync(member.RoleDefId)));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id)
    {
        if (!await MayManageAsync()) return Forbid();

        var member = await db.StoreMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == id && m.RestaurantId == CurrentRestaurantId);
        if (member is null) return NotFound();

        db.StoreMembers.Remove(member);
        // The account dies with its last key, unless the person owns stores themselves.
        if (!await db.Restaurants.AnyAsync(r => r.OwnerUserId == member.UserId) &&
            !await db.StoreMembers.AnyAsync(m => m.UserId == member.UserId && m.Id != member.Id && m.IsActive))
        {
            member.User.IsActive = false;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }
}
