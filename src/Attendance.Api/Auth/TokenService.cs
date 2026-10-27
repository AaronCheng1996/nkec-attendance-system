using System.Security.Claims;
using System.Text;
using Attendance.Api.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Attendance.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "attendance-system";
    public string Audience { get; set; } = "attendance-system";
    public string Key { get; set; } = "";
    public int ExpiresHours { get; set; } = 12;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

public class TokenService(IOptions<JwtOptions> options)
{
    readonly JwtOptions jwt = options.Value;

    public string CreateToken(AppUser user, IEnumerable<string> roles)
    {
        var claims = new List<Claim>
        {
            new(AppClaims.Subject, user.Id),
            new(AppClaims.Name, user.UserName!),
            new(AppClaims.DisplayName, user.DisplayName),
            new(AppClaims.Stamp, user.SecurityStamp ?? ""),
        };
        claims.AddRange(roles.Select(r => new Claim(AppClaims.Role, r)));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(jwt.ExpiresHours),
            SigningCredentials = new SigningCredentials(jwt.SigningKey(), SecurityAlgorithms.HmacSha256),
        });
    }

    /// <summary>Rejects tokens of users who were deactivated or whose security stamp changed after the token was issued.</summary>
    public static async Task ValidateUserAsync(TokenValidatedContext context)
    {
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var principal = context.Principal!;
        var user = principal.GetUserId() is { } id ? await users.FindByIdAsync(id) : null;

        if (user is null || !user.IsActive || user.SecurityStamp != principal.FindFirstValue(AppClaims.Stamp))
            context.Fail("User is no longer valid.");
    }
}
