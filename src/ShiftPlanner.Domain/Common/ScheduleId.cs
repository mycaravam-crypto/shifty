namespace ShiftPlanner.Domain.Common;

// issue #107 (finding M4) — sibling wrapper to EmployeeId (see that file's comment for the
// full rationale/scoping). ScheduleId is the second id the issue names explicitly as a
// starting point ("introduce wrappers for at least EmployeeId/ScheduleId").
public readonly record struct ScheduleId(Guid Value)
{
    public static ScheduleId New() => new(Guid.NewGuid());

    public static readonly ScheduleId Empty = new(Guid.Empty);

    public static implicit operator Guid(ScheduleId id) => id.Value;

    public static implicit operator ScheduleId(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
