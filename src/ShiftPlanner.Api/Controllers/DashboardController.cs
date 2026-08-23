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

public record CostOverviewDto(decimal CurrentTotal, decimal PreviousTotal, decimal? DeltaPercent);

public record UtilizationDto(decimal ContractCapacityHours, decimal PlannedHours, decimal UtilizationPercent);

[ApiController]
[Route("api")]
[Authorize(Policy = "ApiRead")]
public class DashboardController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardDto>> Get(DateOnly? from, DateOnly? to, Guid? teamId, Guid? shiftTypeId)
    {
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

        var employees = await db.Employees.Include(e => e.EligibleShiftTypes).ToListAsync();
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

        var holidays = GermanPublicHolidays.InRange(prevFrom, periodTo).Select(h => h.Date).ToHashSet();

        bool InScope(ShiftAssignment a) =>
            MatchesTeam(a.EmployeeId) && (shiftTypeId is null || a.ShiftTypeId == shiftTypeId);
        var current = allAssignments.Where(a => a.Date >= periodFrom && a.Date <= periodTo && InScope(a)).ToList();
        var previous = allAssignments.Where(a => a.Date >= prevFrom && a.Date <= prevTo && InScope(a)).ToList();

        var coverage = BuildCoverage(current, shiftTypesById);
        var coveragePercent = coverage.Count == 0 ? 100m : Math.Round(coverage.Average(c => Math.Min(100m, c.CoveragePercent)), 1);

        var painPoints = BuildPainPoints(schedules, assignmentsByScheduleId, employeesById, shiftTypes, contracts, absences, teamId);
        var planningStatus = BuildPlanningStatus(schedules, painPoints);

        var costTotal = BuildCost(current, contracts, holidays);
        var previousCostTotal = BuildCost(previous, contracts, holidays);

        var matchingEmployees = employees.Where(e => MatchesTeam(e.Id)).ToList();
        var utilization = BuildUtilization(matchingEmployees, current, contracts, absences, periodFrom, periodTo);
        var overtimeHours = OvertimeHours(matchingEmployees, current, contracts, absences, periodFrom, periodTo);
        var previousOvertimeHours = OvertimeHours(matchingEmployees, previous, contracts, absences, prevFrom, prevTo);

        var kpis = new DashboardKpisDto(
            coveragePercent,
            utilization.UtilizationPercent,
            costTotal, DeltaPercent(costTotal, previousCostTotal),
            planningStatus.CompletionPercent,
            painPoints.Count, painPoints.Count(p => p.Severity == "Error"),
            overtimeHours, DeltaPercent(overtimeHours, previousOvertimeHours));

        return Ok(new DashboardDto(periodFrom, periodTo, kpis, coverage, planningStatus, painPoints,
            new CostOverviewDto(costTotal, previousCostTotal, DeltaPercent(costTotal, previousCostTotal)),
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
            // issue #29: cross-schedule-boundary lookback (historyAssignments) is intentionally
            // omitted here — this is an overview, exact rest-time/consecutive-day enforcement
            // still lives on the existing GET /schedules/{id}/validate.
            var result = ScheduleValidator.Validate(schedule, assignments, employeesById.Values.ToList(), shiftTypes, contracts, null, absences);

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

    private static decimal BuildCost(
        IReadOnlyList<ShiftAssignment> assignments, IReadOnlyList<Contract> contracts, HashSet<DateOnly> holidays) =>
        assignments.Sum(a =>
        {
            var contract = ActiveContract(contracts, a.EmployeeId, a.Date);
            var netHours = WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes);
            return WageCalculator.LaborCost(a.StartTime, a.EndTime, a.Date.DayOfWeek, holidays.Contains(a.Date), netHours, contract?.HourlyRate) ?? 0m;
        });

    // Contract.WeeklyHours scaled to the period's day-span, minus Absence days overlapping the
    // period — same formula as ContractValidator, just applied to an arbitrary period instead
    // of one Schedule.
    private static decimal ExpectedHours(
        Employee employee, IReadOnlyList<Contract> contracts, IReadOnlyList<Absence> absences, DateOnly from, DateOnly to)
    {
        var contract = ActiveContract(contracts, employee.Id, from);
        if (contract is null)
            return 0m;

        var days = to.DayNumber - from.DayNumber + 1;
        var absenceDays = absences.Where(a => a.EmployeeId == employee.Id)
            .Sum(a => WorkingTimeCalculator.OverlapDays(a.From, a.To, from, to));
        var effectiveDays = Math.Max(0, days - absenceDays);
        return contract.WeeklyHours * effectiveDays / 7m;
    }

    private static UtilizationDto BuildUtilization(
        IReadOnlyList<Employee> employees, IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyList<Contract> contracts, IReadOnlyList<Absence> absences, DateOnly from, DateOnly to)
    {
        var capacity = employees.Sum(e => ExpectedHours(e, contracts, absences, from, to));
        var planned = assignments.Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
        var percent = capacity == 0 ? 0m : Math.Round(planned * 100m / capacity, 1);
        return new UtilizationDto(capacity, planned, percent);
    }

    private static decimal OvertimeHours(
        IReadOnlyList<Employee> employees, IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyList<Contract> contracts, IReadOnlyList<Absence> absences, DateOnly from, DateOnly to) =>
        employees.Sum(e =>
        {
            var expected = ExpectedHours(e, contracts, absences, from, to);
            var actual = assignments.Where(a => a.EmployeeId == e.Id)
                .Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
            return Math.Max(0, actual - expected);
        });
}
