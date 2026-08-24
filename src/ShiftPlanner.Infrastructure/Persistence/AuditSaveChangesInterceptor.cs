using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftPlanner.Domain.Common;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Infrastructure.Persistence;

/// <summary>
/// Builds AuditLog rows from the DbContext's change tracker on every save (readme.md §23/§1,
/// "Änderungen nachvollziehbar speichern"). This used to be a <c>SaveChangesAsync</c> override on
/// <see cref="ApplicationDbContext"/> itself, which had two problems: EF Core has four
/// SaveChanges entry points (<c>SaveChanges()</c>, <c>SaveChanges(bool)</c>,
/// <c>SaveChangesAsync(CancellationToken)</c>, <c>SaveChangesAsync(bool, CancellationToken)</c>)
/// and a plain override only intercepts the one overload it declares — any caller reaching a
/// different one (notably the sync <c>SaveChanges()</c>) silently skipped the audit write; and it
/// mixed persistence with audit business logic and raw <see cref="ClaimsPrincipal"/> access in the
/// same class as the entity model. A <see cref="SaveChangesInterceptor"/> fixes both: its
/// <see cref="SavingChanges"/>/<see cref="SavingChangesAsync"/> pair is EF Core's single funnel for
/// all four SaveChanges paths, and it lives outside the DbContext entirely.
/// </summary>
public sealed class AuditSaveChangesInterceptor(IHttpContextAccessor httpContextAccessor) : SaveChangesInterceptor
{
    // readme.md §23/§1: "Änderungen nachvollziehbar speichern" for the entities Staff actually
    // edit through the API. Identity tables (users/roles) aren't included — there's no
    // Benutzer-management endpoint yet (CLAUDE.md), so nothing writes to them via HTTP.
    private static readonly Type[] AuditedTypes =
    [
        typeof(Team), typeof(Employee), typeof(Contract), typeof(ShiftType),
        typeof(Schedule), typeof(ShiftAssignment), typeof(Absence)
    ];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditLogs(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditLogs(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditLogs(DbContext? context)
    {
        if (context is null)
            return;

        var auditLogs = BuildAuditLogs(context.ChangeTracker);
        if (auditLogs.Count > 0)
            context.Set<AuditLog>().AddRange(auditLogs);
    }

    private List<AuditLog> BuildAuditLogs(ChangeTracker changeTracker)
    {
        var logs = new List<AuditLog>();

        foreach (var entry in changeTracker.Entries())
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
