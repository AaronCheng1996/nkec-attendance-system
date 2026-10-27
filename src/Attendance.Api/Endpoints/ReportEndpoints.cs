using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Endpoints;

public static class ReportEndpoints
{
    /// <param name="Rate">Present ÷ (Present + Absent). Excused absences are left out; null when nobody was expected.</param>
    public record SessionStat(
        int SessionId, DateOnly Date, int GroupId, string GroupName, int Present, int Absent, int Excused, double? Rate);

    public record ReportSummary(
        int Sessions, double AvgPresent, double? AvgRate, int DistinctAttendees, int Newcomers, List<SessionStat> SessionStats);

    public record AbsenteeDto(
        int PersonId, string Name, string GroupName, int ConsecutiveAbsences, DateOnly? LastPresentDate,
        string? ContactName, string? ContactPhone);

    public record NewcomerStat(
        int PersonId, string Name, DateOnly FirstVisitDate, string? GroupName, int TimesPresent, int SessionsSince,
        DateOnly? LastPresentDate);

    public static void MapReportEndpoints(this IEndpointRouteBuilder api)
    {
        var reports = api.MapGroup("/reports").RequireAuthorization(Policies.Manager);
        reports.MapGet("/summary", Summary);
        reports.MapGet("/absentees", Absentees);
        reports.MapGet("/newcomers", Newcomers);
        reports.MapGet("/export", Export);
    }

    static IQueryable<Session> SessionsInRange(AppDbContext db, GroupAccess access, DateOnly from, DateOnly to, int? groupId)
    {
        var groupIds = access.GroupIds();
        return db.Sessions.Where(s =>
            !s.IsCancelled && s.SessionDate >= from && s.SessionDate <= to
            && groupIds.Contains(s.GroupId) && (groupId == null || s.GroupId == groupId));
    }

    static async Task<List<SessionStat>> SessionStatsAsync(IQueryable<Session> sessions)
    {
        var rows = await sessions
            .OrderBy(s => s.SessionDate).ThenBy(s => s.StartTime)
            .Select(s => new
            {
                s.Id,
                s.SessionDate,
                s.GroupId,
                GroupName = s.Group.Name,
                Present = s.Records.Count(r => r.Status == AttendanceStatus.Present),
                Absent = s.Records.Count(r => r.Status == AttendanceStatus.Absent),
                Excused = s.Records.Count(r => r.Status == AttendanceStatus.Excused),
            })
            .ToListAsync();

        return rows.Select(r => new SessionStat(
                r.Id, r.SessionDate, r.GroupId, r.GroupName, r.Present, r.Absent, r.Excused,
                r.Present + r.Absent == 0 ? null : (double)r.Present / (r.Present + r.Absent)))
            .ToList();
    }

    static async Task<ReportSummary> Summary(
        DateOnly from, DateOnly to, int? groupId, AppDbContext db, GroupAccess access)
    {
        var sessions = SessionsInRange(db, access, from, to, groupId);
        var stats = await SessionStatsAsync(sessions);

        var distinctAttendees = await db.AttendanceRecords
            .Where(r => r.Status == AttendanceStatus.Present && sessions.Any(s => s.Id == r.SessionId))
            .Select(r => r.PersonId)
            .Distinct()
            .CountAsync();

        var groupIds = access.GroupIds();
        var newcomers = await db.People.CountAsync(p =>
            p.FirstVisitDate >= from && p.FirstVisitDate <= to
            && p.Memberships.Any(m => groupIds.Contains(m.GroupId) && (groupId == null || m.GroupId == groupId)));

        var rates = stats.Where(s => s.Rate is not null).Select(s => s.Rate!.Value).ToList();
        return new ReportSummary(
            stats.Count,
            stats.Count == 0 ? 0 : stats.Average(s => s.Present),
            rates.Count == 0 ? null : rates.Average(),
            distinctAttendees,
            newcomers,
            stats);
    }

    /// <summary>People whose most recent <paramref name="minAbsences"/> sessions were all missed (excused ones skipped).</summary>
    static async Task<List<AbsenteeDto>> Absentees(
        AppDbContext db, GroupAccess access, AppClock clock, int? groupId, int minAbsences = 3)
    {
        var today = clock.Today;
        var since = today.AddDays(-180);
        var groupIds = access.GroupIds();

        var members = await db.GroupMemberships
            .Where(m => groupIds.Contains(m.GroupId) && (groupId == null || m.GroupId == groupId)
                && m.StartDate <= today && (m.EndDate == null || m.EndDate >= today) && m.Person.IsActive)
            .Select(m => new
            {
                m.PersonId,
                m.GroupId,
                m.Person.Name,
                GroupName = m.Group.Name,
                m.Person.Phone,
                Contact = m.Person.Guardians
                    .OrderByDescending(g => g.IsPrimaryContact)
                    .Select(g => new { g.RelatedPerson.Name, g.RelatedPerson.Phone })
                    .FirstOrDefault(),
            })
            .ToListAsync();

        var history = (await db.AttendanceRecords
                .Where(r => !r.Session.IsCancelled && r.Session.SessionDate >= since && r.Session.SessionDate <= today
                    && r.Status != AttendanceStatus.Excused
                    && groupIds.Contains(r.Session.GroupId) && (groupId == null || r.Session.GroupId == groupId))
                .Select(r => new { r.PersonId, r.Session.GroupId, r.Session.SessionDate, r.Status })
                .ToListAsync())
            .ToLookup(r => (r.PersonId, r.GroupId));

        var result = new List<AbsenteeDto>();
        foreach (var m in members)
        {
            var records = history[(m.PersonId, m.GroupId)].OrderByDescending(r => r.SessionDate).ToList();
            var streak = records.TakeWhile(r => r.Status == AttendanceStatus.Absent).Count();
            if (streak < Math.Max(minAbsences, 1)) continue;

            result.Add(new AbsenteeDto(
                m.PersonId, m.Name, m.GroupName, streak,
                records.FirstOrDefault(r => r.Status == AttendanceStatus.Present)?.SessionDate,
                m.Contact?.Name, m.Contact?.Phone ?? m.Phone));
        }

        return result
            .OrderByDescending(r => r.ConsecutiveAbsences)
            .ThenBy(r => r.Name, NameOrder.Comparer)
            .ToList();
    }

    static async Task<List<NewcomerStat>> Newcomers(
        DateOnly from, DateOnly to, int? groupId, AppDbContext db, GroupAccess access)
    {
        var groupIds = access.GroupIds();
        var people = await db.People
            .Where(p => p.FirstVisitDate >= from && p.FirstVisitDate <= to
                && p.Memberships.Any(m => groupIds.Contains(m.GroupId) && (groupId == null || m.GroupId == groupId)))
            .Select(p => new
            {
                p.Id,
                p.Name,
                FirstVisitDate = p.FirstVisitDate!.Value,
                Group = p.Memberships
                    .OrderByDescending(m => m.StartDate)
                    .Select(m => new { m.GroupId, m.Group.Name })
                    .FirstOrDefault(),
                TimesPresent = db.AttendanceRecords.Count(r => r.PersonId == p.Id && r.Status == AttendanceStatus.Present),
                LastPresentDate = db.AttendanceRecords
                    .Where(r => r.PersonId == p.Id && r.Status == AttendanceStatus.Present)
                    .Max(r => (DateOnly?)r.Session.SessionDate),
            })
            .ToListAsync();

        // How many sessions each newcomer's group has held since their first visit.
        var newcomerGroupIds = people.Where(p => p.Group != null).Select(p => p.Group!.GroupId).Distinct().ToList();
        var sessionDates = (await db.Sessions
                .Where(s => !s.IsCancelled && s.SessionDate >= from && newcomerGroupIds.Contains(s.GroupId))
                .Select(s => new { s.GroupId, s.SessionDate })
                .ToListAsync())
            .ToLookup(s => s.GroupId, s => s.SessionDate);

        return people
            .Select(p => new NewcomerStat(
                p.Id, p.Name, p.FirstVisitDate, p.Group?.Name, p.TimesPresent,
                p.Group is null ? 0 : sessionDates[p.Group.GroupId].Count(d => d >= p.FirstVisitDate),
                p.LastPresentDate))
            .OrderByDescending(p => p.FirstVisitDate)
            .ThenBy(p => p.Name, NameOrder.Comparer)
            .ToList();
    }

    static async Task<IResult> Export(DateOnly from, DateOnly to, int? groupId, AppDbContext db, GroupAccess access)
    {
        var sessions = SessionsInRange(db, access, from, to, groupId);
        var stats = await SessionStatsAsync(sessions);
        var detail = await db.AttendanceRecords
            .Where(r => sessions.Any(s => s.Id == r.SessionId))
            .Select(r => new { r.Session.GroupId, GroupName = r.Session.Group.Name, r.Session.SessionDate, r.PersonId, r.Person.Name, r.Status })
            .ToListAsync();

        using var workbook = new XLWorkbook();

        var summary = workbook.AddWorksheet("場次統計");
        WriteRow(summary, 1, "日期", "群組", "出席", "缺席", "請假", "出席率");
        summary.Row(1).Style.Font.Bold = true;
        for (var i = 0; i < stats.Count; i++)
        {
            var s = stats[i];
            var row = i + 2;
            summary.Cell(row, 1).Value = s.Date.ToString("yyyy-MM-dd");
            summary.Cell(row, 2).Value = s.GroupName;
            summary.Cell(row, 3).Value = s.Present;
            summary.Cell(row, 4).Value = s.Absent;
            summary.Cell(row, 5).Value = s.Excused;
            if (s.Rate is { } rate) summary.Cell(row, 6).Value = rate;
        }
        summary.Column(6).Style.NumberFormat.Format = "0.0%";
        summary.Columns().AdjustToContents();

        foreach (var group in detail.GroupBy(d => (d.GroupId, d.GroupName)).OrderBy(g => g.Key.GroupName))
        {
            var sheet = workbook.AddWorksheet(SheetName(workbook, "明細-" + group.Key.GroupName));
            var dates = group.Select(d => d.SessionDate).Distinct().Order().ToList();

            sheet.Cell(1, 1).Value = "姓名";
            for (var c = 0; c < dates.Count; c++) sheet.Cell(1, c + 2).Value = dates[c].ToString("MM/dd");
            sheet.Cell(1, dates.Count + 2).Value = "出席次數";
            sheet.Row(1).Style.Font.Bold = true;

            var row = 2;
            foreach (var person in group.GroupBy(d => (d.PersonId, d.Name)).OrderBy(p => p.Key.Name, NameOrder.Comparer))
            {
                var byDate = person.ToDictionary(d => d.SessionDate, d => d.Status);
                sheet.Cell(row, 1).Value = person.Key.Name;
                for (var c = 0; c < dates.Count; c++)
                    if (byDate.TryGetValue(dates[c], out var status))
                        sheet.Cell(row, c + 2).Value = status switch
                        {
                            AttendanceStatus.Present => "出",
                            AttendanceStatus.Excused => "假",
                            _ => "缺",
                        };
                sheet.Cell(row, dates.Count + 2).Value = byDate.Values.Count(s => s == AttendanceStatus.Present);
                row++;
            }
            sheet.SheetView.FreezeRows(1);
            sheet.SheetView.FreezeColumns(1);
            sheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Results.File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"attendance_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
    }

    static void WriteRow(IXLWorksheet sheet, int row, params string[] values)
    {
        for (var i = 0; i < values.Length; i++) sheet.Cell(row, i + 1).Value = values[i];
    }

    /// <summary>Excel sheet names: max 31 chars, no : \ / ? * [ ], unique within the workbook.</summary>
    static string SheetName(XLWorkbook workbook, string name)
    {
        var clean = new string(name.Select(ch => ":\\/?*[]".Contains(ch) ? '_' : ch).ToArray());
        clean = clean.Length > 31 ? clean[..31] : clean;
        var candidate = clean;
        for (var n = 2; workbook.Worksheets.Contains(candidate); n++)
            candidate = $"{(clean.Length > 28 ? clean[..28] : clean)}({n})";
        return candidate;
    }
}
