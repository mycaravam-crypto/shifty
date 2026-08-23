using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// issue #6: an employee may only be scheduled for a ShiftType listed in their
// EligibleShiftTypes. An employee with no eligibility list set is treated as unrestricted
// (the field defaults to empty for every employee today; requiring it before use would
// invalidate every existing assignment).
public static class EligibilityValidator
{
    public static void Validate(
        IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyDictionary<Guid, Employee> employeesById,
        ValidationResult result)
    {
        foreach (var assignment in assignments)
        {
            if (!employeesById.TryGetValue(assignment.EmployeeId, out var employee))
                continue;
            if (employee.EligibleShiftTypes.Count == 0)
                continue;
            if (employee.EligibleShiftTypes.Any(s => s.Id == assignment.ShiftTypeId))
                continue;

            result.Errors.Add(new ValidationIssue(
                "ShiftTypeNotEligible",
                $"{employee.FirstName} {employee.LastName} ist für diese Schichtart nicht freigegeben.",
                employee.Id, assignment.Id));
        }
    }
}
