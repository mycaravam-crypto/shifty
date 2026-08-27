using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// readme.md §12/§13: two shifts for the same employee that overlap in time are a Warning, not a
// hard Error (matches the readme's own ValidationResult example).
//
// issue #157: used to group by (EmployeeId, Date) and only compare shifts sharing the exact same
// calendar Date — correct as long as every shift was same-day, but an overnight shift bleeding
// into the morning can now overlap with an early shift on the *next* calendar day, which that
// grouping could never see. Redesigned to group by EmployeeId only and compare each employee's
// assignments by absolute start/end instant (Date+StartTime, and Date+EndTime pushed a day later
// when EndsNextDay) — a strict generalization: for an employee with only same-day shifts this
// produces the exact same adjacent-pair comparisons as before (sorting by absolute start time
// clusters same-day shifts together, same as the old per-day grouping did), it just also catches
// the cross-midnight case the old grouping structurally couldn't.
public static class ShiftOverlapValidator
{
    public static void Validate(IReadOnlyList<ShiftAssignment> assignments, ValidationResult result)
    {
        foreach (var group in assignments.GroupBy(a => a.EmployeeId))
        {
            var ordered = group
                .Select(a => (Assignment: a,
                    Start: a.Date.ToDateTime(a.StartTime),
                    End: a.Date.ToDateTime(a.EndTime).AddDays(a.EndsNextDay ? 1 : 0)))
                .OrderBy(x => x.Start)
                .ToList();

            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Start < ordered[i - 1].End)
                {
                    result.Warnings.Add(new ValidationIssue(
                        ValidationIssueCode.ShiftOverlap,
                        $"Überlappende Schichten am {ordered[i].Assignment.Date:yyyy-MM-dd}.",
                        EmployeeId: group.Key, ShiftAssignmentId: ordered[i].Assignment.Id));
                }
            }
        }
    }
}
