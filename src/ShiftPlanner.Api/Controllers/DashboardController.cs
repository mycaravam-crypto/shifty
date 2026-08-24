using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Application.Validation;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

// issue #29 (sub-issue of #27): one aggregated read model for the operational dashboard.
// Every number is derived from the same validators/calculators every other controller
// already uses (ScheduleValidator, StaffingValidator, WorkingTimeCalculator, WageCalculator,
// the Contract-hours-scaling formula) — this file only adds the aggregation across many
// schedules/employees that no existing endpoint does yet.

public record DashboardDto(
    DateOnly From, DateOnly To, DashboardKpisDto Kpis, List<CoverageDayDto> Coverage,
    PlanningStatusDto PlanningStatus, List<PainPointDto> PainPoints,
    CostOverviewDto Cost, UtilizationDto Utilization);

public record DashboardKpisDto(
    decimal StaffingCoveragePercent, decimal WorkforceUtilizationPercent,
    decimal LaborCost, decimal? LaborCostDeltaPercent,
    decimal PlanningCompletionPercent, int OpenIssuesCount, int CriticalIssuesCount,
    decimal OvertimeHours, decimal? OvertimeHoursDeltaPercent);

// Green/Yellow/Red thresholds (95/85) computed here, not client-side, so there's one source
// of truth for what counts as "sufficiently staffed".
public record CoverageDayDto(
    DateOnly Date, Guid ShiftTypeId, string ShiftTypeName,
    int Scheduled, int MinStaffing, decimal CoveragePercent, string Status);

public record PlanningStatusDto(
    int DraftCount, int PublishedCount, int ConflictCount, decimal CompletionPercent,
    List<ScheduleRefDto> AffectedSchedules);

public record ScheduleRefDto(Guid Id, string Name, DateOnly StartDate, ScheduleStatus Status);

public record PainPointDto(
    string Type, string Severity, string Message, Guid ScheduleId, string ScheduleName,
    Guid? EmployeeId, string? EmployeeName);

// issue #56: regular/overtime/premium/weekend cost breakdown restored from the parent issue's
// (#27/#29) trimmed mockup scope. Mapped onto what WageCalculator actually tracks internally
// (base rate + night/Sunday/holiday surcharges) rather than a literal "overtime" cost line —
// see CLAUDE.md for why: there's no separate overtime pay rate anywhere in this codebase, so an
// "overtime cost" bucket would just be a subset of Regular charged at the same rate, redundant
// with the existing OvertimeHours KPI (an hours figure, not a cost one).
public record CostOverviewDto(decimal CurrentTotal, decimal PreviousTotal, decimal? DeltaPercent, CostBreakdownDto Breakdown);

public record CostBreakdownDto(decimal Regular, decimal Night, decimal Sunday, decimal Holiday)
{
    public decimal Total => Regular + Night + Sunday + Holiday;
}

// issue #56: per-employee utilization table restored from the trimmed mockup scope — same
// WorkingTimeCalculator.ExpectedHours formula ContractValidator/HoursBalanceCalculator already
// use, just grouped by employee instead of summed schedule-wide.
public record EmployeeUtilizationDto(
    Guid EmployeeId, string EmployeeName, decimal ContractCapacityHours, decimal PlannedHours,
    decimal UtilizationPercent, decimal OvertimeHours);

public record UtilizationDto(
    decimal ContractCapacityHours, decimal PlannedHours, decimal UtilizationPercent,
    List<EmployeeUtilizationDto> ByEmployee);

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

        var (periodFrom, periodTo) = ResolvePeriod(from, to);
        var periodDays = periodTo.DayNumber - periodFrom.DayNumber + 1;
        var prevTo = periodFrom.AddDays(-1);
        var prevFrom = prevTo.AddDays(-(periodDays - 1));

        var schedules = await db.Schedules
            .Where(s => s.StartDate <= periodTo && s.EndDate >= periodFrom)
            .ToListAsync();
        var previousSchedules = await db.Schedules
            .Where(s => s.StartDate <= prevTo && s.EndDate >= prevFrom)
            .ToListAsync();
        var allScheduleIds = schedules.Select(s => s.Id).Union(previousSchedules.Select(s => s.Id)).ToList();

        var allAssignments = await db.ShiftAssignments
            .Where(a => allScheduleIds.Contains(a.ScheduleId))
            .ToListAsync();
        var assignmentsByScheduleId = allAssignments.GroupBy(a => a.ScheduleId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ShiftAssignment>)g.ToList());

        var employees = await db.Employees.Include(e => e.EligibleShiftTypes).Include(e => e.Team).ToListAsync();
        var employeesById = employees.ToDictionary(e => e.Id);
        bool MatchesTeam(Guid employeeId) =>
            teamId is null || (employeesById.TryGetValue(employeeId, out var e) && e.TeamId == teamId);

        var employeeIds = allAssignments.Select(a => a.EmployeeId)
            .Union(employees.Where(e => MatchesTeam(e.Id)).Select(e => e.Id))
            .ToList();
        var contracts = await db.Contracts.Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync();
        var absences = await db.Absences.Where(a => employeeIds.Contains(a.EmployeeId)).ToListAsync();
        var shiftTypes = await db.ShiftTypes.ToListAsync();
        var shiftTypesById = shiftTypes.ToDictionary(s => s.Id);

        // issue #56: exact ±6-day historyAssignments lookback, mirroring
        // SchedulesController's /validate endpoint exactly, instead of omitting it — fetched once
        // as a pool spanning every schedule in view, then sliced back to each schedule's own
        // ±6-day window inside BuildPainPoints (matching what /validate would compute per-schedule,
        // without a per-schedule DB round trip).
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

        var coverage = BuildCoverage(current, shiftTypesById);
        var coveragePercent = coverage.Count == 0 ? 100m : Math.Round(coverage.Average(c => Math.Min(100m, c.CoveragePercent)), 1);

        var painPoints = BuildPainPoints(schedules, assignmentsByScheduleId, historyPool, employeesById, shiftTypes, contracts, absences, teamId);
        var planningStatus = BuildPlanningStatus(schedules, painPoints);

        var costBreakdown = BuildCostBreakdown(current, contracts, bundeslandByEmployee, holidaysByBundesland, nationwideHolidays);
        var costTotal = costBreakdown.Total;
        var previousCostTotal = BuildCostBreakdown(previous, contracts, bundeslandByEmployee, holidaysByBundesland, nationwideHolidays).Total;

        var matchingEmployees = employees.Where(e => MatchesTeam(e.Id)).ToList();
        var employeeUtilization = BuildEmployeeUtilization(matchingEmployees, current, contracts, absences, periodFrom, periodTo);
        var utilization = new UtilizationDto(
            employeeUtilization.Sum(u => u.ContractCapacityHours),
            employeeUtilization.Sum(u => u.PlannedHours),
            UtilizationPercent(employeeUtilization),
            employeeUtilization);
        var overtimeHours = employeeUtilization.Sum(u => u.OvertimeHours);
        var previousOvertimeHours = BuildEmployeeUtilization(matchingEmployees, previous, contracts, absences, prevFrom, prevTo)
            .Sum(u => u.OvertimeHours);

        var kpis = new DashboardKpisDto(
            coveragePercent,
            utilization.UtilizationPercent,
            costTotal, DeltaPercent(costTotal, previousCostTotal),
            planningStatus.CompletionPercent,
            painPoints.Count, painPoints.Count(p => p.Severity == "Error"),
            overtimeHours, DeltaPercent(overtimeHours, previousOvertimeHours));

        return Ok(new DashboardDto(periodFrom, periodTo, kpis, coverage, planningStatus, painPoints,
            new CostOverviewDto(costTotal, previousCostTotal, DeltaPercent(costTotal, previousCostTotal), costBreakdown),
            utilization));
    }

    private static (DateOnly From, DateOnly To) ResolvePeriod(DateOnly? from, DateOnly? to)
    {
        if (from is { } f && to is { } t)
            return (f, t);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return (monday, monday.AddDays(6));
    }

    private static decimal? DeltaPercent(decimal current, decimal previous) =>
        previous == 0 ? null : Math.Round((current - previous) / previous * 100m, 1);

    private static List<CoverageDayDto> BuildCoverage(
        IReadOnlyList<ShiftAssignment> assignments, IReadOnlyDictionary<Guid, ShiftType> shiftTypesById)
    {
        var result = new List<CoverageDayDto>();
        foreach (var group in assignments.GroupBy(a => (a.ShiftTypeId, a.Date)))
        {
            if (!shiftTypesById.TryGetValue(group.Key.ShiftTypeId, out var shiftType) || shiftType.MinStaffing is not { } min || min == 0)
                continue;

            var scheduled = group.Select(a => a.EmployeeId).Distinct().Count();
            var percent = Math.Round(scheduled * 100m / min, 1);
            var status = percent >= 95 ? "Green" : percent >= 85 ? "Yellow" : "Red";
            result.Add(new CoverageDayDto(group.Key.Date, shiftType.Id, shiftType.Name, scheduled, min, percent, status));
        }
        return result.OrderBy(c => c.Date).ThenBy(c => c.ShiftTypeName).ToList();
    }

    private static List<PainPointDto> BuildPainPoints(
        IReadOnlyList<Schedule> schedules,
        IReadOnlyDictionary<Guid, IReadOnlyList<ShiftAssignment>> assignmentsByScheduleId,
        IReadOnlyList<ShiftAssignment> historyPool,
        IReadOnlyDictionary<Guid, Employee> employeesById,
        IReadOnlyList<ShiftType> shiftTypes,
        IReadOnlyList<Contract> contracts,
        IReadOnlyList<Absence> absences,
        Guid? teamId)
    {
        var points = new List<PainPointDto>();
        foreach (var schedule in schedules)
        {
            var assignments = assignmentsByScheduleId.GetValueOrDefault(schedule.Id, []);
            // issue #56: sliced back down to this schedule's own ±6-day window (same window
            // SchedulesController's /validate endpoint uses) rather than the omitted-entirely
            // lookback this endpoint shipped with originally — RestTimeValidator/
            // ConsecutiveDaysValidator now see the same cross-schedule-boundary history /validate
            // would, instead of only this schedule's own assignments.
            var historyStart = schedule.StartDate.AddDays(-6);
            var historyEnd = schedule.EndDate.AddDays(6);
            var historyAssignments = historyPool.Where(a => a.Date >= historyStart && a.Date <= historyEnd).ToList();
            var result = ScheduleValidator.Validate(schedule, assignments, employeesById.Values.ToList(), shiftTypes, contracts, historyAssignments, absences);

            foreach (var (issue, severity) in result.Errors.Select(e => (e, "Error")).Concat(result.Warnings.Select(w => (w, "Warning"))))
            {
                if (teamId is not null && issue.EmployeeId is { } employeeId
                    && employeesById.TryGetValue(employeeId, out var emp) && emp.TeamId != teamId)
                    continue;

                var employeeName = issue.EmployeeId is { } id && employeesById.TryGetValue(id, out var e)
                    ? $"{e.FirstName} {e.LastName}" : null;
                points.Add(new PainPointDto(issue.Type, severity, issue.Message, schedule.Id, schedule.Name, issue.EmployeeId, employeeName));
            }
        }
        return points.OrderByDescending(p => p.Severity == "Error").ToList();
    }

    private static PlanningStatusDto BuildPlanningStatus(IReadOnlyList<Schedule> schedules, IReadOnlyList<PainPointDto> painPoints)
    {
        var conflictedScheduleIds = painPoints.Where(p => p.Severity == "Error").Select(p => p.ScheduleId).ToHashSet();
        var affected = schedules.Where(s => conflictedScheduleIds.Contains(s.Id))
            .Select(s => new ScheduleRefDto(s.Id, s.Name, s.StartDate, s.Status)).ToList();
        var published = schedules.Count(s => s.Status == ScheduleStatus.Published);
        var completion = schedules.Count == 0 ? 0m : Math.Round(published * 100m / schedules.Count, 1);

        return new PlanningStatusDto(
            schedules.Count(s => s.Status == ScheduleStatus.Draft), published, affected.Count, completion, affected);
    }

    private static Contract? ActiveContract(IReadOnlyList<Contract> contracts, Guid employeeId, DateOnly date) =>
        contracts.Where(c => c.EmployeeId == employeeId && c.ValidFrom <= date && (c.ValidTo is null || c.ValidTo >= date))
            .MaxBy(c => c.ValidFrom);

    // issue #56: aggregated per-surcharge-type so the dashboard can show a cost breakdown, not
    // just a total — WageCalculator.Breakdown (not the plain LaborCost total) is the single
    // source of truth for the split, same as BuildCost always was for the total alone.
    private static CostBreakdownDto BuildCostBreakdown(
        IReadOnlyList<ShiftAssignment> assignments, IReadOnlyList<Contract> contracts,
        IReadOnlyDictionary<Guid, Bundesland?> bundeslandByEmployee,
        IReadOnlyDictionary<Bundesland, HashSet<DateOnly>> holidaysByBundesland,
        HashSet<DateOnly> nationwideHolidays)
    {
        decimal regular = 0, night = 0, sunday = 0, holiday = 0;
        foreach (var a in assignments)
        {
            var contract = ActiveContract(contracts, a.EmployeeId, a.Date);
            var netHours = WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes);
            var land = bundeslandByEmployee.GetValueOrDefault(a.EmployeeId);
            var isHoliday = (land is { } bl ? holidaysByBundesland[bl] : nationwideHolidays).Contains(a.Date);
            // issue #58: BreakMinutes/BreakStartTime threaded through so the night-surcharge
            // portion of the breakdown gets the same break-adjusted precision LaborCost's other
            // call sites already have.
            var breakdown = WageCalculator.Breakdown(a.StartTime, a.EndTime, a.Date.DayOfWeek, isHoliday, netHours, contract?.HourlyRate,
                a.BreakMinutes, a.BreakStartTime);
            if (breakdown is not { } b)
                continue;
            regular += b.Regular;
            night += b.Night;
            sunday += b.Sunday;
            holiday += b.Holiday;
        }
        return new CostBreakdownDto(regular, night, sunday, holiday);
    }

    // issue #56: per-employee utilization restored from the trimmed mockup scope — same
    // WorkingTimeCalculator.ExpectedHours formula ContractValidator/HoursBalanceCalculator use,
    // grouped by employee instead of summed. The schedule-wide UtilizationDto/OvertimeHours KPI
    // are now just aggregates over this list rather than a separate pass over the same data.
    private static List<EmployeeUtilizationDto> BuildEmployeeUtilization(
        IReadOnlyList<Employee> employees, IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyList<Contract> contracts, IReadOnlyList<Absence> absences, DateOnly from, DateOnly to)
    {
        var result = new List<EmployeeUtilizationDto>();
        foreach (var employee in employees)
        {
            var contract = ActiveContract(contracts, employee.Id, from);
            var expected = WorkingTimeCalculator.ExpectedHours(contract, absences, employee.Id, from, to);
            var actual = assignments.Where(a => a.EmployeeId == employee.Id)
                .Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
            var percent = expected == 0 ? 0m : Math.Round(actual * 100m / expected, 1);
            result.Add(new EmployeeUtilizationDto(
                employee.Id, $"{employee.FirstName} {employee.LastName}", expected, actual, percent, Math.Max(0, actual - expected)));
        }
        return result.OrderByDescending(u => u.PlannedHours).ToList();
    }

    private static decimal UtilizationPercent(IReadOnlyList<EmployeeUtilizationDto> byEmployee)
    {
        var capacity = byEmployee.Sum(u => u.ContractCapacityHours);
        var planned = byEmployee.Sum(u => u.PlannedHours);
        return capacity == 0 ? 0m : Math.Round(planned * 100m / capacity, 1);
    }
}
