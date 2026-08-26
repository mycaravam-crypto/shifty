using System.Text.Json.Serialization;

namespace ShiftPlanner.Application.Validation;

// issue #106: every distinct string literal the 7 validators below used to write into
// ValidationIssue.Type directly, now a proper enum instead of free-form strings — mirrors
// ShiftSuggestionEngine.SuggestionReasonCode's existing shape for the equivalent concept.
// [JsonConverter(JsonStringEnumConverter)] keeps the wire format identical to the old string
// literals (this codebase's System.Text.Json setup serializes enums as ordinals by default —
// no global JsonStringEnumConverter registered in Program.cs — so without this attribute the
// JSON contract for ValidationIssue.Type would silently change from a string to a number).
// Member names match the original literals exactly so the serialized string values, and thus
// the frontend's ISSUE_TYPE_LABELS map in ScheduleView.vue, need no changes.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationIssueCode
{
    ShiftOverlap,
    ShiftTypeNotEligible,
    InsufficientBreak,
    Understaffed,
    Overstaffed,
    ContractHoursExceeded,
    AssignedDuringAbsence,
    InsufficientRest,
    TooManyConsecutiveDays,
}

// readme.md §13: validation is never a plain bool, so the UI can show *what* is wrong,
// not just that something is.
public record ValidationIssue(ValidationIssueCode Type, string Message, Guid? EmployeeId = null, Guid? ShiftAssignmentId = null);

public class ValidationResult
{
    public List<ValidationIssue> Errors { get; } = [];
    public List<ValidationIssue> Warnings { get; } = [];
    public bool IsValid => Errors.Count == 0;
}
