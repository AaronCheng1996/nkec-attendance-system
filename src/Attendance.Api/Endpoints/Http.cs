using Microsoft.AspNetCore.Identity;

namespace Attendance.Api.Endpoints;

static class Http
{
    public static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult IdentityErrors(IdentityResult result, string field = "password") =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [field] = result.Errors.Select(e => e.Description).ToArray(),
        });

    public static bool TooLong(string? value, int max) => value is not null && value.Trim().Length > max;
}
