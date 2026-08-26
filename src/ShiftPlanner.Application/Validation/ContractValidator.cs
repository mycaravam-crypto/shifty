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
        //
        // issue #70: ExpectedHours now resolves the applicable contract per day rather than once
        // at the schedule's start date, so a mid-schedule contract change (a month-long Schedule
        // easily outlives one) is reflected correctly instead of the whole span being scaled by
        // whichever contract happened to be active on day 1. The "skip if no contract at all"
        // guard below is still resolved once against the schedule's own span (not per day) —
        // that's just "does validating this employee's hours for this period even make sense",
        // separate from ExpectedHours' own per-day resolution of which contract applies.
        foreach (var group in assignments.GroupBy(a => a.EmployeeId))
        {
            var hasAnyContract = contracts.Any(c => c.EmployeeId == group.Key
                && c.ValidFrom <= schedule.EndDate && (c.ValidTo is null || c.ValidTo >= schedule.StartDate));
            if (!hasAnyContract)
                continue;

            var expectedHours = WorkingTimeCalculator.ExpectedHours(
                contracts, absences ?? [], group.Key, schedule.StartDate, schedule.EndDate);
            var plannedHours = group.Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
            if (plannedHours > expectedHours)
            {
                result.Errors.Add(new ValidationIssue(
                    ValidationIssueCode.ContractHoursExceeded,
                    $"{plannedHours}h geplant, Vertrag sieht {Math.Round(expectedHours, 1)}h für diesen Zeitraum vor.",
                    EmployeeId: group.Key));
            }
        }
    }
}
