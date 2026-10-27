using System.Security.Claims;
using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Endpoints;

public static class UserEndpoints
{
    public record UserDto(
        string Id, string UserName, string DisplayName, bool IsActive, bool IsLockedOut, string? Role,
        List<int> GroupIds, List<int> MinistryIds, DateTimeOffset CreatedAt);

    public record CreateUserRequest(
        string UserName, string DisplayName, string Password, string Role, List<int>? GroupIds, List<int>? MinistryIds);

    public record UpdateUserRequest(string DisplayName, bool IsActive, string Role, List<int>? GroupIds, List<int>? MinistryIds);
    public record ResetPasswordRequest(string NewPassword);

    public static void MapUserEndpoints(this IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("/users").RequireAuthorization(Policies.Admin);
        users.MapGet("/", List);
        users.MapPost("/", Create);
        users.MapPut("/{id}", Update);
        users.MapPost("/{id}/reset-password", ResetPassword);
    }

    static async Task<List<UserDto>> List(AppDbContext db)
    {
        var users = await db.Users.OrderBy(u => u.UserName).ToListAsync();
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name })
            .ToListAsync();
        var leaders = await db.GroupLeaders.ToListAsync();
        var coordinators = await db.MinistryCoordinators.ToListAsync();
        var now = DateTimeOffset.UtcNow;

        return users.Select(u => new UserDto(
                u.Id, u.UserName!, u.DisplayName, u.IsActive, u.LockoutEnd > now,
                roles.FirstOrDefault(r => r.UserId == u.Id)?.Name,
                leaders.Where(l => l.UserId == u.Id).Select(l => l.GroupId).ToList(),
                coordinators.Where(c => c.UserId == u.Id).Select(c => c.MinistryId).ToList(),
                u.CreatedAt))
            .ToList();
    }

    static async Task<IResult> Create(CreateUserRequest req, UserManager<AppUser> users, AppDbContext db)
    {
        if (Validate(req.DisplayName, req.Role) is { } invalid) return invalid;

        var user = new AppUser
        {
            UserName = req.UserName?.Trim(),
            DisplayName = req.DisplayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var created = await users.CreateAsync(user, req.Password ?? "");
        if (!created.Succeeded)
            return Http.IdentityErrors(created, created.Errors.Any(e => e.Code.Contains("UserName")) ? "userName" : "password");

        await users.AddToRoleAsync(user, req.Role);
        await SetAssignmentsAsync(db, user.Id, req.GroupIds, req.MinistryIds);
        return Results.Created($"/api/users/{user.Id}", new { user.Id });
    }

    static async Task<IResult> Update(
        string id, UpdateUserRequest req, ClaimsPrincipal principal, UserManager<AppUser> users, AppDbContext db)
    {
        if (Validate(req.DisplayName, req.Role) is { } invalid) return invalid;

        var user = await users.FindByIdAsync(id);
        if (user is null) return Results.NotFound();
        if (principal.GetUserId() == id && (!req.IsActive || req.Role != Roles.Admin))
            return Results.Problem("不能停用自己的帳號或移除自己的管理員權限", statusCode: StatusCodes.Status400BadRequest);

        var revokeTokens = user.IsActive && !req.IsActive;
        user.DisplayName = req.DisplayName.Trim();
        user.IsActive = req.IsActive;
        await users.UpdateAsync(user);

        var currentRoles = await users.GetRolesAsync(user);
        if (currentRoles.Count != 1 || currentRoles[0] != req.Role)
        {
            await users.RemoveFromRolesAsync(user, currentRoles);
            await users.AddToRoleAsync(user, req.Role);
            revokeTokens = true;
        }
        if (revokeTokens) await users.UpdateSecurityStampAsync(user);

        await SetAssignmentsAsync(db, user.Id, req.GroupIds, req.MinistryIds);
        return Results.NoContent();
    }

    static async Task<IResult> ResetPassword(string id, ResetPasswordRequest req, UserManager<AppUser> users, AppDbContext db)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return Results.NotFound();

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, req.NewPassword ?? "");
        if (!result.Succeeded) return Http.IdentityErrors(result);

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        db.LogEvent("ResetPassword", nameof(AppUser), user.Id);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    static IResult? Validate(string? displayName, string? role)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return Http.Invalid("displayName", "請輸入顯示名稱");
        if (Http.TooLong(displayName, 50)) return Http.Invalid("displayName", "顯示名稱最多 50 個字");
        if (role is null || !Roles.All.Contains(role)) return Http.Invalid("role", "角色不正確");
        return null;
    }

    static async Task SetAssignmentsAsync(AppDbContext db, string userId, List<int>? groupIds, List<int>? ministryIds)
    {
        groupIds ??= [];
        ministryIds ??= [];
        var groups = await db.Groups.Where(g => groupIds.Contains(g.Id)).Select(g => g.Id).ToListAsync();
        var ministries = await db.Ministries.Where(m => ministryIds.Contains(m.Id)).Select(m => m.Id).ToListAsync();

        // Diff against the current assignments so unchanged rows do not show up in the audit log.
        var leaders = await db.GroupLeaders.Where(l => l.UserId == userId).ToListAsync();
        db.GroupLeaders.RemoveRange(leaders.Where(l => !groups.Contains(l.GroupId)));
        db.GroupLeaders.AddRange(groups
            .Except(leaders.Select(l => l.GroupId))
            .Select(id => new GroupLeader { UserId = userId, GroupId = id }));

        var coordinators = await db.MinistryCoordinators.Where(c => c.UserId == userId).ToListAsync();
        db.MinistryCoordinators.RemoveRange(coordinators.Where(c => !ministries.Contains(c.MinistryId)));
        db.MinistryCoordinators.AddRange(ministries
            .Except(coordinators.Select(c => c.MinistryId))
            .Select(id => new MinistryCoordinator { UserId = userId, MinistryId = id }));

        await db.SaveChangesAsync();
    }
}
