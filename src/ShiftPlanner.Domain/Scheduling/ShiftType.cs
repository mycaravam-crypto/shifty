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
}
