using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Common;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Infrastructure.Persistence;

// AuditLog writes used to be built here via a SaveChangesAsync(CancellationToken) override, but
// that only intercepts that one EF Core overload — SaveChanges()/SaveChanges(bool)/
// SaveChangesAsync(bool, CancellationToken) all bypassed it, silently skipping the audit write.
// That logic (plus the HTTP-claims lookup it needed) now lives in
// AuditSaveChangesInterceptor, registered centrally via AddInterceptors so it fires on every
// SaveChanges path regardless of which overload a caller uses. Keeps this class a plain
// persistence/model-mapping DbContext.
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
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
    public DbSet<StaffingRequirement> StaffingRequirements => Set<StaffingRequirement>();

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

        // issue #69: same weekly-pattern combo shouldn't be configured twice. Postgres treats
        // each NULL TeamId as distinct for uniqueness purposes, so this doesn't stop a
        // global (TeamId null) row and a per-team row for the same ShiftType/DayOfWeek from
        // coexisting — that's intentional, they mean different things.
        builder.Entity<StaffingRequirement>(r =>
        {
            r.HasIndex(x => new { x.TeamId, x.ShiftTypeId, x.DayOfWeek }).IsUnique();
            r.HasOne(x => x.ShiftType)
                .WithMany()
                .HasForeignKey(x => x.ShiftTypeId)
                .OnDelete(DeleteBehavior.Cascade);
            r.HasOne<Team>()
                .WithMany()
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Restrict);
        });

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

            // issue #156: maps onto Postgres's own xmin system column as the concurrency token —
            // xmin already exists on every row, so the migration for this (ShiftAssignmentXmin
            // Concurrency) has its generated AddColumn/DropColumn hand-edited to a no-op; Postgres
            // rejects ALTER TABLE ADD/DROP COLUMN on a reserved system column name outright.
            a.Property<uint>("RowVersion")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });
    }
}
