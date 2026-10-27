using System.Security.Claims;
using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace Attendance.Api.Endpoints;

public static class AuthEndpoints
{
    public record LoginRequest(string UserName, string Password);
    public record UserInfo(string Id, string UserName, string DisplayName, IList<string> Roles);
    public record LoginResponse(string AccessToken, UserInfo User);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public const string LoginRateLimit = "login";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimit);
        auth.MapGet("/me", Me);
        auth.MapPost("/change-password", ChangePassword);
    }

    static async Task<IResult> Login(
        LoginRequest req, UserManager<AppUser> users, SignInManager<AppUser> signIn, TokenService tokens, AppDbContext db)
    {
        var userName = req.UserName?.Trim() ?? "";
        var user = userName.Length == 0 ? null : await users.FindByNameAsync(userName);

        if (user is null || !user.IsActive)
        {
            db.LogEvent("LoginFailed", nameof(AppUser), userName);
            await db.SaveChangesAsync();
            return Results.Problem("帳號或密碼錯誤", statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await signIn.CheckPasswordSignInAsync(user, req.Password ?? "", lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            db.LogEvent(result.IsLockedOut ? "LoginLocked" : "LoginFailed", nameof(AppUser), user.Id, user.Id);
            await db.SaveChangesAsync();
            return result.IsLockedOut
                ? Results.Problem("登入失敗次數過多，帳號已暫時鎖定，請 15 分鐘後再試", statusCode: StatusCodes.Status423Locked)
                : Results.Problem("帳號或密碼錯誤", statusCode: StatusCodes.Status401Unauthorized);
        }

        db.LogEvent("Login", nameof(AppUser), user.Id, user.Id);
        await db.SaveChangesAsync();
        return Results.Ok(await CreateLoginResponse(user, users, tokens));
    }

    static UserInfo Me(ClaimsPrincipal user) => new(
        user.GetUserId()!,
        user.FindFirstValue(AppClaims.Name)!,
        user.FindFirstValue(AppClaims.DisplayName)!,
        user.FindAll(AppClaims.Role).Select(c => c.Value).ToList());

    static async Task<IResult> ChangePassword(
        ChangePasswordRequest req, ClaimsPrincipal principal, UserManager<AppUser> users, TokenService tokens)
    {
        var user = await users.FindByIdAsync(principal.GetUserId()!);
        if (user is null) return Results.Unauthorized();

        var result = await users.ChangePasswordAsync(user, req.CurrentPassword ?? "", req.NewPassword ?? "");
        if (!result.Succeeded) return Http.IdentityErrors(result);

        // The security stamp changed, so the old token is now invalid; hand back a new one.
        return Results.Ok(await CreateLoginResponse(user, users, tokens));
    }

    static async Task<LoginResponse> CreateLoginResponse(AppUser user, UserManager<AppUser> users, TokenService tokens)
    {
        var roles = await users.GetRolesAsync(user);
        return new LoginResponse(tokens.CreateToken(user, roles), new UserInfo(user.Id, user.UserName!, user.DisplayName, roles));
    }
}
