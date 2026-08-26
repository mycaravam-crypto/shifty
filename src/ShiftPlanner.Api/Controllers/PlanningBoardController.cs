using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftPlanner.Application.Planning;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Scheduling;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

// issue #72: one aggregated read model for the Wochenansicht/Dienstplan grid, replacing the
// per-employee contracts/absences/hours-balance N+1 requests ScheduleView.vue previously issued
// on every load (dozens to low-hundreds of requests with more than a handful of employees).
// Everything here is derived from the same Domain/Application helpers SchedulesController/
// DashboardController/EmployeesController already use for the identical calculations
// (WorkingTimeCalculator.ExpectedHours, HoursBalanceCalculator.CumulativeBalance,
// WageCalculator.LaborCost, PlanningBoardAggregator.BuildStats) — nothing is re-derived here,
// this endpoint only adds the per-employee-in-one-request aggregation that didn't exist yet.
public record PlanningBoardEmployeeDto(
    Guid EmployeeId,
    List<ShiftAssignmentDto> Assignments,
    decimal? TargetHours,
    decimal PlannedHours,
    decimal BalanceHours,
    List<AbsenceDto> Absences);

public record PlanningBoardDto(DateOnly From, DateOnly To, List<PlanningBoardEmployeeDto> Employees);

[ApiController]
[Route("api")]
[Authorize(Policy = "ApiRead")]
public class PlanningBoardController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("planning-board")]
    public async Task<ActionResult<PlanningBoardDto>> Get(DateOnly from, DateOnly to, Guid? teamId)
    {
        if (to < from)
            return BadRequest("'to' must not be before 'from'.");

        var employeesQuery = db.Employees.Include(e => e.Team).AsNoTracking();
        if (teamId is { } tid)
            employeesQuery = employeesQuery.Where(e => e.TeamId == tid);
        var employees = await employeesQuery.ToListAsync();
        var employeeIds = employees.Select(e => e.Id).ToList();

        var assignmentsInRange = await db.ShiftAssignments.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId) && a.Date >= from && a.Date <= to)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync();

        var contracts = await db.Contracts.AsNoTracking()
            .Where(c => employeeIds.Contains(c.EmployeeId)).ToListAsync();
        var absences = await db.Absences.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId)).ToListAsync();

        // issue #18's GET /employees/{id}/hours-balance, batched once across every employee in
        // view instead of one request per employee: every Schedule fully elapsed before `from`,
        // plus these employees' assignments inside them.
        var priorSchedules = await db.Schedules.AsNoTracking()
            .Where(s => s.EndDate < from).ToListAsync();
        var priorScheduleIds = priorSchedules.Select(s => s.Id).ToHashSet();
        var priorAssignments = await db.ShiftAssignments.AsNoTracking()
            .Where(a => employeeIds.Contains(a.EmployeeId) && priorScheduleIds.Contains(a.ScheduleId))
            .ToListAsync();

        // issue #57: which nationwide-vs-Bundesland holiday set applies depends on each
        // employee's Team, same pattern SchedulesController.GetById/DashboardController already
        // use for the wage-surcharge holiday lookup — resolves a HashSet per distinct
        // Bundesland actually in play (including null = nationwide-only) rather than one shared
        // set. Bundesland? can't be used as a Dictionary key directly (Dictionary throws on a
        // null key even for a nullable value type), so the nationwide/null case gets its own set.
        var bundeslandByEmployee = employees.ToDictionary(e => e.Id, e => e.Team?.Bundesland);
        var nationwideHolidays = GermanPublicHolidays.InRange(from, to).Select(h => h.Date).ToHashSet();
        var holidaysByBundesland = new Dictionary<Bundesland, HashSet<DateOnly>>();
        foreach (var land in bundeslandByEmployee.Values.Distinct())
        {
            if (land is { } b)
                holidaysByBundesland[b] = GermanPublicHolidays.InRange(from, to, b).Select(h => h.Date).ToHashSet();
        }
        HashSet<DateOnly> HolidaysFor(Bundesland? land) =>
            land is { } b ? holidaysByBundesland[b] : nationwideHolidays;

        var result = new List<PlanningBoardEmployeeDto>();
        foreach (var employee in employees)
        {
            var holidays = HolidaysFor(bundeslandByEmployee[employee.Id]);
            var assignmentDtos = assignmentsInRange.Where(a => a.EmployeeId == employee.Id)
                .Select(a => ToAssignmentDto(a, contracts, holidays))
                .ToList();

            var stats = PlanningBoardAggregator.BuildStats(
                employee.Id, from, to, assignmentsInRange, contracts, absences,
                priorSchedules, priorAssignments);

            var employeeAbsences = absences.Where(a => a.EmployeeId == employee.Id
                && a.From <= to && a.To >= from)
                .Select(a => new AbsenceDto(a.Id, a.EmployeeId, a.From, a.To, a.Type, a.Comment))
                .ToList();

            result.Add(new PlanningBoardEmployeeDto(
                employee.Id, assignmentDtos, stats.TargetHours, stats.PlannedHours, stats.BalanceHours,
                employeeAbsences));
        }

        return Ok(new PlanningBoardDto(from, to, result));
    }

    // issue #14: the contract valid on the assignment's own date, not the period's start — a
    // period can span a month, long enough for a mid-month rate change. Same helper shape as
    // SchedulesController.HourlyRateOn.
    private static decimal? HourlyRateOn(IReadOnlyList<Contract> contracts, Guid employeeId, DateOnly date) =>
        contracts.Where(c => c.EmployeeId == employeeId && c.ValidFrom <= date && (c.ValidTo is null || c.ValidTo >= date))
            .MaxBy(c => c.ValidFrom)?.HourlyRate;

    private static ShiftAssignmentDto ToAssignmentDto(ShiftAssignment a, IReadOnlyList<Contract> contracts, HashSet<DateOnly> holidays)
    {
        var netHours = WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes);
        var hourlyRate = HourlyRateOn(contracts, a.EmployeeId, a.Date);
        var timing = new ShiftTiming(a.StartTime, a.EndTime, a.BreakMinutes, a.BreakStartTime);
        var laborCost = WageCalculator.LaborCostWithSurcharges(timing, a.Date.DayOfWeek, holidays.Contains(a.Date), netHours, hourlyRate);
        return new(a.Id, a.ScheduleId, a.EmployeeId, a.ShiftTypeId, a.Date, a.StartTime, a.EndTime, a.BreakMinutes,
            a.BreakStartTime, netHours, laborCost);
    }
}
