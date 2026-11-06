using System.Security.Claims;
using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Endpoints;

public static class GroupEndpoints
{
    public record GroupDto(
        int Id, string Name, int MinistryId, string MinistryName,
        DayOfWeek? DefaultDayOfWeek, TimeOnly? DefaultStartTime, int MemberCount);

    public record GroupSummary(int Id, string Name);
    public record MinistryDto(int Id, string Name, List<GroupSummary> Groups);
    public record SaveRollcallRequest(List<RollcallItem> Records);

    public static void MapGroupEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/groups", ListGroups);
        api.MapGet("/ministries", ListMinistries).RequireAuthorization(Policies.ManagePeople);

        var group = api.MapGroup("/groups/{groupId:int}");
        group.MapGet("/rollcall", GetRollcall);
        group.MapPut("/rollcall", SaveRollcall);
        group.MapPost("/newcomers", AddNewcomer);
    }

    static Task<List<GroupDto>> ListGroups(GroupAccess access, AppClock clock)
    {
        var today = clock.Today;
        return access.Groups()
            .Where(g => g.IsActive)
            .OrderBy(g => g.Ministry.SortOrder).ThenBy(g => g.DefaultStartTime).ThenBy(g => g.Name)
            .Select(g => new GroupDto(
                g.Id, g.Name, g.MinistryId, g.Ministry.Name, g.DefaultDayOfWeek, g.DefaultStartTime,
                g.Memberships.Count(m =>
                    m.StartDate <= today && (m.EndDate == null || m.EndDate >= today) && m.Person.IsActive)))
            .ToListAsync();
    }

    static Task<List<MinistryDto>> ListMinistries(AppDbContext db) =>
        db.Ministries
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .Select(m => new MinistryDto(
                m.Id, m.Name,
                m.Groups.Where(g => g.IsActive).OrderBy(g => g.DefaultStartTime).Select(g => new GroupSummary(g.Id, g.Name)).ToList()))
            .ToListAsync();

    static async Task<IResult> GetRollcall(int groupId, DateOnly date, GroupAccess access, RollcallService rollcall)
    {
        var group = await access.FindGroupAsync(groupId);
        return group is null ? Results.NotFound() : Results.Ok(await rollcall.BuildAsync(group, date));
    }

    static async Task<IResult> SaveRollcall(
        int groupId, DateOnly date, SaveRollcallRequest req, GroupAccess access, RollcallService rollcall, ClaimsPrincipal user)
    {
        var group = await access.FindGroupAsync(groupId);
        if (group is null) return Results.NotFound();

        var error = await rollcall.SaveAsync(group, date, req.Records ?? [], user.GetUserId()!);
        return error is null
            ? Results.Ok(await rollcall.BuildAsync(group, date))
            : Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
    }

    static async Task<IResult> AddNewcomer(
        int groupId, DateOnly date, NewcomerRequest req, GroupAccess access, RollcallService rollcall, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Http.Invalid("name", "請輸入姓名");
        if (Http.TooLong(req.Name, 50) || Http.TooLong(req.Nickname, 50) || Http.TooLong(req.GuardianName, 50))
            return Http.Invalid("name", "姓名最多 50 個字");
        if (Http.TooLong(req.GuardianPhone, 20)) return Http.Invalid("guardianPhone", "電話格式不正確");
        if (Http.TooLong(req.Notes, 500)) return Http.Invalid("notes", "備註最多 500 個字");
        if (!SchoolGrade.IsValid(req.Grade)) return Http.Invalid("grade", "年級不正確");
        if (req.GuardianRelation is { } relation && !Enum.IsDefined(relation))
            return Http.Invalid("guardianRelation", "家長關係不正確");
        if (!req.Consent) return Http.Invalid("consent", "需取得家長同意才能登記個人資料");

        var group = await access.FindGroupAsync(groupId);
        if (group is null) return Results.NotFound();

        await rollcall.AddNewcomerAsync(group, date, req, user.GetUserId()!);
        return Results.Ok(await rollcall.BuildAsync(group, date));
    }
}
