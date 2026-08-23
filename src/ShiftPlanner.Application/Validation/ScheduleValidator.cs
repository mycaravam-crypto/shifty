using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// PlanningDomain entry point, readme.md §12: runs every rule for a Schedule's assignments
// and returns one combined ValidationResult.
public static class ScheduleValidator
{
    public static ValidationResult Validate(
        Schedule schedule,
        IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyList<Employee> employees,
        IReadOnlyList<ShiftType> shiftTypes,
        IReadOnlyList<Contract> contracts,
        IReadOnlyList<ShiftAssignment>? historyAssignments = null,
        IReadOnlyList<Absence>? absences = null)
    {
        var result = new ValidationResult();
        var employeesById = employees.ToDictionary(e => e.Id);
        var shiftTypesById = shiftTypes.ToDictionary(s => s.Id);

        ShiftOverlapValidator.Validate(assignments, result);
        EligibilityValidator.Validate(assignments, employeesById, result);
        BreakMinutesValidator.Validate(assignments, result);
        StaffingValidator.Validate(assignments, shiftTypesById, result);
        ContractValidator.Validate(schedule, assignments, contracts, absences, result);
        AbsenceValidator.Validate(assignments, absences ?? [], employeesById, result);

        // issues #8/#9: rest time and consecutive-day streaks can span schedule boundaries,
        // so these two run over a wider window when the caller supplies one.
        RestTimeValidator.Validate(historyAssignments ?? assignments, result);
        ConsecutiveDaysValidator.Validate(historyAssignments ?? assignments, result);

        return result;
    }
}
