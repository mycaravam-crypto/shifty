namespace ShiftPlanner.Domain.Scheduling;

public enum ScheduleStatus { Draft, Published, Archived }

// A named planning period ("Wochenplan") that owns its ShiftAssignments — readme.md §7.
public class Schedule
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ScheduleStatus Status { get; set; } = ScheduleStatus.Draft;
}
