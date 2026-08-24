using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// issue #8: ArbZG §5 requires 11h rest between shifts. Needs cross-schedule history, so
// `assignments` here is deliberately wider than the Schedule being validated (see caller).
public static class RestTimeValidator
{
    private const int MinRestHours = 11;

    public static void Validate(IReadOnlyList<ShiftAssignment> assignments, ValidationResult result)
    {
        foreach (var group in assignments.GroupBy(a => a.EmployeeId))
        {
            var ordered = group
                .Where(a => a.EndTime > a.StartTime) // cross-midnight shifts unsupported — issue #11
                .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
                .ToList();

            for (var i = 1; i < ordered.Count; i++)
            {
                var prevEnd = ordered[i - 1].Date.ToDateTime(ordered[i - 1].EndTime);
                var nextStart = ordered[i].Date.ToDateTime(ordered[i].StartTime);
                var rest = nextStart - prevEnd;
                if (rest >= TimeSpan.Zero && rest < TimeSpan.FromHours(MinRestHours))
                {
                    result.Errors.Add(new ValidationIssue(
                        "InsufficientRest",
                        $"Ruhezeit von {MinRestHours}h unterschritten ({rest.TotalHours:F1}h).",
                        EmployeeId: group.Key, ShiftAssignmentId: ordered[i].Id));
                }
            }
        }
    }
}
