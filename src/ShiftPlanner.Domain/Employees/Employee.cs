using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Domain.Employees;

public class Employee
{
    public Guid Id { get; set; }
    public required string PersonnelNumber { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public bool Active { get; set; } = true;
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }

    // Shift types this employee is allowed to be scheduled for ("mögliche Schichten",
    // readme.md §3). No StaffingValidator/eligibility check yet — that lands with
    // Phase 3 validation once ShiftAssignment exists.
    public List<ShiftType> EligibleShiftTypes { get; set; } = [];
}
