using Attendance.Api.Domain;

namespace Attendance.Api.Data;

/// <summary>Fake children, parents and eight weeks of attendance so the screens and reports have something to show.</summary>
static class DemoData
{
    const string Marker = "[示範資料]";

    public static async Task SeedAsync(AppDbContext db, DateOnly today, string markedBy)
    {
        var random = new Random(2026);
        var number = 0;

        var ministry = new Ministry { Name = "兒童主日學", SortOrder = 1 };
        var groups = new[]
        {
            new Group { Ministry = ministry, Name = "第一堂兒童班", DefaultDayOfWeek = DayOfWeek.Sunday, DefaultStartTime = new TimeOnly(9, 0) },
            new Group { Ministry = ministry, Name = "第二堂兒童班", DefaultDayOfWeek = DayOfWeek.Sunday, DefaultStartTime = new TimeOnly(11, 0) },
        };
        db.Groups.AddRange(groups);

        var lastSunday = today;
        while (lastSunday.DayOfWeek != DayOfWeek.Sunday) lastSunday = lastSunday.AddDays(-1);
        var sundays = Enumerable.Range(0, 8).Select(i => lastSunday.AddDays(-7 * i)).Reverse().ToList();
        var longAgo = sundays[0].AddDays(-180);

        foreach (var group in groups)
        {
            var members = new List<(Person Person, GroupMembership Membership)>();

            // 28 regular children plus 2 newcomers who joined in recent weeks.
            for (var i = 0; i < 30; i++)
            {
                var isNewcomer = i >= 28;
                var start = isNewcomer ? sundays[i == 28 ? 5 : 7] : longAgo;
                // Plainly fake names and phone numbers (學生001 / 家長001 / 0900000001), never realistic ones.
                var n = (++number).ToString("000");
                var child = new Person
                {
                    Name = $"學生{n}",
                    Gender = random.Next(2) == 0 ? Gender.Male : Gender.Female,
                    BirthDate = today.AddDays(-random.Next(4 * 365, 12 * 365)),
                    FirstVisitDate = start,
                    ConsentAt = DateTimeOffset.UtcNow,
                    ConsentMethod = "Paper",
                    Notes = Marker,
                };
                var isMother = random.Next(3) > 0;
                child.Guardians.Add(new PersonRelationship
                {
                    RelatedPerson = new Person
                    {
                        Name = $"家長{n}",
                        Gender = isMother ? Gender.Female : Gender.Male,
                        Phone = $"0900000{n}",
                        Notes = Marker,
                    },
                    RelationType = isMother ? RelationType.Mother : RelationType.Father,
                    IsPrimaryContact = true,
                });

                var membership = new GroupMembership { Group = group, Person = child, StartDate = start };
                db.GroupMemberships.Add(membership);
                members.Add((child, membership));
            }

            foreach (var sunday in sundays)
            {
                var session = new Session { Group = group, SessionDate = sunday, StartTime = group.DefaultStartTime };
                db.Sessions.Add(session);

                for (var i = 0; i < members.Count; i++)
                {
                    var (person, membership) = members[i];
                    if (membership.StartDate > sunday) continue;

                    // The first two children have stopped coming in the last four weeks, for the care list.
                    var status = i < 2 && sunday >= sundays[4] ? AttendanceStatus.Absent
                        : sunday == membership.StartDate ? AttendanceStatus.Present
                        : random.Next(100) switch
                        {
                            < 82 => AttendanceStatus.Present,
                            < 88 => AttendanceStatus.Excused,
                            _ => AttendanceStatus.Absent,
                        };

                    db.AttendanceRecords.Add(new AttendanceRecord
                    {
                        Session = session,
                        Person = person,
                        Status = status,
                        MarkedBy = markedBy,
                        MarkedAt = DateTimeOffset.UtcNow,
                    });
                }
            }
        }

        await db.SaveChangesAsync();
    }
}
