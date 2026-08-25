using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Planning;

// issue #72: the per-employee "target / planned / carried-over" math the Wochenansicht grid
// needs — extracted out of PlanningBoardController so it's unit-testable without a live
// ApplicationDbContext (the controller only does the DB fetching; this is the actual
// aggregation). Reuses the exact same Domain calculators every other read model in this
// codebase already relies on (WorkingTimeCalculator.ExpectedHours/NetHours,
// HoursBalanceCalculator.CumulativeBalance), so the numbers match ContractValidator/
// DashboardController/the old GET /employees/{id}/hours-balance endpoint exactly — this
// endpoint is a read-model consolidation, not a new calculation.
public record PlanningBoardEmployeeStats(decimal? TargetHours, decimal PlannedHours, decimal BalanceHours);

public static class PlanningBoardAggregator
{
    // assignmentsInRange: every ShiftAssignment for ANY employee within [from, to] (the
    // controller filters to just this employeeId here) — same shape SchedulesController's own
    // schedule-detail endpoint already sends. priorSchedules/priorAssignments: every Schedule
    // fully elapsed before `from` and the relevant assignments inside them, i.e. exactly what
    // GET /employees/{id}/hours-balance?before={from} already fetches per employee, batched
    // once across every employee in view instead of one request per employee.
    public static PlanningBoardEmployeeStats BuildStats(
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<ShiftAssignment> assignmentsInRange,
        IReadOnlyList<Contract> contracts,
        IReadOnlyList<Absence> absences,
        IReadOnlyList<Schedule> priorSchedules,
        IReadOnlyList<ShiftAssignment> priorAssignments)
    {
        var employeeContracts = contracts.Where(c => c.EmployeeId == employeeId).ToList();
        var employeeAbsences = absences.Where(a => a.EmployeeId == employeeId).ToList();

        var targetContract = ResolveTargetContract(employeeContracts, from);
        var targetHours = targetContract is null
            ? (decimal?)null
            : WorkingTimeCalculator.ExpectedHours(targetContract, employeeAbsences, employeeId, from, to);

        var plannedHours = assignmentsInRange.Where(a => a.EmployeeId == employeeId)
            .Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));

        var balanceHours = HoursBalanceCalculator.CumulativeBalance(
            employeeId, from, priorSchedules,
            priorAssignments.Where(a => a.EmployeeId == employeeId).ToList(),
            employeeContracts, employeeAbsences);

        return new PlanningBoardEmployeeStats(targetHours, plannedHours, balanceHours);
    }

    // Mirrors the exact selection ScheduleView.vue's own client-side targetHoursFor used to make
    // (the per-employee-N+1 behavior this endpoint replaces): prefer the contract active on
    // `from`; if none is active there, fall back to the most-recently-started contract instead
    // of showing no target at all — only "no contracts exist for this employee" returns null.
    // Deliberately not the plainer "no fallback" ActiveContract helper ContractValidator/
    // DashboardController use for their own Schedule-vs-active-contract checks (no target is the
    // right answer there); this one has to reproduce the grid's previous figure exactly.
    public static Contract? ResolveTargetContract(IReadOnlyList<Contract> contracts, DateOnly from)
    {
        if (contracts.Count == 0)
            return null;
        var active = contracts.FirstOrDefault(c => c.ValidFrom <= from && (c.ValidTo is null || c.ValidTo >= from));
        return active ?? contracts.MaxBy(c => c.ValidFrom);
    }
}
