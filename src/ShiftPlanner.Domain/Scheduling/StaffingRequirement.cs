namespace ShiftPlanner.Domain.Scheduling;

// issue #69: models staffing demand independently of whether anyone has actually been
// scheduled yet. StaffingValidator/DashboardController previously only ever grouped existing
// ShiftAssignment rows, so a (ShiftType, Date) combination nobody scheduled anyone for produced
// no signal at all — a fully unstaffed day was invisible. A weekly pattern (DayOfWeek), not a
// per-date row, is deliberately lighter: it avoids needing to seed years of per-date data for a
// recurring need like "always at least 2 on Frühschicht on a Monday".
public class StaffingRequirement
{
    public Guid Id { get; set; }

    // Null = applies across all teams, i.e. every assignment for this ShiftType/day counts
    // toward it regardless of which team the assigned employee belongs to. Set = only
    // assignments to employees on that specific Team count.
    public Guid? TeamId { get; set; }

    public Guid ShiftTypeId { get; set; }
    public ShiftType ShiftType { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }

    public int MinimumStaffing { get; set; }
}
