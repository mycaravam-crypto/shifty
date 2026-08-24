using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Domain.Employees;

public class Employee
{
    public Guid Id { get; set; }
    public required string PersonnelNumber { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool Active { get; set; } = true;
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }

    // Shift types this employee is allowed to be scheduled for ("mögliche Schichten",
    // readme.md §3). No StaffingValidator/eligibility check yet — that lands with
    // Phase 3 validation once ShiftAssignment exists.
    public List<ShiftType> EligibleShiftTypes { get; set; } = [];

    // readme.md §17's "später können hier Arbeitszeitpräferenzen ergänzt werden" — which
    // shift types/weekdays this employee prefers or would rather avoid, feeding
    // ShiftSuggestionEngine. Absence of a row for a given ShiftType/DayOfWeek means neutral.
    public List<ShiftTypePreference> ShiftTypePreferences { get; set; } = [];
    public List<WeekdayPreference> WeekdayPreferences { get; set; } = [];
}
