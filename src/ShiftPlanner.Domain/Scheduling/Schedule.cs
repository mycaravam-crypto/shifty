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

    // issue #68: set only by the PublishSchedule use case (SchedulesController.Publish), never
    // by the general-purpose Update endpoint — a Schedule only ever transitions Draft ->
    // Published through that one gated path, so these two are always set/unset together.
    public DateTimeOffset? PublishedAt { get; set; }
    public string? PublishedBy { get; set; }
}
