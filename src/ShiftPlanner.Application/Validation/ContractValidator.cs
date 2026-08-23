using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// readme.md §13/§14: planned hours vs. the contract active at the schedule's start — an Error,
// per the readme's own ValidationResult example ("ContractHoursExceeded").
public static class ContractValidator
{
    public static void Validate(
        Schedule schedule,
        IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyList<Contract> contracts,
        ValidationResult result)
    {
        // Schedules aren't always a week (e.g. a full calendar month) — scale the contract's
        // weekly limit to the schedule's actual span instead of assuming 7 days.
        var scheduleDays = schedule.EndDate.DayNumber - schedule.StartDate.DayNumber + 1;
        foreach (var group in assignments.GroupBy(a => a.EmployeeId))
        {
            var contract = contracts
                .Where(c => c.EmployeeId == group.Key && c.ValidFrom <= schedule.StartDate
                    && (c.ValidTo is null || c.ValidTo >= schedule.StartDate))
                .MaxBy(c => c.ValidFrom);
            if (contract is null)
                continue;

            var expectedHours = contract.WeeklyHours * scheduleDays / 7m;
            var plannedHours = group.Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
            if (plannedHours > expectedHours)
            {
                result.Errors.Add(new ValidationIssue(
                    "ContractHoursExceeded",
                    $"{plannedHours}h geplant, Vertrag sieht {Math.Round(expectedHours, 1)}h für diesen Zeitraum vor.",
                    group.Key));
            }
        }
    }
}
