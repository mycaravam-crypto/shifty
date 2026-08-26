using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// issue #10: ArbZG §4 minimums — >=30min break over 6h worked, >=45min over 9h.
public static class BreakMinutesValidator
{
    public static void Validate(IReadOnlyList<ShiftAssignment> assignments, ValidationResult result)
    {
        foreach (var a in assignments)
        {
            var grossMinutes = (a.EndTime - a.StartTime).TotalMinutes;
            if (grossMinutes <= 0)
                continue; // cross-midnight shifts are unsupported for now — issue #11

            var required = grossMinutes switch
            {
                > 9 * 60 => 45,
                > 6 * 60 => 30,
                _ => 0
            };
            if (a.BreakMinutes < required)
            {
                result.Errors.Add(new ValidationIssue(
                    ValidationIssueCode.InsufficientBreak,
                    $"Mindestpause von {required}min unterschritten ({a.BreakMinutes}min).",
                    EmployeeId: a.EmployeeId, ShiftAssignmentId: a.Id));
            }
        }
    }
}
