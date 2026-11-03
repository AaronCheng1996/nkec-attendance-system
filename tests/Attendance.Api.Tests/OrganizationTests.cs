using System.Net;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Endpoints;

namespace Attendance.Api.Tests;

public class OrganizationTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    record Created(int Id);

    static object GroupRequest(int ministryId, string name, int? minGrade = null, int? maxGrade = null, bool isActive = true) => new
    {
        ministryId, name, defaultDayOfWeek = "Sunday", defaultStartTime = "09:30:00", sortOrder = 1,
        minGrade, maxGrade, isActive,
    };

    async Task<int> CreateAsync(HttpClient client, string url, object body)
    {
        var res = await client.PostAsJsonAsync(url, body, Json, Ct);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<Created>(Json, Ct))!.Id;
    }

    [Fact]
    public async Task Admin_sets_up_a_ministry_with_a_group_that_shows_up_for_roll_call()
    {
        var admin = await app.ClientForAsync(Roles.Admin);

        var ministryId = await CreateAsync(admin, "/api/ministries", new { name = "兒童主日學", sortOrder = 1 });
        var groupId = await CreateAsync(admin, "/api/groups", GroupRequest(ministryId, "幼兒班", minGrade: -2, maxGrade: 0));

        var org = await admin.GetFromJsonAsync<List<OrganizationEndpoints.MinistryDetail>>("/api/organization", Json, Ct);
        var group = Assert.Single(org!.Single(m => m.Id == ministryId).Groups);
        Assert.Equal(("幼兒班", DayOfWeek.Sunday, new TimeOnly(9, 30), -2, 0),
            (group.Name, group.DefaultDayOfWeek, group.DefaultStartTime, group.MinGrade, group.MaxGrade));

        var groups = await admin.GetFromJsonAsync<List<GroupEndpoints.GroupDto>>("/api/groups", Json, Ct);
        Assert.Contains(groups!, g => g.Id == groupId);
    }

    [Fact]
    public async Task Deactivated_groups_leave_the_roll_call_list()
    {
        var admin = await app.ClientForAsync(Roles.Admin);
        var ministryId = await CreateAsync(admin, "/api/ministries", new { name = "青少年", sortOrder = 2 });
        var groupId = await CreateAsync(admin, "/api/groups", GroupRequest(ministryId, "國中組"));

        var res = await admin.PutAsJsonAsync($"/api/groups/{groupId}", GroupRequest(ministryId, "國中組", isActive: false), Json, Ct);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        var groups = await admin.GetFromJsonAsync<List<GroupEndpoints.GroupDto>>("/api/groups", Json, Ct);
        Assert.DoesNotContain(groups!, g => g.Id == groupId);
    }

    [Fact]
    public async Task Ministry_can_be_renamed()
    {
        var admin = await app.ClientForAsync(Roles.Admin);
        var id = await CreateAsync(admin, "/api/ministries", new { name = "舊名稱", sortOrder = 3 });

        (await admin.PutAsJsonAsync($"/api/ministries/{id}", new { name = "新名稱", sortOrder = 3, isActive = true }, Json, Ct))
            .EnsureSuccessStatusCode();

        var org = await admin.GetFromJsonAsync<List<OrganizationEndpoints.MinistryDetail>>("/api/organization", Json, Ct);
        Assert.Equal("新名稱", org!.Single(m => m.Id == id).Name);
    }

    [Theory]
    [InlineData("", null, null)]
    [InlineData("班級", 3, 1)]
    [InlineData("班級", 0, 20)]
    public async Task Invalid_groups_are_rejected(string name, int? minGrade, int? maxGrade)
    {
        var admin = await app.ClientForAsync(Roles.Admin);
        var ministryId = await CreateAsync(admin, "/api/ministries", new { name = "事工", sortOrder = 9 });

        var res = await admin.PostAsJsonAsync("/api/groups", GroupRequest(ministryId, name, minGrade, maxGrade), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Group_needs_an_existing_ministry()
    {
        var admin = await app.ClientForAsync(Roles.Admin);

        var res = await admin.PostAsJsonAsync("/api/groups", GroupRequest(99999, "孤兒班"), Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Coordinator)]
    [InlineData(Roles.Leader)]
    public async Task Only_admins_manage_ministries_and_groups(string role)
    {
        var client = await app.ClientForAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/organization", Ct)).StatusCode);
        var res = await client.PostAsJsonAsync("/api/ministries", new { name = "不該建立", sortOrder = 1 }, Json, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
