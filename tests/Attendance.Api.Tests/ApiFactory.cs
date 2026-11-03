using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Attendance.Api.Auth;
using Attendance.Api.Data;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Attendance.Api.Tests;

public static class TestHelpers
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Same JSON shape as the API: camelCase, enums as strings.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>A Sunday well inside one school year (2026 學年度).</summary>
    public static readonly DateOnly Sunday = new(2026, 10, 4);
}

/// <summary>
/// Runs the real API in memory on an SQLite in-memory database, so tests need neither SQL Server nor Docker.
/// Each test class gets its own app and database through <c>IClassFixture&lt;ApiFactory&gt;</c>.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminUserName = "admin";
    public const string AdminPassword = "admin1234";

    readonly SqliteConnection connection = new("DataSource=:memory:");
    int userCount;

    public ApiFactory()
    {
        // The in-memory database lives as long as this connection stays open.
        connection.Open();
        // Stands in for the SQL Server collation the people list sorts by.
        connection.CreateCollation("Chinese_Taiwan_Stroke_CI_AS", (a, b) => NameOrder.Comparer.Compare(a, b));

        // The schema must exist before the app starts, because startup seeds roles and the first admin.
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, new HttpContextAccessor());
        db.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "unused");
        builder.UseSetting("Jwt:Key", "test-only-signing-key-0123456789-0123456789");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Seed:AdminUserName", AdminUserName);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Logging:LogLevel:Default", "Warning");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public DateOnly Today => Services.GetRequiredService<AppClock>().Today;

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task WithDbAsync(Func<AppDbContext, Task> action) => WithDbAsync(async db =>
    {
        await action(db);
        return true;
    });

    public async Task<AppUser> CreateUserAsync(string role, int[]? groupIds = null, int[]? ministryIds = null)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new AppUser
        {
            UserName = $"{role.ToLowerInvariant()}{Interlocked.Increment(ref userCount)}",
            DisplayName = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Succeeded(await users.CreateAsync(user, "password1"));
        Succeeded(await users.AddToRoleAsync(user, role));

        db.GroupLeaders.AddRange((groupIds ?? []).Select(id => new GroupLeader { UserId = user.Id, GroupId = id }));
        db.MinistryCoordinators.AddRange((ministryIds ?? []).Select(id => new MinistryCoordinator { UserId = user.Id, MinistryId = id }));
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>A client signed in as <paramref name="user"/>. Skips the login endpoint, which is rate limited.</summary>
    public async Task<HttpClient> ClientForAsync(AppUser user)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var current = await users.FindByIdAsync(user.Id) ?? throw new InvalidOperationException("User not found.");
        var token = Services.GetRequiredService<TokenService>().CreateToken(current, await users.GetRolesAsync(current));

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<HttpClient> ClientForAsync(string role) => await ClientForAsync(await CreateUserAsync(role));

    public Task<Group> CreateGroupAsync(string name = "兒童班", int? ministryId = null) => WithDbAsync(async db =>
    {
        var group = new Group { Name = name, DefaultStartTime = new TimeOnly(9, 0) };
        if (ministryId is { } id) group.MinistryId = id;
        else group.Ministry = new Ministry { Name = "兒童主日學" };
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    });

    public Task<int> AddMemberAsync(int groupId, string name, DateOnly? start = null, DateOnly? end = null) =>
        WithDbAsync(async db =>
        {
            var person = new Person { Name = name };
            db.GroupMemberships.Add(new GroupMembership
            {
                GroupId = groupId,
                Person = person,
                StartDate = start ?? new DateOnly(2020, 1, 1),
                EndDate = end,
            });
            await db.SaveChangesAsync();
            return person.Id;
        });

    static void Succeeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
