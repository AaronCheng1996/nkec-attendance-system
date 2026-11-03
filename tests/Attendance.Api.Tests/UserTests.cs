using System.Net;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Endpoints;

namespace Attendance.Api.Tests;

public class UserTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_creates_a_leader_for_a_group()
    {
        var group = await app.CreateGroupAsync();
        var admin = await app.ClientForAsync(Roles.Admin);

        var res = await admin.PostAsJsonAsync("/api/users", new
        {
            userName = "teacher-a",
            displayName = "王老師",
            password = "password1",
            role = Roles.Leader,
            groupIds = new[] { group.Id },
        }, Json, Ct);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var users = await admin.GetFromJsonAsync<List<UserEndpoints.UserDto>>("/api/users", Json, Ct);
        var created = users!.Single(u => u.UserName == "teacher-a");
        Assert.Equal(Roles.Leader, created.Role);
        Assert.Equal([group.Id], created.GroupIds);
    }

    [Fact]
    public async Task Weak_passwords_are_rejected()
    {
        var admin = await app.ClientForAsync(Roles.Admin);

        var res = await admin.PostAsJsonAsync("/api/users",
            new { userName = "teacher-b", displayName = "李老師", password = "short", role = Roles.Leader }, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_lock_themselves_out()
    {
        var self = await app.CreateUserAsync(Roles.Admin);
        var client = await app.ClientForAsync(self);

        var res = await client.PutAsJsonAsync($"/api/users/{self.Id}",
            new { displayName = "我", isActive = false, role = Roles.Admin }, Json, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
