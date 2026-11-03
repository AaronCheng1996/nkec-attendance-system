using Microsoft.AspNetCore.Identity;

namespace Attendance.Api.Domain;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>預留：帳號對應的人員資料（同工本身也是 People 的一筆）。</summary>
    public int? PersonId { get; set; }

    // Google / LINE 登入使用 Identity 內建的 AspNetUserLogins，不需要額外欄位。
}
