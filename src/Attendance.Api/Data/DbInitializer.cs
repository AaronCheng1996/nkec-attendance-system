using Attendance.Api.Auth;
using Attendance.Api.Domain;
using Attendance.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Attendance.Api.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbInitializer));

        if (config.GetValue("Database:MigrateOnStartup", false))
        {
            logger.LogInformation("Applying database migrations");
            await db.Database.MigrateAsync();
        }

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        var admin = await EnsureFirstAdminAsync(sp.GetRequiredService<UserManager<AppUser>>(), config, logger);

        if (config.GetValue("Seed:DemoData", false) && admin is not null && !await db.Ministries.AnyAsync())
        {
            logger.LogInformation("Seeding demo data");
            db.SuppressAudit = true;
            await DemoData.SeedAsync(db, sp.GetRequiredService<AppClock>().Today, admin.Id);
            db.SuppressAudit = false;
        }
    }

    /// <summary>Creates the first Admin from Seed:AdminUserName / Seed:AdminPassword when there are no users yet.</summary>
    static async Task<AppUser?> EnsureFirstAdminAsync(UserManager<AppUser> users, IConfiguration config, ILogger logger)
    {
        if (await users.Users.AnyAsync())
            return (await users.GetUsersInRoleAsync(Roles.Admin)).FirstOrDefault();

        var userName = config["Seed:AdminUserName"];
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No users exist. Set Seed:AdminUserName and Seed:AdminPassword to create the first admin.");
            return null;
        }

        var admin = new AppUser { UserName = userName, DisplayName = "系統管理員", CreatedAt = DateTimeOffset.UtcNow };
        var result = await users.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Could not create the first admin: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Created first admin account {UserName}", userName);
        return admin;
    }
}
