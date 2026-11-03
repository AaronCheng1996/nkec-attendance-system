using Microsoft.AspNetCore.Authorization;

namespace Attendance.Api.Auth;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Coordinator = "Coordinator";
    public const string Leader = "Leader";

    public static readonly string[] All = [Admin, Coordinator, Leader];
}

/// <summary>
/// 授權原則依「能做什麼」命名，端點只引用原則，不直接寫角色。
/// 角色與權限的對應集中在 <see cref="AddAppPolicies"/>，之後要調整權限只改這裡。
/// </summary>
public static class Policies
{
    public const string ManageUsers = "ManageUsers";
    public const string ManagePeople = "ManagePeople";
    public const string ViewReports = "ViewReports";

    /// <summary>預留：事工 / 群組的新增與編輯。</summary>
    public const string ManageGroups = "ManageGroups";

    /// <summary>預留：健康 / 過敏等敏感欄位。</summary>
    public const string ViewSensitiveInfo = "ViewSensitiveInfo";

    public static AuthorizationBuilder AddAppPolicies(this AuthorizationBuilder builder) => builder
        .AddPolicy(ManageUsers, p => p.RequireRole(Roles.Admin))
        .AddPolicy(ManageGroups, p => p.RequireRole(Roles.Admin))
        .AddPolicy(ManagePeople, p => p.RequireRole(Roles.Admin, Roles.Coordinator))
        .AddPolicy(ViewReports, p => p.RequireRole(Roles.Admin, Roles.Coordinator))
        .AddPolicy(ViewSensitiveInfo, p => p.RequireRole(Roles.Admin, Roles.Coordinator));
}

public static class AppClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string DisplayName = "display_name";
    public const string Role = "role";

    /// <summary>Identity security stamp. Changing a password, role or active flag rotates it and revokes old tokens.</summary>
    public const string Stamp = "stamp";
}
