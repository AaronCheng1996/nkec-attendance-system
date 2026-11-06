using Attendance.Api.Data;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Endpoints;

/// <summary>Changes applied to many members at once from the people list.</summary>
public static class PeopleBatchEndpoints
{
    public const int MaxPeople = 1000;

    public record BatchMoveRequest(List<int> PersonIds, int GroupId, DateOnly StartDate);
    public record BatchGradeRequest(List<int> PersonIds, int? Grade);
    /// <param name="Years">How many grades to move up; -1 undoes a promotion.</param>
    public record PromoteRequest(List<int> PersonIds, int Years = 1);

    /// <param name="Updated">People changed.</param>
    /// <param name="Skipped">People left as they were (e.g. already in the target group).</param>
    public record BatchResult(int Updated, int Skipped);

    public static void MapPeopleBatchEndpoints(this RouteGroupBuilder people)
    {
        people.MapPost("/batch/move", Move);
        people.MapPost("/batch/grade", SetGrade);
        people.MapPost("/batch/promote", Promote);
    }

    static IResult? ValidateIds(List<int>? ids) =>
        ids is null || ids.Count == 0 ? Http.Invalid("personIds", "請先勾選成員")
        : ids.Count > MaxPeople ? Http.Invalid("personIds", $"一次最多 {MaxPeople} 人")
        : null;

    static Task<List<Person>> LoadAsync(GroupAccess access, List<int> ids)
    {
        var distinct = ids.Distinct().ToList();
        return access.People()
            .Include(p => p.Memberships).ThenInclude(m => m.Group)
            .Where(p => distinct.Contains(p.Id))
            .ToListAsync();
    }

    static async Task<IResult> Move(BatchMoveRequest req, AppDbContext db, GroupAccess access)
    {
        if (ValidateIds(req.PersonIds) is { } invalid) return invalid;
        var target = await access.FindGroupAsync(req.GroupId);
        if (target is null) return Http.Invalid("groupId", "請選擇群組");

        var people = await LoadAsync(access, req.PersonIds);
        var moved = people.Count(p => PeopleEndpoints.MoveMember(db, p, target, req.StartDate));
        await db.SaveChangesAsync();
        return Results.Ok(new BatchResult(moved, people.Count - moved));
    }

    static async Task<IResult> SetGrade(BatchGradeRequest req, AppDbContext db, GroupAccess access, AppClock clock)
    {
        if (ValidateIds(req.PersonIds) is { } invalid) return invalid;
        if (!SchoolGrade.IsValid(req.Grade)) return Http.Invalid("grade", "年級不正確");

        var today = clock.Today;
        var entryYear = req.Grade is { } grade ? SchoolGrade.EntryYear(grade, today) : (int?)null;
        var ids = req.PersonIds.Distinct().ToList();
        var people = await access.People().Where(p => ids.Contains(p.Id)).ToListAsync();
        var changed = people.Where(p => p.SchoolEntryYear != entryYear).ToList();
        changed.ForEach(p => p.SchoolEntryYear = entryYear);
        await db.SaveChangesAsync();
        return Results.Ok(new BatchResult(changed.Count, people.Count - changed.Count));
    }

    /// <summary>
    /// Moves everyone selected up <see cref="PromoteRequest.Years"/> grades (小班 → 中班, 小二 → 小三…).
    /// Grades are stored as the school entry year, so this shifts that year; their groups do not change.
    /// People without a grade, or who would leave the valid range, are skipped.
    /// </summary>
    static async Task<IResult> Promote(PromoteRequest req, AppDbContext db, GroupAccess access, AppClock clock)
    {
        if (ValidateIds(req.PersonIds) is { } invalid) return invalid;
        if (req.Years is 0 or < -3 or > 3) return Http.Invalid("years", "升班級數不正確");

        var today = clock.Today;
        var ids = req.PersonIds.Distinct().ToList();
        var people = await access.People().Where(p => ids.Contains(p.Id)).ToListAsync();
        var changed = people
            .Where(p => SchoolGrade.Grade(p.SchoolEntryYear, today) is { } grade
                && SchoolGrade.IsValid(grade + req.Years))
            .ToList();
        changed.ForEach(p => p.SchoolEntryYear -= req.Years);
        await db.SaveChangesAsync();
        return Results.Ok(new BatchResult(changed.Count, people.Count - changed.Count));
    }
}
