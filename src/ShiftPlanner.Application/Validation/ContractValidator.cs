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
        foreach (var group in assignments.GroupBy(a => a.EmployeeId))
        {
            var contract = contracts
                .Where(c => c.EmployeeId == group.Key && c.ValidFrom <= schedule.StartDate
                    && (c.ValidTo is null || c.ValidTo >= schedule.StartDate))
                .MaxBy(c => c.ValidFrom);
            if (contract is null)
                continue;

            var plannedHours = group.Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
            if (plannedHours > contract.WeeklyHours)
            {
                result.Errors.Add(new ValidationIssue(
                    "ContractHoursExceeded",
                    $"{plannedHours}h geplant, Vertrag sieht {contract.WeeklyHours}h vor.",
                    group.Key));
            }
        }
    }
}
