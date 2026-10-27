using Attendance.Api.Data;
using Attendance.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Services;

public record RosterPerson(
    int PersonId, string Name, string? Nickname, Gender Gender, DateOnly? BirthDate, DateOnly? FirstVisitDate,
    bool IsVisitor, AttendanceStatus? Status);

public record RollcallDto(
    int GroupId, string GroupName, DateOnly Date, int? SessionId, bool IsCancelled,
    DateTimeOffset? LastMarkedAt, string? LastMarkedBy, List<RosterPerson> People);

public record RollcallItem(int PersonId, AttendanceStatus Status);

public record NewcomerRequest(
    string Name, string? Nickname, Gender Gender, DateOnly? BirthDate,
    string? GuardianName, string? GuardianPhone, RelationType? GuardianRelation,
    string? Notes, bool Consent);

public class RollcallService(AppDbContext db)
{
    record RecordRow(
        int PersonId, AttendanceStatus Status, DateTimeOffset MarkedAt, string MarkedByName,
        string Name, string? Nickname, Gender Gender, DateOnly? BirthDate, DateOnly? FirstVisitDate);

    public IQueryable<GroupMembership> ActiveMemberships(int groupId, DateOnly date) =>
        db.GroupMemberships.Where(m =>
            m.GroupId == groupId && m.StartDate <= date && (m.EndDate == null || m.EndDate >= date) && m.Person.IsActive);

    Task<Session?> FindSessionAsync(Group group, DateOnly date) =>
        db.Sessions.FirstOrDefaultAsync(s =>
            s.GroupId == group.Id && s.SessionDate == date && s.StartTime == group.DefaultStartTime);

    async Task<Session> GetOrCreateSessionAsync(Group group, DateOnly date)
    {
        if (await FindSessionAsync(group, date) is { } existing) return existing;

        var session = new Session { GroupId = group.Id, SessionDate = date, StartTime = group.DefaultStartTime };
        db.Sessions.Add(session);
        try
        {
            await db.SaveChangesAsync();
            return session;
        }
        catch (DbUpdateException)
        {
            // Another teacher created it at the same moment.
            db.Entry(session).State = EntityState.Detached;
            return await FindSessionAsync(group, date) ?? throw new InvalidOperationException("Session could not be created.");
        }
    }

    /// <summary>The roster for a date: active members plus anyone else already recorded for that session.</summary>
    public async Task<RollcallDto> BuildAsync(Group group, DateOnly date)
    {
        var members = await ActiveMemberships(group.Id, date)
            .Select(m => new RosterPerson(
                m.PersonId, m.Person.Name, m.Person.Nickname, m.Person.Gender, m.Person.BirthDate,
                m.Person.FirstVisitDate, false, null))
            .ToListAsync();

        var session = await FindSessionAsync(group, date);
        var records = session is null
            ? []
            : await db.AttendanceRecords
                .Where(r => r.SessionId == session.Id)
                .Select(r => new RecordRow(
                    r.PersonId, r.Status, r.MarkedAt, r.MarkedByUser.DisplayName,
                    r.Person.Name, r.Person.Nickname, r.Person.Gender, r.Person.BirthDate, r.Person.FirstVisitDate))
                .ToListAsync();

        var recordById = records.ToDictionary(r => r.PersonId);
        var people = members
            .DistinctBy(m => m.PersonId)
            .Select(m => recordById.TryGetValue(m.PersonId, out var r) ? m with { Status = r.Status } : m)
            .ToList();
        var memberIds = people.Select(p => p.PersonId).ToHashSet();
        people.AddRange(records
            .Where(r => !memberIds.Contains(r.PersonId))
            .Select(r => new RosterPerson(r.PersonId, r.Name, r.Nickname, r.Gender, r.BirthDate, r.FirstVisitDate, true, r.Status)));
        people.Sort((a, b) => NameOrder.Comparer.Compare(a.Name, b.Name));

        var last = records.MaxBy(r => r.MarkedAt);
        return new RollcallDto(group.Id, group.Name, date, session?.Id, session?.IsCancelled ?? false,
            last?.MarkedAt, last?.MarkedByName, people);
    }

    /// <summary>Saves a roll call. Members missing from <paramref name="items"/> are recorded as absent.</summary>
    /// <returns>An error message, or null on success.</returns>
    public async Task<string?> SaveAsync(Group group, DateOnly date, IReadOnlyCollection<RollcallItem> items, string userId)
    {
        if (items.Any(i => !Enum.IsDefined(i.Status))) return "點名狀態不正確";

        var memberIds = (await ActiveMemberships(group.Id, date).Select(m => m.PersonId).ToListAsync()).ToHashSet();
        var session = await FindSessionAsync(group, date);
        var existing = session is null
            ? new Dictionary<int, AttendanceRecord>()
            : await db.AttendanceRecords.Where(r => r.SessionId == session.Id).ToDictionaryAsync(r => r.PersonId);

        if (items.Any(i => !memberIds.Contains(i.PersonId) && !existing.ContainsKey(i.PersonId)))
            return "名單中有不屬於此群組的成員，請重新整理後再試";

        session ??= await GetOrCreateSessionAsync(group, date);

        var statuses = items.GroupBy(i => i.PersonId).ToDictionary(g => g.Key, g => g.Last().Status);
        foreach (var id in memberIds) statuses.TryAdd(id, AttendanceStatus.Absent);

        var now = DateTimeOffset.UtcNow;
        foreach (var (personId, status) in statuses)
        {
            if (existing.TryGetValue(personId, out var record))
            {
                if (record.Status == status) continue;
                record.Status = status;
                record.MarkedBy = userId;
                record.MarkedAt = now;
            }
            else
            {
                db.AttendanceRecords.Add(new AttendanceRecord
                {
                    SessionId = session.Id,
                    PersonId = personId,
                    Status = status,
                    IsVisitor = !memberIds.Contains(personId),
                    MarkedBy = userId,
                    MarkedAt = now,
                });
            }
        }

        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Registers a first-time visitor: person, optional guardian, membership and a present mark.</summary>
    public async Task<int> AddNewcomerAsync(Group group, DateOnly date, NewcomerRequest req, string userId)
    {
        var session = await GetOrCreateSessionAsync(group, date);
        var now = DateTimeOffset.UtcNow;

        var child = new Person
        {
            Name = req.Name.Trim(),
            Nickname = Clean(req.Nickname),
            Gender = req.Gender,
            BirthDate = req.BirthDate,
            FirstVisitDate = date,
            Notes = Clean(req.Notes),
            ConsentAt = now,
            ConsentMethod = "App",
        };

        if (Clean(req.GuardianName) is { } guardianName)
        {
            child.Guardians.Add(new PersonRelationship
            {
                RelatedPerson = new Person { Name = guardianName, Phone = Clean(req.GuardianPhone) },
                RelationType = req.GuardianRelation ?? RelationType.Guardian,
                IsPrimaryContact = true,
            });
        }
        else
        {
            child.Phone = Clean(req.GuardianPhone);
        }

        db.People.Add(child);
        db.GroupMemberships.Add(new GroupMembership { Group = group, Person = child, StartDate = date });
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            SessionId = session.Id,
            Person = child,
            Status = AttendanceStatus.Present,
            MarkedBy = userId,
            MarkedAt = now,
        });

        await db.SaveChangesAsync();
        return child.Id;
    }

    static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
