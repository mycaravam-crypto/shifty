namespace ShiftPlanner.Domain.Contracts;

// Historized: an employee can have multiple contracts over time (readme.md §4).
// Contract data intentionally does not live on Employee directly.
public class Contract
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public decimal WeeklyHours { get; set; }
    public int WorkingDaysPerWeek { get; set; }
    public decimal DailyTargetHours { get; set; }
}
