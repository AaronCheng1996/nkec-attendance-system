using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Attendance.Api.Auth;
using Attendance.Api.Endpoints;

namespace Attendance.Api.Tests;

public class AuthTests(ApiFactory app) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task First_admin_is_seeded_and_can_log_in()
    {
        var client = app.CreateClient();

        var res = await client.PostAsJsonAsync(
            "/api/auth/login", new { userName = ApiFactory.AdminUserName, password = ApiFactory.AdminPassword }, Ct);

        res.EnsureSuccessStatusCode();
        var login = await res.Content.ReadFromJsonAsync<AuthEndpoints.LoginResponse>(Json, Ct);
        Assert.Contains(Roles.Admin, login!.User.Roles);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var me = await client.GetFromJsonAsync<AuthEndpoints.UserInfo>("/api/auth/me", Json, Ct);
        Assert.Equal(ApiFactory.AdminUserName, me!.UserName);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var res = await app.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { userName = ApiFactory.AdminUserName, password = "wrong-password1" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Api_requires_a_token()
    {
        var res = await app.CreateClient().GetAsync("/api/groups", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_is_signed_out_immediately()
    {
        var leader = await app.CreateUserAsync(Roles.Leader);
        var leaderClient = await app.ClientForAsync(leader);
        Assert.Equal(HttpStatusCode.OK, (await leaderClient.GetAsync("/api/groups", Ct)).StatusCode);

        var admin = await app.ClientForAsync(Roles.Admin);
        var res = await admin.PutAsJsonAsync($"/api/users/{leader.Id}",
            new { displayName = "停用的老師", isActive = false, role = Roles.Leader }, Json, Ct);
        res.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await leaderClient.GetAsync("/api/groups", Ct)).StatusCode);
    }

    [Fact]
    public async Task Health_and_version_are_public()
    {
        var client = app.CreateClient();

        Assert.Equal("Healthy", await client.GetStringAsync("/healthz", Ct));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/version", Ct)).StatusCode);
    }
}
