using Microsoft.AspNetCore.Identity;

namespace Attendance.Api.Auth;

public class ChineseIdentityErrorDescriber : IdentityErrorDescriber
{
    static IdentityError Error(string code, string description) => new() { Code = code, Description = description };

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "密碼錯誤");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"密碼至少需要 {length} 個字元");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "密碼需要包含數字");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "密碼需要包含英文小寫字母");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "密碼需要包含英文大寫字母");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "密碼需要包含符號");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"密碼至少需要 {uniqueChars} 種不同字元");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), $"帳號「{userName}」已被使用");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "帳號只能使用英文字母、數字及 - . _ @ +");
}
