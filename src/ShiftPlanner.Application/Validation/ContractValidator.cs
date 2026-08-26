using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
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
        IReadOnlyList<Absence>? absences,
        ValidationResult result)
    {
        // Schedules aren't always a week (e.g. a full calendar month) — WorkingTimeCalculator.
        // ExpectedHours scales the contract's weekly limit to the schedule's actual span instead
        // of assuming 7 days, and excludes Absence days (issue #17) so a week of vacation doesn't
        // false-flag as under-planned or (after making up for it elsewhere) over-planned.
        foreach (var group in assignments.GroupBy(a => a.EmployeeId))
        {
            var contract = contracts
                .Where(c => c.EmployeeId == group.Key && c.ValidFrom <= schedule.StartDate
                    && (c.ValidTo is null || c.ValidTo >= schedule.StartDate))
                .MaxBy(c => c.ValidFrom);
            if (contract is null)
                continue;

            var expectedHours = WorkingTimeCalculator.ExpectedHours(
                contract, absences ?? [], group.Key, schedule.StartDate, schedule.EndDate);
            var plannedHours = group.Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
            if (plannedHours > expectedHours)
            {
                result.Errors.Add(new ValidationIssue(
                    "ContractHoursExceeded",
                    $"{plannedHours}h geplant, Vertrag sieht {Math.Round(expectedHours, 1)}h für diesen Zeitraum vor.",
                    EmployeeId: group.Key));
            }
        }
    }
}
