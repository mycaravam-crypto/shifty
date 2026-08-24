using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Common;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IHttpContextAccessor httpContextAccessor)
    : IdentityDbContext<ApplicationUser>(options)
{
    // readme.md §23/§1: "Änderungen nachvollziehbar speichern" for the entities Staff
    // actually edit through the API. Identity tables (users/roles) aren't included — there's
    // no Benutzer-management endpoint yet (CLAUDE.md), so nothing writes to them via HTTP.
    private static readonly Type[] AuditedTypes =
    [
        typeof(Team), typeof(Employee), typeof(Contract), typeof(ShiftType),
        typeof(Schedule), typeof(ShiftAssignment), typeof(Absence)
    ];

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<ShiftType> ShiftTypes => Set<ShiftType>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<ShiftTypePreference> ShiftTypePreferences => Set<ShiftTypePreference>();
    public DbSet<WeekdayPreference> WeekdayPreferences => Set<WeekdayPreference>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Team>()
            .HasIndex(t => t.Name).IsUnique();

        builder.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.PersonnelNumber).IsUnique();
            e.HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.EligibleShiftTypes).WithMany();
        });

        builder.Entity<Contract>(c =>
        {
            c.HasIndex(x => new { x.EmployeeId, x.ValidFrom }).IsUnique();
            c.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Absence>(a =>
        {
            a.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShiftType>()
            .HasIndex(s => s.Name).IsUnique();

        // issue #55: server-side refresh-token revocation. UserId is a plain string FK to
        // Identity's ApplicationUser (Infrastructure layer) — Domain can't reference it via a
        // navigation property, same reason as AuditLog.UserId above, but the FK constraint
        // itself is still enforced here.
        builder.Entity<RefreshToken>(r =>
        {
            r.HasIndex(x => x.TokenHash).IsUnique();
            r.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShiftTypePreference>(p =>
        {
            p.HasIndex(x => new { x.EmployeeId, x.ShiftTypeId }).IsUnique();
            p.HasOne<Employee>()
                .WithMany(e => e.ShiftTypePreferences)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
            p.HasOne(x => x.ShiftType)
                .WithMany()
                .HasForeignKey(x => x.ShiftTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WeekdayPreference>(p =>
        {
            p.HasIndex(x => new { x.EmployeeId, x.DayOfWeek }).IsUnique();
            p.HasOne<Employee>()
                .WithMany(e => e.WeekdayPreferences)
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShiftAssignment>(a =>
        {
            a.HasOne<Schedule>()
                .WithMany()
                .HasForeignKey(x => x.ScheduleId)
                .OnDelete(DeleteBehavior.Cascade);
            a.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
            a.HasOne<ShiftType>()
                .WithMany()
                .HasForeignKey(x => x.ShiftTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditLogs = BuildAuditLogs();
        if (auditLogs.Count > 0)
            AuditLogs.AddRange(auditLogs);

        return base.SaveChangesAsync(cancellationToken);
    }

    private List<AuditLog> BuildAuditLogs()
    {
        var logs = new List<AuditLog>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;
            if (!AuditedTypes.Contains(entry.Entity.GetType()))
                continue;

            Dictionary<string, object?>? oldValues = null;
            Dictionary<string, object?>? newValues = null;

            switch (entry.State)
            {
                case EntityState.Added:
                    newValues = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                    break;
                case EntityState.Deleted:
                    oldValues = entry.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
                    break;
                case EntityState.Modified:
                    var changed = entry.Properties.Where(p => p.IsModified).ToList();
                    if (changed.Count == 0)
                        continue;
                    oldValues = changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
                    newValues = changed.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                    break;
            }

            var keyValue = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue;

            logs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = CurrentUserId(),
                Action = entry.State switch
                {
                    EntityState.Added => "Create",
                    EntityState.Deleted => "Delete",
                    _ => "Update"
                },
                EntityType = entry.Entity.GetType().Name,
                EntityId = keyValue?.ToString() ?? string.Empty,
                Timestamp = DateTimeOffset.UtcNow,
                OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
                NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues)
            });
        }

        return logs;
    }

    private string CurrentUserId()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return "system";

        // JWT/Staff users carry NameIdentifier (the ApplicationUser.Id, see JwtTokenFactory);
        // API keys only carry Name (see ApiKeyAuthenticationHandler) since they aren't Staff.
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? "system";
    }
}
