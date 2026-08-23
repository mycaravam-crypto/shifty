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

            // issue #17: days the employee is on Absence within this Schedule's span don't
            // count toward the expected hours, so a week of vacation doesn't false-flag as
            // under-planned or (after making up for it elsewhere) over-planned.
            var absenceDays = (absences ?? [])
                .Where(a => a.EmployeeId == group.Key)
                .Sum(a => OverlapDays(a.From, a.To, schedule.StartDate, schedule.EndDate));
            var effectiveDays = Math.Max(0, scheduleDays - absenceDays);

            var expectedHours = contract.WeeklyHours * effectiveDays / 7m;
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

    private static int OverlapDays(DateOnly from, DateOnly to, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var start = from > rangeStart ? from : rangeStart;
        var end = to < rangeEnd ? to : rangeEnd;
        return end >= start ? end.DayNumber - start.DayNumber + 1 : 0;
    }
}
