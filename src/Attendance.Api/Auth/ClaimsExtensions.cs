using System.Security.Claims;

namespace Attendance.Api.Auth;

public static class ClaimsExtensions
{
    public static string? GetUserId(this ClaimsPrincipal user) => user.FindFirstValue(AppClaims.Subject);
}
