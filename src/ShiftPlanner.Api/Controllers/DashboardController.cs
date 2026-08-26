using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Application.Dashboard;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

// issue #29 (sub-issue of #27): one aggregated read model for the operational dashboard.
// Every number is derived from the same validators/calculators every other controller
// already uses (ScheduleValidator, StaffingValidator, WorkingTimeCalculator, WageCalculator,
// the Contract-hours-scaling formula) — this file only adds the aggregation across many
// schedules/employees that no existing endpoint does yet.
//
// issue #95: the actual aggregation (the Build* methods, plus their DTOs) now lives in
// ShiftPlanner.Application.Dashboard.DashboardAggregator — a pure, DB-free class that's unit-
// testable in isolation, same pattern ScheduleValidator/WageCalculator already use. This
// controller's job is now just: resolve the query into a period, load the raw data EF Core
// needs to load, and hand it to DashboardAggregator. No aggregation logic of its own.

[ApiController]
[Route("api")]
[Authorize(Policy = "ApiRead")]
public class DashboardController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Get(DateOnly? from, DateOnly? to, Guid? teamId, Guid? shiftTypeId)
    {
        if (from is not null && to is not null && to < from)
            return BadRequest("'to' must not be before 'from'.");

        var (periodFrom, periodTo) = DashboardAggregator.ResolvePeriod(from, to, DateOnly.FromDateTime(DateTime.UtcNow));
        var periodDays = periodTo.DayNumber - periodFrom.DayNumber + 1;
        var prevTo = periodFrom.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(periodDays - 1));

        var schedules = await db.Schedules
            .AsNoTracking()
            .Where(s => s.StartDate <= periodTo && s.EndDate >= periodFrom)
            .ToListAsync();
        var previousSchedules = await db.Schedules
            .AsNoTracking()
            .Where(s => s.StartDate <= prevTo && s.EndDate >= prevFrom)
            .ToListAsync();
        var allScheduleIds = schedules.Select(s => s.Id).Union(previousSchedules.Select(s => s.Id)).ToList();

        var allAssignments = await db.ShiftAssignments
            .AsNoTracking()
            .Where(a => allScheduleIds.Contains(a.ScheduleId))
            .ToListAsync();
        var assignmentsByScheduleId = allAssignments.GroupBy(a => a.ScheduleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ShiftAssignment>)g.ToList());

        var employees = await db.Employees.AsNoTracking().Include(e => e.EligibleShiftTypes).Include(e => e.Team).ToListAsync();
        var employeesById = employees.ToDictionary(e => e.Id);
        bool MatchesTeam(Guid employeeId) =>
            teamId is null || (employeesById.TryGetValue(employeeId, out var e) && e.TeamId == teamId);

        var employeeIds = allAssignments.Select(a => a.EmployeeId)
            .Union(employees.Where(e => MatchesTeam(e.Id)).Select(e => e.Id))
            .ToList();
        var contracts = await db.Contracts.AsNoTracking().Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync();
        var absences = await db.Absences.AsNoTracking().Where(a => employeeIds.Contains(a.EmployeeId)).ToListAsync();
        var shiftTypes = await db.ShiftTypes.AsNoTracking().ToListAsync();
        var shiftTypesById = shiftTypes.ToDictionary(s => s.Id);

        // issue #56: exact ±6-day historyAssignments lookback, mirroring
        // SchedulesController's /validate endpoint exactly, instead of omitting it — fetched once
        // as a pool spanning every schedule in view, then sliced back to each schedule's own
        // ±6-day window inside DashboardAggregator.BuildPainPoints (matching what /validate would
        // compute per-schedule, without a per-schedule DB round trip).
        //
        // schedules.Min/.Max are pre-computed into plain DateOnly locals before the query: EF
        // Core cannot translate a Min()/Max() call over an already-materialized in-memory list
        // when it appears inline inside a Where() lambda passed to the DbSet — it throws
        // InvalidOperationException at runtime trying to translate it as SQL. Only reachable
        // (and only ever 500'd) whenever at least one schedule overlaps the requested period.
        var historyWindowStart = schedules.Count == 0 ? default : schedules.Min(s => s.StartDate).AddDays(-6);
        var historyWindowEnd = schedules.Count == 0 ? default : schedules.Max(s => s.EndDate).AddDays(6);
        List<ShiftAssignment> historyPool = schedules.Count == 0
            ? []
            : await db.ShiftAssignments
                .AsNoTracking()
                .Where(a => employeeIds.Contains(a.EmployeeId)
                    && a.Date >= historyWindowStart
                    && a.Date <= historyWindowEnd)
                .ToListAsync();

        // Mirrors SchedulesController.GetById: which nationwide-vs-Bundesland holiday set
        // applies depends on each employee's Team, so this resolves a HashSet per distinct
        // Bundesland actually in play (including null = nationwide-only) rather than one
        // shared set — otherwise state-specific holiday surcharges are undercounted here even
        // though the same shift's cost is correct in the Schedule detail view.
        //
        // Bundesland? (null = nationwide-only) cannot be used as a Dictionary key directly:
        // Dictionary<TKey,TValue> throws ArgumentNullException on a null key at runtime even
        // when TKey is a nullable value type. A non-nullable Dictionary<Bundesland, ...> for
        // actual states plus a separate set for the nationwide/null case avoids that.
        var bundeslandByEmployee = employees.ToDictionary(e => e.Id, e => e.Team?.Bundesland);
        var nationwideHolidays = GermanPublicHolidays.InRange(prevFrom, periodTo).Select(h => h.Date).ToHashSet();
        var holidaysByBundesland = new Dictionary<Bundesland, HashSet<DateOnly>>();
        foreach (var land in bundeslandByEmployee.Values.Distinct())
        {
            if (land is { } b)
                holidaysByBundesland[b] = GermanPublicHolidays.InRange(prevFrom, periodTo, b).Select(h => h.Date).ToHashSet();
        }

        bool InScope(ShiftAssignment a) =>
            MatchesTeam(a.EmployeeId) && (shiftTypeId is null || a.ShiftTypeId == shiftTypeId);
        var current = allAssignments.Where(a => a.Date >= periodFrom && a.Date <= periodTo && InScope(a)).ToList();
        var previous = allAssignments.Where(a => a.Date >= prevFrom && a.Date <= prevTo && InScope(a)).ToList();

        var coverage = DashboardAggregator.BuildCoverage(current, shiftTypesById);
        var coveragePercent = DashboardAggregator.CoveragePercent(coverage);

        var painPoints = DashboardAggregator.BuildPainPoints(schedules, assignmentsByScheduleId, historyPool, employeesById, shiftTypes, contracts, absences, teamId);
        var planningStatus = DashboardAggregator.BuildPlanningStatus(schedules, painPoints);

        var costBreakdown = DashboardAggregator.BuildCostBreakdown(current, contracts, bundeslandByEmployee, holidaysByBundesland, nationwideHolidays);
        var costTotal = costBreakdown.Total;
        var previousCostTotal = DashboardAggregator.BuildCostBreakdown(previous, contracts, bundeslandByEmployee, holidaysByBundesland, nationwideHolidays).Total;

        var matchingEmployees = employees.Where(e => MatchesTeam(e.Id)).ToList();
        var employeeUtilization = DashboardAggregator.BuildEmployeeUtilization(matchingEmployees, current, contracts, absences, periodFrom, periodTo);
        var utilization = DashboardAggregator.BuildUtilization(employeeUtilization);
        var overtimeHours = employeeUtilization.Sum(u => u.OvertimeHours);
        var previousOvertimeHours = DashboardAggregator.BuildEmployeeUtilization(matchingEmployees, previous, contracts, absences, prevFrom, prevTo)
            .Sum(u => u.OvertimeHours);

        var kpis = new DashboardKpisDto(
            coveragePercent,
            utilization.UtilizationPercent,
            costTotal, DashboardAggregator.DeltaPercent(costTotal, previousCostTotal),
            planningStatus.CompletionPercent,
            painPoints.Count, painPoints.Count(p => p.Severity == PainSeverity.Error),
            overtimeHours, DashboardAggregator.DeltaPercent(overtimeHours, previousOvertimeHours));

        return Ok(new DashboardDto(periodFrom, periodTo, kpis, coverage, planningStatus, painPoints,
            new CostOverviewDto(costTotal, previousCostTotal, DashboardAggregator.DeltaPercent(costTotal, previousCostTotal), costBreakdown),
            utilization));
    }
}
