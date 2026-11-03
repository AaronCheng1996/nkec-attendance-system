using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Endpoints;

/// <summary>Setting up ministries and their groups (classes, small groups).</summary>
public static class OrganizationEndpoints
{
    public record GroupDetail(
        int Id, int MinistryId, string Name, string? Description, DayOfWeek? DefaultDayOfWeek, TimeOnly? DefaultStartTime,
        int SortOrder, int? MinGrade, int? MaxGrade, bool IsActive, int MemberCount);

    public record MinistryDetail(int Id, string Name, int SortOrder, bool IsActive, List<GroupDetail> Groups);

    public record SaveMinistryRequest(string Name, int SortOrder, bool IsActive = true);

    public record SaveGroupRequest(
        int MinistryId, string Name, string? Description, DayOfWeek? DefaultDayOfWeek, TimeOnly? DefaultStartTime,
        int SortOrder, int? MinGrade, int? MaxGrade, bool IsActive = true);

    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/organization", List).RequireAuthorization(Policies.ManageGroups);
        api.MapPost("/ministries", CreateMinistry).RequireAuthorization(Policies.ManageGroups);
        api.MapPut("/ministries/{id:int}", UpdateMinistry).RequireAuthorization(Policies.ManageGroups);
        api.MapPost("/groups", CreateGroup).RequireAuthorization(Policies.ManageGroups);
        api.MapPut("/groups/{id:int}", UpdateGroup).RequireAuthorization(Policies.ManageGroups);
    }

    /// <summary>Every ministry and group, inactive ones included.</summary>
    static Task<List<MinistryDetail>> List(AppDbContext db, AppClock clock)
    {
        var today = clock.Today;
        return db.Ministries
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => new MinistryDetail(
                m.Id, m.Name, m.SortOrder, m.IsActive,
                m.Groups
                    .OrderBy(g => g.SortOrder).ThenBy(g => g.DefaultStartTime).ThenBy(g => g.Id)
                    .Select(g => new GroupDetail(
                        g.Id, g.MinistryId, g.Name, g.Description, g.DefaultDayOfWeek, g.DefaultStartTime,
                        g.SortOrder, g.MinGrade, g.MaxGrade, g.IsActive,
                        g.Memberships.Count(ms =>
                            ms.StartDate <= today && (ms.EndDate == null || ms.EndDate >= today) && ms.Person.IsActive)))
                    .ToList()))
            .ToListAsync();
    }

    static async Task<IResult> CreateMinistry(SaveMinistryRequest req, AppDbContext db)
    {
        if (ValidateName(req.Name) is { } invalid) return invalid;

        var ministry = new Ministry { Name = req.Name.Trim(), SortOrder = req.SortOrder, IsActive = req.IsActive };
        db.Ministries.Add(ministry);
        await db.SaveChangesAsync();
        return Results.Created($"/api/ministries/{ministry.Id}", new { ministry.Id });
    }

    static async Task<IResult> UpdateMinistry(int id, SaveMinistryRequest req, AppDbContext db)
    {
        if (ValidateName(req.Name) is { } invalid) return invalid;

        var ministry = await db.Ministries.FindAsync(id);
        if (ministry is null) return Results.NotFound();

        ministry.Name = req.Name.Trim();
        ministry.SortOrder = req.SortOrder;
        ministry.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    static async Task<IResult> CreateGroup(SaveGroupRequest req, AppDbContext db)
    {
        if (await ValidateGroupAsync(req, db) is { } invalid) return invalid;

        var group = new Group { MinistryId = req.MinistryId };
        Apply(group, req);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return Results.Created($"/api/groups/{group.Id}", new { group.Id });
    }

    static async Task<IResult> UpdateGroup(int id, SaveGroupRequest req, AppDbContext db)
    {
        if (await ValidateGroupAsync(req, db) is { } invalid) return invalid;

        var group = await db.Groups.FindAsync(id);
        if (group is null) return Results.NotFound();

        group.MinistryId = req.MinistryId;
        Apply(group, req);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    static void Apply(Group group, SaveGroupRequest req)
    {
        group.Name = req.Name.Trim();
        group.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        group.DefaultDayOfWeek = req.DefaultDayOfWeek;
        group.DefaultStartTime = req.DefaultStartTime;
        group.SortOrder = req.SortOrder;
        group.MinGrade = req.MinGrade;
        group.MaxGrade = req.MaxGrade;
        group.IsActive = req.IsActive;
    }

    static IResult? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Http.Invalid("name", "請輸入名稱");
        if (Http.TooLong(name, 50)) return Http.Invalid("name", "名稱最多 50 個字");
        return null;
    }

    static async Task<IResult?> ValidateGroupAsync(SaveGroupRequest req, AppDbContext db)
    {
        if (ValidateName(req.Name) is { } invalid) return invalid;
        if (Http.TooLong(req.Description, 200)) return Http.Invalid("description", "說明最多 200 個字");
        if (req.DefaultDayOfWeek is { } day && !Enum.IsDefined(day)) return Http.Invalid("defaultDayOfWeek", "聚會星期不正確");
        if (!SchoolGrade.IsValid(req.MinGrade) || !SchoolGrade.IsValid(req.MaxGrade) || req.MinGrade > req.MaxGrade)
            return Http.Invalid("minGrade", "年級範圍不正確");
        if (!await db.Ministries.AnyAsync(m => m.Id == req.MinistryId)) return Http.Invalid("ministryId", "請選擇事工");
        return null;
    }
}
