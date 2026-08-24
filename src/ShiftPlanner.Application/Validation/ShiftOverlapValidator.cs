using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// readme.md §12/§13: two shifts for the same employee on the same day that overlap in time
// are a Warning, not a hard Error (matches the readme's own ValidationResult example).
public static class ShiftOverlapValidator
{
    public static void Validate(IReadOnlyList<ShiftAssignment> assignments, ValidationResult result)
    {
        foreach (var group in assignments.GroupBy(a => (a.EmployeeId, a.Date)))
        {
            var ordered = group.OrderBy(a => a.StartTime).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StartTime < ordered[i - 1].EndTime)
                {
                    result.Warnings.Add(new ValidationIssue(
                        ValidationIssueCode.ShiftOverlap,
                        $"Überlappende Schichten am {group.Key.Date:yyyy-MM-dd}.",
                        group.Key.EmployeeId, ordered[i].Id));
                }
            }
        }
    }
}
