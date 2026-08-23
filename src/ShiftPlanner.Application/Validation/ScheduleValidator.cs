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
        IReadOnlyList<Contract> contracts)
    {
        var result = new ValidationResult();
        var employeesById = employees.ToDictionary(e => e.Id);
        var shiftTypesById = shiftTypes.ToDictionary(s => s.Id);

        ShiftOverlapValidator.Validate(assignments, result);
        EligibilityValidator.Validate(assignments, employeesById, result);
        BreakMinutesValidator.Validate(assignments, result);
        StaffingValidator.Validate(assignments, shiftTypesById, result);
        ContractValidator.Validate(schedule, assignments, contracts, result);

        return result;
    }
}
