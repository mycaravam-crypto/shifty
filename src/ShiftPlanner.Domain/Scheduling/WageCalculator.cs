namespace ShiftPlanner.Domain.Scheduling;

// Single source of truth for labor cost — mirrors WorkingTimeCalculator (readme.md §14).
// HourlyRate is optional (Contract.HourlyRate, issue #14), so cost is null when unset.
public static class WageCalculator
{
    public static decimal? LaborCost(decimal netHours, decimal? hourlyRate) =>
        hourlyRate is null ? null : netHours * hourlyRate.Value;
}
