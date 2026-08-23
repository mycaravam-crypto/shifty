using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Domain.Common;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ShiftType> ShiftTypes => Set<ShiftType>();

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
        });

        builder.Entity<Contract>(c =>
        {
            c.HasIndex(x => new { x.EmployeeId, x.ValidFrom }).IsUnique();
            c.HasOne<Employee>()
                .WithMany()
                .HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ShiftType>()
            .HasIndex(s => s.Name).IsUnique();
    }
}
