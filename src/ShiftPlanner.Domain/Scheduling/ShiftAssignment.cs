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

    // issue #81 (step 1 of a "revisit and decide" issue — see the PR description for the full
    // design analysis): schema-only groundwork for real cross-midnight (overnight) shift
    // support. Deliberately UNUSED for now — every calculator/validator below still assumes
    // EndTime > StartTime, and the write boundary (SchedulesController/ShiftTypesController,
    // issue #11/#101) still rejects EndTime <= StartTime outright, so this column is always
    // false for every row that can exist today. It exists purely so a later, explicitly-scoped
    // follow-up issue can (a) start accepting EndTime <= StartTime when this flag is set
    // without a second migration, and (b) update WorkingTimeCalculator/WageCalculator/
    // RestTimeValidator/ConsecutiveDaysValidator/ShiftOverlapValidator/GermanPublicHolidays one
    // at a time against a schema that's already in place. No DTO/controller exposes this field
    // yet — setting it via the API would currently be meaningless (nothing reads it, and the
    // controllers still 400 on backwards times regardless of this flag).
    public bool EndsNextDay { get; set; }

    // issue #156: optimistic concurrency uses Postgres's own xmin system column as the
    // concurrency token (see ApplicationDbContext.OnModelCreating's UseXminAsConcurrencyToken) —
    // it's an EF/Infrastructure-level shadow property (accessed via EF.Property<uint>(a, "xmin")
    // where needed), not a CLR property here, matching how every other entity in this file has no
    // EF Core-specific concerns leaking into Domain.
}
