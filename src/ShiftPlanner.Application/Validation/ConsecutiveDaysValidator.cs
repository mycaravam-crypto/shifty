using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// issue #9: ArbZG requires a rest day after 6 consecutive workdays. Needs cross-schedule
// history, so `assignments` here is deliberately wider than the Schedule being validated
// (see caller).
public static class ConsecutiveDaysValidator
{
    private const int MaxConsecutiveDays = 6;

    public static void Validate(IReadOnlyList<ShiftAssignment> assignments, ValidationResult result)
    {
        foreach (var group in assignments.GroupBy(a => a.EmployeeId))
        {
            var days = group.OrderBy(a => a.Date).Select(a => a.Date).Distinct().ToList();
            var streakStart = 0;
            for (var i = 1; i <= days.Count; i++)
            {
                if (i < days.Count && days[i] == days[i - 1].AddDays(1))
                    continue;

                var streakLength = i - streakStart;
                if (streakLength > MaxConsecutiveDays)
                {
                    var overflowDay = days[streakStart + MaxConsecutiveDays];
                    var assignment = group.First(a => a.Date == overflowDay);
                    result.Errors.Add(new ValidationIssue(
                        ValidationIssueCode.TooManyConsecutiveDays,
                        $"Mehr als {MaxConsecutiveDays} aufeinanderfolgende Arbeitstage.",
                        EmployeeId: group.Key, ShiftAssignmentId: assignment.Id));
                }
                streakStart = i;
            }
        }
    }
}
