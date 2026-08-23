namespace ShiftPlanner.Application.Validation;

// readme.md §13: validation is never a plain bool, so the UI can show *what* is wrong,
// not just that something is.
public record ValidationIssue(string Type, string Message, Guid? EmployeeId = null, Guid? ShiftAssignmentId = null);

public class ValidationResult
{
    public List<ValidationIssue> Errors { get; } = [];
    public List<ValidationIssue> Warnings { get; } = [];
    public bool IsValid => Errors.Count == 0;
}
