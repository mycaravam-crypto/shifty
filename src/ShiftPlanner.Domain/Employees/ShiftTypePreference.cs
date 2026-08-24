using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Domain.Employees;

// Which ShiftTypes an employee prefers or would rather avoid — distinct from
// Employee.EligibleShiftTypes (allowed vs. wanted). Feeds ShiftSuggestionEngine.
public class ShiftTypePreference
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ShiftTypeId { get; set; }
    public ShiftType ShiftType { get; set; } = null!;
    public PreferenceLevel Level { get; set; }
}
