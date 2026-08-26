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

    // issue #116: same shape as Contract.Create/Validate — the plain setters above stay
    // unvalidated for EF Core's materialization path (the DB is assumed to already hold valid
    // rows), while `Create` is the validating entry point controllers should use to build a new
    // Absence, and `Validate` lets a caller re-check the invariant after mutating From/To on an
    // already-tracked instance (e.g. an update) before saving.
    public static Absence Create(Guid id, Guid employeeId, DateOnly from, DateOnly to, AbsenceType type, string? comment)
    {
        var absence = new Absence
        {
            Id = id,
            EmployeeId = employeeId,
            From = from,
            To = to,
            Type = type,
            Comment = comment,
        };
        absence.Validate();
        return absence;
    }

    // Throws ArgumentException when To precedes From.
    public void Validate()
    {
        if (To < From)
            throw new ArgumentException($"To ({To:yyyy-MM-dd}) must not be before From ({From:yyyy-MM-dd}).");
    }
}
