namespace ShiftPlanner.Domain.Scheduling;

// A template ("Vorlage"), not an actual worked shift — readme.md §5.
public class ShiftType
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }
    public required string Color { get; set; }
    public bool Active { get; set; } = true;

    // Optional headcount target per shift instance ("Mindest-/Maximalbesetzung", readme.md
    // §3), checked by StaffingValidator. Null means unconstrained.
    public int? MinStaffing { get; set; }
    public int? MaxStaffing { get; set; }

    // issue #157: mirrors ShiftAssignment.EndsNextDay — lets a template itself represent a
    // recurring overnight shift (e.g. "Nachtschicht" 22:00-06:00) so creating an assignment from
    // it (drag-drop, auto-fill, suggestions) carries the flag through automatically instead of a
    // manager having to set it by hand on every instance.
    public bool EndsNextDay { get; set; }
}
