using System.Text.Encodings.Web;
using System.Text.Json;
using Attendance.Api.Auth;
using Attendance.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Attendance.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor http)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Ministry> Ministries => Set<Ministry>();
    public DbSet<MinistryCoordinator> MinistryCoordinators => Set<MinistryCoordinator>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupLeader> GroupLeaders => Set<GroupLeader>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<PersonRelationship> PersonRelationships => Set<PersonRelationship>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>Set during bulk seeding so demo data does not flood the audit log.</summary>
    public bool SuppressAudit { get; set; }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e => e.Property(x => x.DisplayName).HasMaxLength(50));

        b.Entity<Ministry>(e => e.Property(x => x.Name).HasMaxLength(50));

        b.Entity<MinistryCoordinator>(e =>
        {
            e.HasKey(x => new { x.UserId, x.MinistryId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<Ministry>().WithMany().HasForeignKey(x => x.MinistryId);
        });

        b.Entity<Group>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.Description).HasMaxLength(200);
            e.HasOne(x => x.Ministry).WithMany(x => x.Groups).HasForeignKey(x => x.MinistryId);
        });

        b.Entity<GroupLeader>(e =>
        {
            e.HasKey(x => new { x.UserId, x.GroupId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId);
        });

        b.Entity<Person>(e =>
        {
            e.ToTable("People", t => t.HasCheckConstraint(
                "CK_People_CustomFields_IsJson", "[CustomFields] IS NULL OR ISJSON([CustomFields]) = 1"));
            e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.Nickname).HasMaxLength(50);
            e.Property(x => x.Phone).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.Email).HasMaxLength(100);
            e.Property(x => x.ConsentMethod).HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(450);
            e.Property(x => x.UpdatedBy).HasMaxLength(450);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.FirstVisitDate);
        });

        b.Entity<PersonRelationship>(e =>
        {
            e.HasKey(x => new { x.PersonId, x.RelatedPersonId });
            e.HasOne(x => x.Person).WithMany(x => x.Guardians).HasForeignKey(x => x.PersonId);
            e.HasOne(x => x.RelatedPerson).WithMany().HasForeignKey(x => x.RelatedPersonId);
        });

        b.Entity<GroupMembership>(e =>
        {
            e.HasOne(x => x.Group).WithMany(x => x.Memberships).HasForeignKey(x => x.GroupId);
            e.HasOne(x => x.Person).WithMany(x => x.Memberships).HasForeignKey(x => x.PersonId);
            e.HasIndex(x => new { x.GroupId, x.EndDate });
        });

        b.Entity<Session>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(50);
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId);
            // One session per group/date/time. HasFilter(null) makes a NULL StartTime count as a value too.
            e.HasIndex(x => new { x.GroupId, x.SessionDate, x.StartTime }).IsUnique().HasFilter(null);
        });

        b.Entity<AttendanceRecord>(e =>
        {
            e.HasOne(x => x.Session).WithMany(x => x.Records).HasForeignKey(x => x.SessionId);
            e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId);
            e.HasOne(x => x.MarkedByUser).WithMany().HasForeignKey(x => x.MarkedBy);
            e.HasIndex(x => new { x.SessionId, x.PersonId }).IsUnique();
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.Action).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.EntityType).HasMaxLength(50).IsUnicode(false);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        // Records are deactivated rather than deleted, so never cascade deletes across domain tables.
        foreach (var fk in b.Model.GetEntityTypes()
                     .Where(t => t.ClrType.Namespace == typeof(Person).Namespace)
                     .SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    /// <summary>Records an event that is not an entity change, such as a login or password reset.</summary>
    public void LogEvent(string action, string entityType, string entityId, string? userId = null)
    {
        var ctx = http.HttpContext;
        AuditLogs.Add(new AuditLog
        {
            UserId = userId ?? ctx?.User.GetUserId(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId.Length > 100 ? entityId[..100] : entityId,
            IpAddress = ctx?.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var ctx = http.HttpContext;
        var userId = ctx?.User.GetUserId();
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Person>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = userId;
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = userId;
            }
        }

        List<(EntityEntry Entry, string Action, string? Changes)> pending = SuppressAudit
            ? []
            : ChangeTracker.Entries()
                .Where(e => (e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) && IsAudited(e))
                .Select(e => (e, ActionOf(e.State), DescribeChanges(e)))
                .Where(p => p.Item3 is not null)
                .ToList();

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        // Written after the main save so generated keys of new rows are known.
        if (pending.Count > 0)
        {
            var ip = ctx?.Connection.RemoteIpAddress?.ToString();
            AuditLogs.AddRange(pending.Select(p => new AuditLog
            {
                UserId = userId,
                Action = p.Action,
                EntityType = EntityTypeName(p.Entry),
                EntityId = KeyOf(p.Entry),
                Changes = p.Changes,
                IpAddress = ip,
                CreatedAt = now,
            }));
            await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        return result;
    }

    static readonly HashSet<string> NotAudited =
    [
        nameof(AppUser.PasswordHash), nameof(AppUser.SecurityStamp), nameof(AppUser.ConcurrencyStamp),
        nameof(AppUser.NormalizedUserName), nameof(AppUser.NormalizedEmail),
        nameof(AppUser.AccessFailedCount), nameof(AppUser.LockoutEnd),
        nameof(Person.RowVersion), nameof(Person.CreatedAt), nameof(Person.CreatedBy),
        nameof(Person.UpdatedAt), nameof(Person.UpdatedBy),
    ];

    static readonly JsonSerializerOptions AuditJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static bool IsAudited(EntityEntry e) =>
        e.Entity is not AuditLog
        && (e.Entity is IdentityUserRole<string> || e.Metadata.ClrType.Namespace == typeof(Person).Namespace);

    static string ActionOf(EntityState state) => state switch
    {
        EntityState.Added => "Create",
        EntityState.Deleted => "Delete",
        _ => "Update",
    };

    static string EntityTypeName(EntityEntry e) =>
        e.Entity is IdentityUserRole<string> ? "UserRole" : e.Metadata.ClrType.Name;

    static string KeyOf(EntityEntry e) =>
        string.Join(",", e.Metadata.FindPrimaryKey()!.Properties.Select(k => e.Property(k.Name).CurrentValue));

    static string? DescribeChanges(EntityEntry e)
    {
        var changes = new Dictionary<string, object?>();
        foreach (var p in e.Properties)
        {
            var name = p.Metadata.Name;
            if (NotAudited.Contains(name)) continue;

            switch (e.State)
            {
                case EntityState.Added when !p.IsTemporary:
                    changes[name] = p.CurrentValue;
                    break;
                case EntityState.Deleted:
                    changes[name] = p.OriginalValue;
                    break;
                case EntityState.Modified when p.IsModified && !Equals(p.OriginalValue, p.CurrentValue):
                    changes[name] = new { from = p.OriginalValue, to = p.CurrentValue };
                    break;
            }
        }
        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes, AuditJson);
    }
}
