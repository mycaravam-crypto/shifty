namespace ShiftPlanner.Domain.Scheduling;

// The actual worked shift — can deviate from its ShiftType template, readme.md §6.
public class ShiftAssignment
{
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid ShiftTypeId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakMinutes { get; set; }

    // issue #58: optional break start time. Null means "unknown/unspecified break timing" —
    // WageCalculator falls back to its existing (unadjusted) night-overlap approximation in
    // that case. When set, it lets WageCalculator subtract the break's own overlap with the
    // night window from the raw shift/night-window overlap for a precise figure.
    public TimeOnly? BreakStartTime { get; set; }
}
