using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// issue #7 (legacy): the group-based check below only ever looks at (ShiftType, Date)
// combinations that already have at least one assignment — a day nobody scheduled a shift for
// isn't flagged as understaffed. Min/max are optional targets, so both directions are Warnings,
// not Errors. Kept as-is for backward compatibility (existing ShiftType.MinStaffing/MaxStaffing
// data still drives it), but issue #69's ValidateRequirements below is what actually closes the
// "invisible fully-unstaffed day" gap this comment used to describe as unsolved.
public static class StaffingValidator
{
    public static void Validate(
        IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyDictionary<Guid, ShiftType> shiftTypesById,
        ValidationResult result)
    {
        foreach (var group in assignments.GroupBy(a => (a.ShiftTypeId, a.Date)))
        {
            if (!shiftTypesById.TryGetValue(group.Key.ShiftTypeId, out var shiftType))
                continue;

            var count = group.Select(a => a.EmployeeId).Distinct().Count();
            if (shiftType.MinStaffing is { } min && count < min)
            {
                result.Warnings.Add(new ValidationIssue(
                    ValidationIssueCode.Understaffed,
                    $"{shiftType.Name} am {group.Key.Date:yyyy-MM-dd}: {count}/{min} besetzt."));
            }
            if (shiftType.MaxStaffing is { } max && count > max)
            {
                result.Warnings.Add(new ValidationIssue(
                    ValidationIssueCode.Overstaffed,
                    $"{shiftType.Name} am {group.Key.Date:yyyy-MM-dd}: {count}/{max} besetzt."));
            }
        }
    }

    // issue #69: models demand independently of existing assignments. Walks the schedule's own
    // date range × every active StaffingRequirement whose DayOfWeek matches, rather than only
    // grouping assignments that already exist — so a (ShiftType, Date) slot with a configured
    // requirement and literally zero assignments is now flagged, which the method above can
    // never do (GroupBy only ever produces groups for rows that exist).
    public static void ValidateRequirements(
        DateOnly scheduleStart,
        DateOnly scheduleEnd,
        IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyDictionary<Guid, ShiftType> shiftTypesById,
        IReadOnlyDictionary<Guid, Employee> employeesById,
        IReadOnlyList<StaffingRequirement> requirements,
        ValidationResult result)
    {
        if (requirements.Count == 0)
            return;

        for (var date = scheduleStart; date <= scheduleEnd; date = date.AddDays(1))
        {
            foreach (var requirement in requirements)
            {
                if (requirement.DayOfWeek != date.DayOfWeek)
                    continue;
                if (!shiftTypesById.TryGetValue(requirement.ShiftTypeId, out var shiftType))
                    continue;

                var count = assignments
                    .Where(a => a.ShiftTypeId == requirement.ShiftTypeId && a.Date == date)
                    .Where(a => requirement.TeamId is not { } teamId
                        || (employeesById.TryGetValue(a.EmployeeId, out var employee) && employee.TeamId == teamId))
                    .Select(a => a.EmployeeId)
                    .Distinct()
                    .Count();

                if (count < requirement.MinimumStaffing)
                {
                    result.Warnings.Add(new ValidationIssue(
                        ValidationIssueCode.Understaffed,
                        $"{shiftType.Name} am {date:yyyy-MM-dd}: {count}/{requirement.MinimumStaffing} besetzt (Bedarf)."));
                }
            }
        }
    }
}
