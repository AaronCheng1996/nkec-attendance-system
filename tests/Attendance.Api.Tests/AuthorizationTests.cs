using System.Net;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Endpoints;

namespace Attendance.Api.Tests;

public class AuthorizationTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/api/people")]
    [InlineData("/api/ministries")]
    [InlineData("/api/reports/summary?from=2026-01-01&to=2026-12-31")]
    [InlineData("/api/users")]
    public async Task Leaders_cannot_use_admin_pages(string url)
    {
        var leader = await app.ClientForAsync(Roles.Leader);

        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(url, Ct)).StatusCode);
    }

    [Fact]
    public async Task Coordinators_manage_people_but_not_accounts()
    {
        var coordinator = await app.ClientForAsync(Roles.Coordinator);

        Assert.Equal(HttpStatusCode.OK, (await coordinator.GetAsync("/api/people", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await coordinator.GetAsync("/api/users", Ct)).StatusCode);
    }

    [Fact]
    public async Task Leaders_only_see_and_mark_their_own_groups()
    {
        var mine = await app.CreateGroupAsync("我的班");
        var other = await app.CreateGroupAsync("別人的班");
        var leader = await app.ClientForAsync(await app.CreateUserAsync(Roles.Leader, groupIds: [mine.Id]));

        var groups = await leader.GetFromJsonAsync<List<GroupEndpoints.GroupDto>>("/api/groups", Json, Ct);

        Assert.Equal([mine.Id], groups!.Select(g => g.Id));
        var res = await leader.GetAsync($"/api/groups/{other.Id}/rollcall?date={Sunday:yyyy-MM-dd}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Coordinators_see_the_groups_of_their_ministries()
    {
        var mine = await app.CreateGroupAsync("我的事工");
        var sameMinistry = await app.CreateGroupAsync("同事工的班", mine.MinistryId);
        await app.CreateGroupAsync("別的事工");
        var coordinator = await app.ClientForAsync(
            await app.CreateUserAsync(Roles.Coordinator, ministryIds: [mine.MinistryId]));

        var groups = await coordinator.GetFromJsonAsync<List<GroupEndpoints.GroupDto>>("/api/groups", Json, Ct);

        Assert.Equal([mine.Id, sameMinistry.Id], groups!.Select(g => g.Id).Order());
    }
}
