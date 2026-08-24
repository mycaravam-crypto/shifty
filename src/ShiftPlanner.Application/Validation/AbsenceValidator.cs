using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// issue #17/readme.md §8: "Darf dieser Mitarbeiter an diesem Zeitpunkt eingeplant werden?" — a
// ShiftAssignment falling within an employee's Absence range is an Error, not a Warning, since
// it means the employee genuinely isn't available (vacation, sick leave, ...).
public static class AbsenceValidator
{
    public static void Validate(
        IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyList<Absence> absences,
        IReadOnlyDictionary<Guid, Employee> employeesById,
        ValidationResult result)
    {
        foreach (var assignment in assignments)
        {
            var absence = absences.FirstOrDefault(a =>
                a.EmployeeId == assignment.EmployeeId && assignment.Date >= a.From && assignment.Date <= a.To);
            if (absence is null)
                continue;

            var name = employeesById.TryGetValue(assignment.EmployeeId, out var employee)
                ? $"{employee.FirstName} {employee.LastName}"
                : "Mitarbeiter";
            result.Errors.Add(new ValidationIssue(
                ValidationIssueCode.AssignedDuringAbsence,
                $"{name} ist am {assignment.Date:yyyy-MM-dd} als {absence.Type} abwesend.",
                assignment.EmployeeId, assignment.Id));
        }
    }
}
