namespace Attendance.Api.Auth;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Coordinator = "Coordinator";
    public const string Leader = "Leader";

    public static readonly string[] All = [Admin, Coordinator, Leader];
}

public static class Policies
{
    public const string Admin = "Admin";

    /// <summary>Admin 或 Coordinator：成員管理、報表。</summary>
    public const string Manager = "Manager";
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
