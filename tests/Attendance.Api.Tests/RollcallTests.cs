using System.Net;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Tests;

public class RollcallTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    static string Url(int groupId) => $"/api/groups/{groupId}/rollcall?date={Sunday:yyyy-MM-dd}";

    static object Marks(params (int PersonId, AttendanceStatus Status)[] marks) =>
        new { records = marks.Select(m => new { personId = m.PersonId, status = m.Status }) };

    async Task<(Group Group, HttpClient Client)> GroupWithLeaderAsync()
    {
        var group = await app.CreateGroupAsync();
        var client = await app.ClientForAsync(await app.CreateUserAsync(Roles.Leader, groupIds: [group.Id]));
        return (group, client);
    }

    [Fact]
    public async Task Roster_only_lists_members_on_that_date()
    {
        var (group, client) = await GroupWithLeaderAsync();
        var current = await app.AddMemberAsync(group.Id, "在班上");
        await app.AddMemberAsync(group.Id, "之後才來", start: Sunday.AddDays(7));
        await app.AddMemberAsync(group.Id, "已經升班", end: Sunday.AddDays(-7));

        var roll = await client.GetFromJsonAsync<RollcallDto>(Url(group.Id), Json, Ct);

        Assert.Equal([current], roll!.People.Select(p => p.PersonId));
    }

    [Fact]
    public async Task Members_left_unmarked_are_saved_as_absent()
    {
        var (group, client) = await GroupWithLeaderAsync();
        var present = await app.AddMemberAsync(group.Id, "甲");
        var excused = await app.AddMemberAsync(group.Id, "乙");
        var absent = await app.AddMemberAsync(group.Id, "丙");

        var res = await client.PutAsJsonAsync(
            Url(group.Id), Marks((present, AttendanceStatus.Present), (excused, AttendanceStatus.Excused)), Json, Ct);

        res.EnsureSuccessStatusCode();
        var statuses = (await res.Content.ReadFromJsonAsync<RollcallDto>(Json, Ct))!.People
            .ToDictionary(p => p.PersonId, p => p.Status);
        Assert.Equal(AttendanceStatus.Present, statuses[present]);
        Assert.Equal(AttendanceStatus.Excused, statuses[excused]);
        Assert.Equal(AttendanceStatus.Absent, statuses[absent]);
    }

    [Fact]
    public async Task Saving_again_overwrites_the_previous_marks()
    {
        var (group, client) = await GroupWithLeaderAsync();
        var person = await app.AddMemberAsync(group.Id, "甲");

        (await client.PutAsJsonAsync(Url(group.Id), Marks((person, AttendanceStatus.Present)), Json, Ct)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync(Url(group.Id), Marks(), Json, Ct)).EnsureSuccessStatusCode();

        var records = await app.WithDbAsync(db => db.AttendanceRecords.Where(r => r.PersonId == person).ToListAsync());
        Assert.Equal(AttendanceStatus.Absent, Assert.Single(records).Status);
    }

    [Fact]
    public async Task People_outside_the_group_are_rejected()
    {
        var (group, client) = await GroupWithLeaderAsync();
        var otherGroup = await app.CreateGroupAsync("別班");
        var outsider = await app.AddMemberAsync(otherGroup.Id, "別班的人");

        var res = await client.PutAsJsonAsync(Url(group.Id), Marks((outsider, AttendanceStatus.Present)), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Newcomer_is_registered_with_grade_and_marked_present()
    {
        var (group, client) = await GroupWithLeaderAsync();

        var res = await client.PostAsJsonAsync($"/api/groups/{group.Id}/newcomers?date={Sunday:yyyy-MM-dd}",
            new { name = "新朋友", birthDate = "2018-05-01", grade = 3, consent = true }, Json, Ct);

        res.EnsureSuccessStatusCode();
        var newcomer = Assert.Single((await res.Content.ReadFromJsonAsync<RollcallDto>(Json, Ct))!.People);
        Assert.Equal(("新朋友", 3, AttendanceStatus.Present), (newcomer.Name, newcomer.Grade, newcomer.Status));
        Assert.Equal(Sunday, newcomer.FirstVisitDate);

        var person = await app.WithDbAsync(db => db.People.SingleAsync(p => p.Id == newcomer.PersonId));
        Assert.Equal(SchoolGrade.EntryYear(3, Sunday), person.SchoolEntryYear);
        Assert.NotNull(person.ConsentAt);
        Assert.Null(person.Phone);
    }

    [Fact]
    public async Task Newcomer_phone_is_optional_and_shown_on_the_care_list()
    {
        var (group, client) = await GroupWithLeaderAsync();

        var res = await client.PostAsJsonAsync($"/api/groups/{group.Id}/newcomers?date={Sunday:yyyy-MM-dd}",
            new { name = "有電話", guardianPhone = " 0912345678 ", consent = true }, Json, Ct);

        res.EnsureSuccessStatusCode();
        var id = Assert.Single((await res.Content.ReadFromJsonAsync<RollcallDto>(Json, Ct))!.People).PersonId;
        var phone = await app.WithDbAsync(db => db.People.Where(p => p.Id == id).Select(p => p.Phone).SingleAsync());
        Assert.Equal("0912345678", phone);
    }

    [Fact]
    public async Task Newcomer_phone_is_limited_to_20_characters()
    {
        var (group, client) = await GroupWithLeaderAsync();

        var res = await client.PostAsJsonAsync($"/api/groups/{group.Id}/newcomers?date={Sunday:yyyy-MM-dd}",
            new { name = "小明", guardianPhone = new string('1', 21), consent = true }, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("", 3, true)]
    [InlineData("小明", 20, true)]
    [InlineData("小明", 3, false)]
    public async Task Invalid_newcomers_are_rejected(string name, int grade, bool consent)
    {
        var (group, client) = await GroupWithLeaderAsync();

        var res = await client.PostAsJsonAsync($"/api/groups/{group.Id}/newcomers?date={Sunday:yyyy-MM-dd}",
            new { name, grade, consent }, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
