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
                // Deliberately NOT centralized via WorkingTimeCalculator.IsValidShiftTiming
                // (issue #101), unlike RestTimeValidator/ShiftSuggestionEngine's identical-
                // looking filters: TimeOnly's `-` operator (used above for grossMinutes) wraps
                // a negative result by adding a full day, so this check is only ever true when
                // EndTime == StartTime exactly — a genuinely-backwards EndTime < StartTime
                // instead wraps to a *positive* grossMinutes and falls through to the normal
                // break-minutes check below (see ZeroLengthShift_SkippedDefensively and
                // GenuinelyBackwardsShift_NotSkipped_ProcessedWithWrappedDuration in
                // BreakMinutesValidatorTests for the two cases spelled out). Switching this to
                // `!IsValidShiftTiming(...)` would change behavior (skip real cross-midnight
                // rows too), which issue #101 explicitly scopes out — the write boundary
                // (SchedulesController/ShiftTypesController) is what actually rejects such
                // data before it ever reaches this validator.

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
