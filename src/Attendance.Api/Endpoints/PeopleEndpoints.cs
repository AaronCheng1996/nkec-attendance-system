using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Endpoints;

public static class PeopleEndpoints
{
    public record PersonListItem(
        int Id, string Name, string? Nickname, Gender Gender, DateOnly? BirthDate, int? Grade, DateOnly? FirstVisitDate,
        bool IsActive, List<string> Groups, List<string> Guardians);

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

    /// <summary>An existing member entered by staff (newcomers are registered from the roll call page instead).</summary>
    /// <remarks>With <paramref name="GuardianName"/> the phone is the guardian's; without it, the member's own.</remarks>
    public record CreatePersonRequest(
        string Name, Gender Gender, DateOnly? BirthDate, int? Grade, string? Phone, string? Notes,
        int GroupId, DateOnly? StartDate, bool Consent, string? GuardianName = null, RelationType? GuardianRelation = null);

    public record GuardianRequest(string Name, string? Phone, RelationType RelationType, bool IsPrimaryContact);

    public static void MapPeopleEndpoints(this IEndpointRouteBuilder api)
    {
        var people = api.MapGroup("/people").RequireAuthorization(Policies.ManagePeople);
        people.MapGet("/", List);
        people.MapPost("/", Create);
        people.MapGet("/import/template", ImportTemplate);
        // Bearer-token auth is not sent automatically by browsers, so a form post needs no antiforgery token.
        people.MapPost("/import", Import).DisableAntiforgery();
        people.MapGet("/{id:int}", Get);
        people.MapPut("/{id:int}", Update);
        people.MapPost("/{id:int}/move", Move);
        people.MapDelete("/{id:int}/memberships/{membershipId:int}", EndMembership);
        people.MapPost("/{id:int}/guardians", AddGuardian);
        people.MapPut("/{id:int}/guardians/{guardianId:int}", UpdateGuardian);
        people.MapDelete("/{id:int}/guardians/{guardianId:int}", RemoveGuardian);
        people.MapPeopleBatchEndpoints();
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
            q = q.Where(p => p.Name.Contains(s) || (p.Nickname != null && p.Nickname.Contains(s))
                || p.Guardians.Any(g => g.RelatedPerson.Name.Contains(s)));
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
                    .ToList(),
                p.Guardians
                    .OrderByDescending(g => g.IsPrimaryContact)
                    .Select(g => g.RelatedPerson.Name)
                    .ToList()))
            .ToListAsync();
    }

    static async Task<IResult> Create(CreatePersonRequest req, AppDbContext db, GroupAccess access, AppClock clock)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Http.Invalid("name", "請輸入姓名");
        if (Http.TooLong(req.Name, 50)) return Http.Invalid("name", "姓名最多 50 個字");
        if (Http.TooLong(req.Phone, 20)) return Http.Invalid("phone", "電話格式不正確");
        if (Http.TooLong(req.Notes, 500)) return Http.Invalid("notes", "備註最多 500 個字");
        if (!SchoolGrade.IsValid(req.Grade)) return Http.Invalid("grade", "年級不正確");
        if (!Enum.IsDefined(req.Gender)) return Http.Invalid("gender", "性別不正確");
        if (Http.TooLong(req.GuardianName, 50)) return Http.Invalid("guardianName", "家長姓名最多 50 個字");
        if (req.GuardianRelation is { } relation && !Enum.IsDefined(relation))
            return Http.Invalid("guardianRelation", "家長關係不正確");
        if (!req.Consent) return Http.Invalid("consent", "需取得家長同意才能登記個人資料");

        var today = clock.Today;
        // A group is required: the people list only shows people who belong to one.
        var group = await access.FindGroupAsync(req.GroupId);
        if (group is null) return Http.Invalid("groupId", "請選擇群組");

        var person = new Person
        {
            Name = req.Name.Trim(),
            Gender = req.Gender,
            BirthDate = req.BirthDate,
            SchoolEntryYear = req.Grade is { } grade ? SchoolGrade.EntryYear(grade, today) : null,
            Notes = Clean(req.Notes),
            ConsentAt = DateTimeOffset.UtcNow,
            ConsentMethod = "Paper",
        };
        Guardians.AddContact(person, req.GuardianName, req.Phone, req.GuardianRelation);
        db.People.Add(person);
        db.GroupMemberships.Add(new GroupMembership { Group = group, Person = person, StartDate = req.StartDate ?? today });

        await db.SaveChangesAsync();
        return Results.Created($"/api/people/{person.Id}", new { person.Id });
    }

    const long MaxImportBytes = 5 * 1024 * 1024;

    static async Task<IResult> ImportTemplate(PersonImport import) =>
        Results.File(
            await import.TemplateAsync(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "成員匯入範本.xlsx");

    /// <summary>Checks an uploaded sheet; with <paramref name="commit"/> also creates the new members.</summary>
    static async Task<IResult> Import(
        IFormFile? file, [FromForm] DateOnly? startDate, [FromForm] bool consent, [FromForm] bool commit,
        PersonImport import, AppClock clock)
    {
        if (file is null || file.Length == 0) return Http.Invalid("file", "請選擇要匯入的 Excel 檔");
        if (file.Length > MaxImportBytes) return Http.Invalid("file", "檔案太大，最多 5 MB");
        if (commit && !consent) return Http.Invalid("consent", "需取得家長同意才能登記個人資料");

        try
        {
            await using var stream = file.OpenReadStream();
            return Results.Ok(await import.RunAsync(stream, startDate ?? clock.Today, clock.Today, commit));
        }
        catch (ImportFileException ex)
        {
            return Http.Invalid("file", ex.Message);
        }
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

        if (!MoveMember(db, person, target, req.StartDate))
            return Results.Problem("此成員已在該群組", statusCode: StatusCodes.Status409Conflict);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Ends the person's memberships in the target's ministry the day before <paramref name="startDate"/> and adds
    /// the new one. <paramref name="person"/> needs its memberships and their groups loaded.
    /// </summary>
    /// <returns>False when the person is already in the target group on that date (nothing changes).</returns>
    internal static bool MoveMember(AppDbContext db, Person person, Group target, DateOnly startDate)
    {
        var current = person.Memberships
            .Where(m => m.Group.MinistryId == target.MinistryId && (m.EndDate == null || m.EndDate >= startDate))
            .ToList();
        if (current.Any(m => m.GroupId == target.Id && m.StartDate <= startDate)) return false;

        foreach (var m in current)
        {
            if (m.StartDate >= startDate) db.GroupMemberships.Remove(m);
            else m.EndDate = startDate.AddDays(-1);
        }
        db.GroupMemberships.Add(new GroupMembership { GroupId = target.Id, PersonId = person.Id, StartDate = startDate });
        return true;
    }

    static IResult? ValidateGuardian(GuardianRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Http.Invalid("name", "請輸入家長姓名");
        if (Http.TooLong(req.Name, 50)) return Http.Invalid("name", "姓名最多 50 個字");
        if (Http.TooLong(req.Phone, 20)) return Http.Invalid("phone", "電話格式不正確");
        if (!Enum.IsDefined(req.RelationType)) return Http.Invalid("relationType", "家長關係不正確");
        return null;
    }

    static async Task<IResult> AddGuardian(int id, GuardianRequest req, AppDbContext db, GroupAccess access)
    {
        if (ValidateGuardian(req) is { } invalid) return invalid;
        var child = await access.People().Include(p => p.Guardians).FirstOrDefaultAsync(p => p.Id == id);
        if (child is null) return Results.NotFound();

        // The first guardian is always the main contact.
        var primary = req.IsPrimaryContact || child.Guardians.Count == 0;
        if (primary) child.Guardians.ForEach(g => g.IsPrimaryContact = false);
        var guardian = Guardians.New(req.Name, req.Phone, req.RelationType);
        child.Guardians.Add(new PersonRelationship
        {
            RelatedPerson = guardian, RelationType = req.RelationType, IsPrimaryContact = primary,
        });
        await db.SaveChangesAsync();
        return Results.Created($"/api/people/{guardian.Id}", new { guardian.Id });
    }

    static async Task<IResult> UpdateGuardian(
        int id, int guardianId, GuardianRequest req, AppDbContext db, GroupAccess access)
    {
        if (ValidateGuardian(req) is { } invalid) return invalid;
        var child = await access.People()
            .Include(p => p.Guardians).ThenInclude(g => g.RelatedPerson)
            .FirstOrDefaultAsync(p => p.Id == id);
        var link = child?.Guardians.FirstOrDefault(g => g.RelatedPersonId == guardianId);
        if (child is null || link is null) return Results.NotFound();

        link.RelatedPerson.Name = req.Name.Trim();
        link.RelatedPerson.Phone = Clean(req.Phone);
        link.RelationType = req.RelationType;
        // There is always one main contact, so this can only hand the role over, never clear it.
        if (req.IsPrimaryContact) child.Guardians.ForEach(g => g.IsPrimaryContact = g == link);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>Unlinks a guardian; the guardian's own record is deleted too unless something else refers to it.</summary>
    static async Task<IResult> RemoveGuardian(int id, int guardianId, AppDbContext db, GroupAccess access)
    {
        var child = await access.People().Include(p => p.Guardians).FirstOrDefaultAsync(p => p.Id == id);
        var link = child?.Guardians.FirstOrDefault(g => g.RelatedPersonId == guardianId);
        if (child is null || link is null) return Results.NotFound();

        child.Guardians.Remove(link);
        if (link.IsPrimaryContact && child.Guardians.FirstOrDefault() is { } next) next.IsPrimaryContact = true;

        var inUse = await db.PersonRelationships.AnyAsync(r =>
                (r.RelatedPersonId == guardianId && r.PersonId != id) || r.PersonId == guardianId)
            || await db.GroupMemberships.AnyAsync(m => m.PersonId == guardianId)
            || await db.AttendanceRecords.AnyAsync(r => r.PersonId == guardianId)
            || await db.Users.AnyAsync(u => u.PersonId == guardianId);
        if (!inUse && await db.People.FindAsync(guardianId) is { } orphan) db.People.Remove(orphan);

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
