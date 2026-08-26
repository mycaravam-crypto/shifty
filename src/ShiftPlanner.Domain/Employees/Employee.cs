using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Domain.Employees;

public class Employee
{
    // issue #103: backing fields for the three navigation collections below, kept mutable
    // internally but exposed publicly only as read-only views + dedicated Add/Remove/Replace
    // methods — external code (controllers, validators, ShiftSuggestionEngine) used to be able
    // to reassign or mutate these lists directly, which none of them should do except through
    // the intentional full-replace PUT endpoints. EF Core discovers these fields by naming
    // convention (`_camelCase` matching the PascalCase property) and, since the properties have
    // no public setter, uses the fields themselves for both reads and materialization — no
    // explicit HasField/UsePropertyAccessMode configuration needed (same pattern EF Core's own
    // docs use for read-only collection navigations).
    private readonly List<ShiftType> _eligibleShiftTypes = [];
    private readonly List<ShiftTypePreference> _shiftTypePreferences = [];
    private readonly List<WeekdayPreference> _weekdayPreferences = [];

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
    public IReadOnlyCollection<ShiftType> EligibleShiftTypes => _eligibleShiftTypes;

    // readme.md §17's "später können hier Arbeitszeitpräferenzen ergänzt werden" — which
    // shift types/weekdays this employee prefers or would rather avoid, feeding
    // ShiftSuggestionEngine. Absence of a row for a given ShiftType/DayOfWeek means neutral.
    public IReadOnlyCollection<ShiftTypePreference> ShiftTypePreferences => _shiftTypePreferences;
    public IReadOnlyCollection<WeekdayPreference> WeekdayPreferences => _weekdayPreferences;

    public void AddEligibleShiftType(ShiftType shiftType) => _eligibleShiftTypes.Add(shiftType);

    public void RemoveEligibleShiftType(ShiftType shiftType) => _eligibleShiftTypes.Remove(shiftType);

    // Full-replace semantic backing PUT /employees/{id}/eligible-shift-types — clears and
    // re-adds so the endpoint's behavior (and JSON contract) is unchanged from the outside.
    public void ReplaceEligibleShiftTypes(IEnumerable<ShiftType> shiftTypes)
    {
        _eligibleShiftTypes.Clear();
        _eligibleShiftTypes.AddRange(shiftTypes);
    }

    public void AddShiftTypePreference(ShiftTypePreference preference) => _shiftTypePreferences.Add(preference);

    public void RemoveShiftTypePreference(ShiftTypePreference preference) => _shiftTypePreferences.Remove(preference);

    public void AddWeekdayPreference(WeekdayPreference preference) => _weekdayPreferences.Add(preference);

    public void RemoveWeekdayPreference(WeekdayPreference preference) => _weekdayPreferences.Remove(preference);
}
