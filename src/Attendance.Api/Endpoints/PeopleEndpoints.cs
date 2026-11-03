using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Endpoints;

public static class PeopleEndpoints
{
    public record PersonListItem(
        int Id, string Name, string? Nickname, Gender Gender, DateOnly? BirthDate, int? Grade, DateOnly? FirstVisitDate,
        bool IsActive, List<string> Groups);

    public record GuardianDto(int PersonId, string Name, string? Phone, RelationType RelationType, bool IsPrimaryContact);
    public record MembershipDto(int Id, int GroupId, string GroupName, DateOnly StartDate, DateOnly? EndDate);
    public record AttendanceHistoryItem(DateOnly Date, string GroupName, AttendanceStatus Status);

    public record PersonDetail(
        int Id, string Name, string? Nickname, Gender Gender, DateOnly? BirthDate, int? Grade, string? Phone, string? Email,
        DateOnly? FirstVisitDate, DateTimeOffset? ConsentAt, string? Notes, bool IsActive,
        List<GuardianDto> Guardians, List<MembershipDto> Memberships, List<AttendanceHistoryItem> RecentAttendance);

    public record UpdatePersonRequest(
        string Name, string? Nickname, Gender Gender, DateOnly? BirthDate, int? Grade, string? Phone, string? Email,
        string? Notes, bool IsActive);

    public record MovePersonRequest(int GroupId, DateOnly StartDate);

    public static void MapPeopleEndpoints(this IEndpointRouteBuilder api)
    {
        var people = api.MapGroup("/people").RequireAuthorization(Policies.ManagePeople);
        people.MapGet("/", List);
        people.MapGet("/{id:int}", Get);
        people.MapPut("/{id:int}", Update);
        people.MapPost("/{id:int}/move", Move);
        people.MapDelete("/{id:int}/memberships/{membershipId:int}", EndMembership);
    }

    static Task<List<PersonListItem>> List(
        GroupAccess access, AppClock clock, string? search, int? groupId, bool includeInactive = false)
    {
        var today = clock.Today;
        // Only people who belong (or belonged) to a group; guardians show up on their child's page.
        var q = access.People().Where(p => p.Memberships.Any());
        if (!includeInactive) q = q.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p => p.Name.Contains(s) || (p.Nickname != null && p.Nickname.Contains(s)));
        }
        if (groupId is not null)
            q = q.Where(p => p.Memberships.Any(m =>
                m.GroupId == groupId && m.StartDate <= today && (m.EndDate == null || m.EndDate >= today)));

        return q
            .OrderBy(p => EF.Functions.Collate(p.Name, "Chinese_Taiwan_Stroke_CI_AS"))
            .Take(300)
            .Select(p => new PersonListItem(
                p.Id, p.Name, p.Nickname, p.Gender, p.BirthDate, SchoolGrade.Grade(p.SchoolEntryYear, today),
                p.FirstVisitDate, p.IsActive,
                p.Memberships
                    .Where(m => m.StartDate <= today && (m.EndDate == null || m.EndDate >= today))
                    .Select(m => m.Group.Name)
                    .ToList()))
            .ToListAsync();
    }

    static async Task<IResult> Get(int id, AppDbContext db, GroupAccess access, AppClock clock)
    {
        var today = clock.Today;
        var person = await access.People()
            .Where(p => p.Id == id)
            .Select(p => new PersonDetail(
                p.Id, p.Name, p.Nickname, p.Gender, p.BirthDate, SchoolGrade.Grade(p.SchoolEntryYear, today), p.Phone, p.Email, p.FirstVisitDate, p.ConsentAt,
                p.Notes, p.IsActive,
                p.Guardians
                    .OrderByDescending(g => g.IsPrimaryContact)
                    .Select(g => new GuardianDto(g.RelatedPersonId, g.RelatedPerson.Name, g.RelatedPerson.Phone, g.RelationType, g.IsPrimaryContact))
                    .ToList(),
                p.Memberships
                    .OrderByDescending(m => m.StartDate)
                    .Select(m => new MembershipDto(m.Id, m.GroupId, m.Group.Name, m.StartDate, m.EndDate))
                    .ToList(),
                new List<AttendanceHistoryItem>()))
            .FirstOrDefaultAsync();
        if (person is null) return Results.NotFound();

        var recent = await db.AttendanceRecords
            .Where(r => r.PersonId == id)
            .OrderByDescending(r => r.Session.SessionDate)
            .Take(12)
            .Select(r => new AttendanceHistoryItem(r.Session.SessionDate, r.Session.Group.Name, r.Status))
            .ToListAsync();

        return Results.Ok(person with { RecentAttendance = recent });
    }

    static async Task<IResult> Update(int id, UpdatePersonRequest req, AppDbContext db, GroupAccess access, AppClock clock)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Http.Invalid("name", "請輸入姓名");
        if (Http.TooLong(req.Name, 50) || Http.TooLong(req.Nickname, 50)) return Http.Invalid("name", "姓名最多 50 個字");
        if (Http.TooLong(req.Phone, 20)) return Http.Invalid("phone", "電話格式不正確");
        if (Http.TooLong(req.Email, 100)) return Http.Invalid("email", "Email 格式不正確");
        if (Http.TooLong(req.Notes, 500)) return Http.Invalid("notes", "備註最多 500 個字");
        if (!SchoolGrade.IsValid(req.Grade)) return Http.Invalid("grade", "年級不正確");

        var person = await access.People().FirstOrDefaultAsync(p => p.Id == id);
        if (person is null) return Results.NotFound();

        person.Name = req.Name.Trim();
        person.Nickname = Clean(req.Nickname);
        person.Gender = req.Gender;
        person.BirthDate = req.BirthDate;
        // Keep the stored entry year when the grade is unchanged, so editing other fields never shifts it.
        if (req.Grade != SchoolGrade.Grade(person.SchoolEntryYear, clock.Today))
            person.SchoolEntryYear = req.Grade is { } grade ? SchoolGrade.EntryYear(grade, clock.Today) : null;
        person.Phone = Clean(req.Phone);
        person.Email = Clean(req.Email);
        person.Notes = Clean(req.Notes);
        person.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>Moves a person to another group of the same ministry, e.g. moving up a class.</summary>
    static async Task<IResult> Move(int id, MovePersonRequest req, AppDbContext db, GroupAccess access)
    {
        var target = await access.FindGroupAsync(req.GroupId);
        if (target is null) return Results.NotFound();

        var person = await access.People()
            .Include(p => p.Memberships).ThenInclude(m => m.Group)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (person is null) return Results.NotFound();

        var current = person.Memberships
            .Where(m => m.Group.MinistryId == target.MinistryId && (m.EndDate == null || m.EndDate >= req.StartDate))
            .ToList();
        if (current.Any(m => m.GroupId == target.Id && m.StartDate <= req.StartDate))
            return Results.Problem("此成員已在該群組", statusCode: StatusCodes.Status409Conflict);

        foreach (var m in current)
        {
            if (m.StartDate >= req.StartDate) db.GroupMemberships.Remove(m);
            else m.EndDate = req.StartDate.AddDays(-1);
        }
        db.GroupMemberships.Add(new GroupMembership { GroupId = target.Id, PersonId = id, StartDate = req.StartDate });
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    static async Task<IResult> EndMembership(int id, int membershipId, AppDbContext db, GroupAccess access, AppClock clock)
    {
        var membership = await db.GroupMemberships.FirstOrDefaultAsync(m => m.Id == membershipId && m.PersonId == id);
        if (membership is null || await access.FindGroupAsync(membership.GroupId) is null) return Results.NotFound();

        var today = clock.Today;
        if (membership.StartDate > today) db.GroupMemberships.Remove(membership);
        else membership.EndDate = today;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
