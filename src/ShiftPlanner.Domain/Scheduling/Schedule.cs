namespace ShiftPlanner.Domain.Scheduling;

public enum ScheduleStatus { Draft, Published, Archived }

// A named planning period ("Wochenplan") that owns its ShiftAssignments — readme.md §7.
public class Schedule
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public ScheduleStatus Status { get; private set; } = ScheduleStatus.Draft;

    // issue #68: set only by Publish() below, never by the general-purpose Update endpoint — a
    // Schedule only ever transitions Draft -> Published through that one gated path, so these
    // two are always set/unset together. issue #97: private setters + the Publish/Archive
    // methods below make that rule an invariant of the type itself, not just a comment/
    // controller-level convention — the only way to change Status is through them.
    public DateTimeOffset? PublishedAt { get; private set; }
    public string? PublishedBy { get; private set; }

    /// <summary>
    /// Draft -&gt; Published. Throws <see cref="InvalidOperationException"/> unless the Schedule
    /// is currently Draft. Callers (SchedulesController) are expected to have already run the
    /// blocking-Errors validation check before calling this — this method only enforces the
    /// starting-state invariant, not business/validation rules.
    /// </summary>
    public void Publish(string publishedBy, DateTimeOffset at)
    {
        if (Status != ScheduleStatus.Draft)
            throw new InvalidOperationException($"Cannot publish a Schedule that is {Status}; only a Draft schedule can be published.");

        Status = ScheduleStatus.Published;
        PublishedAt = at;
        PublishedBy = publishedBy;
    }

    /// <summary>
    /// Published -&gt; Archived. Throws <see cref="InvalidOperationException"/> unless the
    /// Schedule is currently Published.
    /// </summary>
    public void Archive()
    {
        if (Status != ScheduleStatus.Published)
            throw new InvalidOperationException($"Cannot archive a Schedule that is {Status}; only a Published schedule can be archived.");

        Status = ScheduleStatus.Archived;
    }
}
