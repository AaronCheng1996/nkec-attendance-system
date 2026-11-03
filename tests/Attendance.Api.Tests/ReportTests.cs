using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Domain;
using Attendance.Api.Endpoints;
using ClosedXML.Excel;

namespace Attendance.Api.Tests;

public class ReportTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    /// <summary>Records one session directly in the database. Statuses are matched to people by position.</summary>
    async Task AddSessionAsync(int groupId, DateOnly date, int[] people, AttendanceStatus[] statuses, bool cancelled = false)
    {
        var markedBy = (await app.CreateUserAsync(Roles.Admin)).Id;
        await app.WithDbAsync(async db =>
        {
            var session = new Session { GroupId = groupId, SessionDate = date, IsCancelled = cancelled };
            for (var i = 0; i < people.Length; i++)
                session.Records.Add(new AttendanceRecord
                {
                    PersonId = people[i], Status = statuses[i], MarkedBy = markedBy, MarkedAt = DateTimeOffset.UtcNow,
                });
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Attendance_rate_leaves_out_excused_and_cancelled_sessions()
    {
        var group = await app.CreateGroupAsync();
        int[] people = [await app.AddMemberAsync(group.Id, "甲"), await app.AddMemberAsync(group.Id, "乙"), await app.AddMemberAsync(group.Id, "丙")];
        await AddSessionAsync(group.Id, Sunday, people, [AttendanceStatus.Present, AttendanceStatus.Absent, AttendanceStatus.Excused]);
        await AddSessionAsync(group.Id, Sunday.AddDays(7), people, [AttendanceStatus.Absent, AttendanceStatus.Absent, AttendanceStatus.Absent], cancelled: true);
        var client = await app.ClientForAsync(Roles.Admin);

        var summary = await client.GetFromJsonAsync<ReportEndpoints.ReportSummary>(
            $"/api/reports/summary?from={Sunday:yyyy-MM-dd}&to={Sunday.AddDays(7):yyyy-MM-dd}&groupId={group.Id}", Json, Ct);

        Assert.Equal(1, summary!.Sessions);
        Assert.Equal(0.5, summary.AvgRate);
        Assert.Equal(1, summary.DistinctAttendees);
    }

    [Fact]
    public async Task Care_list_shows_people_absent_for_the_last_three_sessions()
    {
        var group = await app.CreateGroupAsync();
        var missing = await app.AddMemberAsync(group.Id, "很久沒來");
        var back = await app.AddMemberAsync(group.Id, "上週有來");
        var today = app.Today;
        await AddSessionAsync(group.Id, today.AddDays(-28), [missing, back], [AttendanceStatus.Present, AttendanceStatus.Present]);
        await AddSessionAsync(group.Id, today.AddDays(-21), [missing, back], [AttendanceStatus.Absent, AttendanceStatus.Absent]);
        await AddSessionAsync(group.Id, today.AddDays(-14), [missing, back], [AttendanceStatus.Absent, AttendanceStatus.Absent]);
        await AddSessionAsync(group.Id, today.AddDays(-7), [missing, back], [AttendanceStatus.Absent, AttendanceStatus.Present]);
        var client = await app.ClientForAsync(Roles.Admin);

        var absentees = await client.GetFromJsonAsync<List<ReportEndpoints.AbsenteeDto>>(
            $"/api/reports/absentees?groupId={group.Id}", Json, Ct);

        var absentee = Assert.Single(absentees!);
        Assert.Equal((missing, 3, today.AddDays(-28)), (absentee.PersonId, absentee.ConsecutiveAbsences, absentee.LastPresentDate));
    }

    [Fact]
    public async Task Excel_export_has_a_summary_and_a_sheet_per_group()
    {
        var group = await app.CreateGroupAsync("第一堂");
        var person = await app.AddMemberAsync(group.Id, "甲");
        await AddSessionAsync(group.Id, Sunday, [person], [AttendanceStatus.Present]);
        var client = await app.ClientForAsync(Roles.Admin);

        var res = await client.GetAsync($"/api/reports/export?from={Sunday:yyyy-MM-dd}&to={Sunday:yyyy-MM-dd}", Ct);

        res.EnsureSuccessStatusCode();
        using var workbook = new XLWorkbook(await res.Content.ReadAsStreamAsync(Ct));
        Assert.True(workbook.Worksheets.Contains("場次統計"));
        Assert.Equal("出", workbook.Worksheet("明細-第一堂").Cell(2, 2).GetString());
    }
}
