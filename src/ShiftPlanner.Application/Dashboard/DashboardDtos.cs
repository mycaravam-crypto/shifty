using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Dashboard;

// issue #95: DTOs moved out of Api/Controllers/DashboardController.cs alongside the
// aggregation logic that builds them (DashboardAggregator, same folder) — they're plain data,
// no ASP.NET Core dependency, so there's no Api->Application->Api cycle in having them live
// here instead. Shapes/names are unchanged from before the move, so the JSON contract the
// frontend consumes is identical.

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
    int Scheduled, int MinStaffing, decimal CoveragePercent, CoverageStatus Status);

public record PlanningStatusDto(
    int DraftCount, int PublishedCount, int ConflictCount, decimal CompletionPercent,
    List<ScheduleRefDto> AffectedSchedules);

public record ScheduleRefDto(Guid Id, string Name, DateOnly StartDate, ScheduleStatus Status);

public record PainPointDto(
    string Type, PainSeverity Severity, string Message, Guid ScheduleId, string ScheduleName,
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
