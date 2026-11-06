using System.Net;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using Microsoft.EntityFrameworkCore;
using static Attendance.Api.Endpoints.PeopleBatchEndpoints;

namespace Attendance.Api.Tests;

public class BatchTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    Task<List<int>> CurrentGroupsAsync(int personId, DateOnly on) => app.WithDbAsync(db =>
        db.GroupMemberships
            .Where(m => m.PersonId == personId && m.StartDate <= on && (m.EndDate == null || m.EndDate >= on))
            .Select(m => m.GroupId)
            .ToListAsync());

    [Fact]
    public async Task Batch_move_moves_everyone_and_skips_those_already_there()
    {
        var from = await app.CreateGroupAsync("舊班");
        var to = await app.CreateGroupAsync("新班", from.MinistryId);
        var a = await app.AddMemberAsync(from.Id, "甲");
        var b = await app.AddMemberAsync(from.Id, "乙");
        var already = await app.AddMemberAsync(to.Id, "丙");
        var client = await app.ClientForAsync(await app.CreateUserAsync(Roles.Coordinator, ministryIds: [from.MinistryId]));

        var res = await client.PostAsJsonAsync("/api/people/batch/move",
            new BatchMoveRequest([a, b, already], to.Id, Sunday), Json, Ct);

        res.EnsureSuccessStatusCode();
        Assert.Equal(new BatchResult(2, 1), await res.Content.ReadFromJsonAsync<BatchResult>(Json, Ct));
        foreach (var id in new[] { a, b, already }) Assert.Equal([to.Id], await CurrentGroupsAsync(id, Sunday));
        // History is kept: they were still in the old class the week before.
        Assert.Equal([from.Id], await CurrentGroupsAsync(a, Sunday.AddDays(-7)));
    }

    [Fact]
    public async Task Batch_grade_sets_the_grade_of_everyone_selected()
    {
        var group = await app.CreateGroupAsync();
        var a = await app.AddMemberAsync(group.Id, "設年級甲");
        var b = await app.AddMemberAsync(group.Id, "設年級乙");
        var client = await app.ClientForAsync(Roles.Admin);

        var res = await client.PostAsJsonAsync("/api/people/batch/grade", new BatchGradeRequest([a, b], 4), Json, Ct);

        Assert.Equal(new BatchResult(2, 0), await res.Content.ReadFromJsonAsync<BatchResult>(Json, Ct));
        var years = await app.WithDbAsync(db =>
            db.People.Where(p => p.Id == a || p.Id == b).Select(p => p.SchoolEntryYear).ToListAsync());
        Assert.All(years, y => Assert.Equal(4, SchoolGrade.Grade(y, app.Today)));
    }

    Task<int?> GradeAsync(int id) => app.WithDbAsync(async db =>
        SchoolGrade.Grade(await db.People.Where(p => p.Id == id).Select(p => p.SchoolEntryYear).SingleAsync(), app.Today));

    [Fact]
    public async Task Promote_moves_grades_up_and_leaves_groups_alone()
    {
        var group = await app.CreateGroupAsync("升班班");
        var client = await app.ClientForAsync(Roles.Admin);
        var small = await app.AddMemberAsync(group.Id, "小班生");
        var second = await app.AddMemberAsync(group.Id, "小二生");
        var senior = await app.AddMemberAsync(group.Id, "高三生");
        var none = await app.AddMemberAsync(group.Id, "沒年級");
        (await client.PostAsJsonAsync("/api/people/batch/grade", new BatchGradeRequest([small], -2), Json, Ct)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/people/batch/grade", new BatchGradeRequest([second], 2), Json, Ct)).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/people/batch/grade", new BatchGradeRequest([senior], 12), Json, Ct)).EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync("/api/people/batch/promote",
            new PromoteRequest([small, second, senior, none]), Json, Ct);

        // 高三 cannot go higher and 沒年級 has nothing to raise, so both are skipped.
        Assert.Equal(new BatchResult(2, 2), await res.Content.ReadFromJsonAsync<BatchResult>(Json, Ct));
        Assert.Equal((-1, 3, 12, (int?)null), (await GradeAsync(small), await GradeAsync(second), await GradeAsync(senior), await GradeAsync(none)));
        Assert.Equal([group.Id], await CurrentGroupsAsync(small, app.Today));

        // A mistaken promotion can be undone.
        (await client.PostAsJsonAsync("/api/people/batch/promote", new PromoteRequest([small], -1), Json, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(-2, await GradeAsync(small));
    }

    [Fact]
    public async Task Promote_rejects_a_zero_or_huge_step()
    {
        var group = await app.CreateGroupAsync();
        var id = await app.AddMemberAsync(group.Id, "級數");
        var client = await app.ClientForAsync(Roles.Admin);

        var zero = await client.PostAsJsonAsync("/api/people/batch/promote", new PromoteRequest([id], 0), Json, Ct);
        var huge = await client.PostAsJsonAsync("/api/people/batch/promote", new PromoteRequest([id], 10), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
    }

    [Fact]
    public async Task Batch_actions_need_a_selection_and_staff_rights()
    {
        var group = await app.CreateGroupAsync();
        var id = await app.AddMemberAsync(group.Id, "權限");
        var admin = await app.ClientForAsync(Roles.Admin);
        var leader = await app.ClientForAsync(await app.CreateUserAsync(Roles.Leader, [group.Id]));

        var empty = await admin.PostAsJsonAsync("/api/people/batch/move", new BatchMoveRequest([], group.Id, Sunday), Json, Ct);
        var forbidden = await leader.PostAsJsonAsync("/api/people/batch/move", new BatchMoveRequest([id], group.Id, Sunday), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
