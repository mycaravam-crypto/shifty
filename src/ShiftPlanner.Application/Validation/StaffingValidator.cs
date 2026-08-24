using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Validation;

// issue #7: only checks (ShiftType, Date) combinations that actually have assignments —
// a day nobody scheduled a shift for isn't flagged as understaffed. Min/max are optional
// targets, so both directions are Warnings, not Errors.
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
}
