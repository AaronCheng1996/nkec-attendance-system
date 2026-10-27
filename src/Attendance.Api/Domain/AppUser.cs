using Microsoft.AspNetCore.Identity;

namespace Attendance.Api.Domain;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
