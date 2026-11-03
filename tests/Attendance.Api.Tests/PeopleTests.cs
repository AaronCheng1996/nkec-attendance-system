using System.Net;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Endpoints;
using Attendance.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Tests;

public class PeopleTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    static object UpdateRequest(string name, int? grade = null) =>
        new { name, gender = "Unknown", grade, isActive = true };

    [Fact]
    public async Task Search_finds_people_by_name()
    {
        var group = await app.CreateGroupAsync();
        // Test classes share one database, so use a name no other test creates.
        var wanted = await app.AddMemberAsync(group.Id, "王搜尋測試");
        await app.AddMemberAsync(group.Id, "李大華");
        var client = await app.ClientForAsync(Roles.Coordinator);

        var people = await client.GetFromJsonAsync<List<PeopleEndpoints.PersonListItem>>("/api/people?search=搜尋測試", Json, Ct);

        Assert.Equal([wanted], people!.Select(p => p.Id));
    }

    static object NewMember(int groupId, string name, bool consent = true) => new
    {
        name, gender = "Female", birthDate = "2019-03-01", grade = 2, phone = "0911222333", groupId, consent,
    };

    [Fact]
    public async Task Staff_add_a_member_straight_into_a_group()
    {
        var group = await app.CreateGroupAsync();
        var client = await app.ClientForAsync(Roles.Admin);

        var res = await client.PostAsJsonAsync("/api/people", NewMember(group.Id, "手動新增"), Json, Ct);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var roll = await client.GetFromJsonAsync<RollcallDto>(
            $"/api/groups/{group.Id}/rollcall?date={app.Today:yyyy-MM-dd}", Json, Ct);
        var member = Assert.Single(roll!.People);
        Assert.Equal(("手動新增", 2), (member.Name, member.Grade));
        // Not a newcomer: they were entered from an existing list, not on their first visit.
        Assert.Null(member.FirstVisitDate);
    }

    [Fact]
    public async Task New_members_need_consent_and_a_group()
    {
        var group = await app.CreateGroupAsync();
        var client = await app.ClientForAsync(Roles.Admin);

        var noConsent = await client.PostAsJsonAsync("/api/people", NewMember(group.Id, "甲", consent: false), Json, Ct);
        var noGroup = await client.PostAsJsonAsync("/api/people", NewMember(99999, "乙"), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, noConsent.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noGroup.StatusCode);
    }

    [Fact]
    public async Task Edits_are_written_to_the_audit_log()
    {
        var group = await app.CreateGroupAsync();
        var id = await app.AddMemberAsync(group.Id, "舊名字");
        var client = await app.ClientForAsync(Roles.Coordinator);

        (await client.PutAsJsonAsync($"/api/people/{id}", UpdateRequest("新名字"), Json, Ct)).EnsureSuccessStatusCode();

        var log = await app.WithDbAsync(db =>
            db.AuditLogs.SingleAsync(l => l.EntityType == "Person" && l.EntityId == id.ToString() && l.Action == "Update"));
        Assert.Contains("舊名字", log.Changes);
        Assert.Contains("新名字", log.Changes);
        Assert.NotNull(log.UserId);
    }

    [Fact]
    public async Task Grade_is_stored_as_entry_year()
    {
        var group = await app.CreateGroupAsync();
        var id = await app.AddMemberAsync(group.Id, "小明");
        var client = await app.ClientForAsync(Roles.Coordinator);

        (await client.PutAsJsonAsync($"/api/people/{id}", UpdateRequest("小明", grade: 4), Json, Ct)).EnsureSuccessStatusCode();

        var detail = await client.GetFromJsonAsync<PeopleEndpoints.PersonDetail>($"/api/people/{id}", Json, Ct);
        Assert.Equal(4, detail!.Grade);
        var entryYear = await app.WithDbAsync(db => db.People.Where(p => p.Id == id).Select(p => p.SchoolEntryYear).SingleAsync());
        Assert.Equal(SchoolGrade.EntryYear(4, app.Today), entryYear);
    }

    [Fact]
    public async Task Moving_ends_the_old_membership_the_day_before()
    {
        var from = await app.CreateGroupAsync("小班");
        var to = await app.CreateGroupAsync("大班", from.MinistryId);
        var id = await app.AddMemberAsync(from.Id, "升班的孩子");
        var client = await app.ClientForAsync(Roles.Admin);

        var res = await client.PostAsJsonAsync($"/api/people/{id}/move", new { groupId = to.Id, startDate = Sunday }, Json, Ct);

        res.EnsureSuccessStatusCode();
        var memberships = await app.WithDbAsync(db => db.GroupMemberships.Where(m => m.PersonId == id).ToListAsync());
        Assert.Equal(Sunday.AddDays(-1), memberships.Single(m => m.GroupId == from.Id).EndDate);
        Assert.Null(memberships.Single(m => m.GroupId == to.Id).EndDate);
    }
}
