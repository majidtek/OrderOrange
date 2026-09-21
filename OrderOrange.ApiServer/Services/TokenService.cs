using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using OrderOrange.ApiServer.Models;
using Microsoft.IdentityModel.Tokens;

namespace OrderOrange.ApiServer.Services;

public class TokenService(IConfiguration config)
{
    /// <param name="perms">
    /// The exact permissions of a custom role, comma separated. When present the
    /// session is judged by this list rather than by the preset named in storeRole.
    /// </param>
    public string CreateToken(User user, int? restaurantId = null, string? storeRole = null, string? perms = null, bool longLived = false)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (restaurantId is not null)
            claims.Add(new Claim("restaurantId", restaurantId.Value.ToString()));
        if (!string.IsNullOrEmpty(storeRole))
            claims.Add(new Claim("storeRole", storeRole));
        if (!string.IsNullOrEmpty(perms))
            claims.Add(new Claim("perms", perms));

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            // "Keep me signed in" = a month; otherwise a working day and a bit.
            expires: longLived ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
