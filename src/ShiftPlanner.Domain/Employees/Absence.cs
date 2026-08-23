namespace ShiftPlanner.Domain.Employees;

public enum AbsenceType
{
    Vacation,
    Sick,
    Training,
    Other
}

// readme.md §8: "Darf dieser Mitarbeiter an diesem Zeitpunkt eingeplant werden?" — a closed
// date range an employee is unavailable for scheduling. Like Contract, this intentionally does
// not live on Employee directly (an employee has many, over time).
public class Absence
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public AbsenceType Type { get; set; }
    public string? Comment { get; set; }
}
