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
}
