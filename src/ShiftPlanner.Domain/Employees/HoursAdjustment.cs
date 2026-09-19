namespace ShiftPlanner.Domain.Employees;

// issue #165: a manual correction to an employee's Ist/Soll hours balance for cases that aren't
// a normal ShiftAssignment edit — a late-arriving sick note, a data-entry correction, a goodwill
// adjustment. Like Absence/Contract, this intentionally does not live on Employee directly (an
// employee has many, over time). HoursDelta is signed: positive credits the employee (reduces an
// under-hours deficit), negative debits them. Reason is required — this exists specifically so
// every deviation from the computed Soll carries a notice, per issue #165's own admin-function ask.
public class HoursAdjustment
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly Date { get; set; }
    public decimal HoursDelta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // Same shape as Absence.Create/Validate — plain setters stay unvalidated for EF Core's
    // materialization path, Create is the validating entry point controllers should use.
    public static HoursAdjustment Create(
        Guid id, Guid employeeId, DateOnly date, decimal hoursDelta, string reason, string createdBy, DateTimeOffset createdAt)
    {
        var adjustment = new HoursAdjustment
        {
            Id = id,
            EmployeeId = employeeId,
            Date = date,
            HoursDelta = hoursDelta,
            Reason = reason,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
        };
        adjustment.Validate();
        return adjustment;
    }

    // Throws ArgumentException when Reason is blank or HoursDelta is zero (a zero-delta
    // "adjustment" isn't a deviation and wouldn't mean anything on the printed report).
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Reason))
            throw new ArgumentException("Reason is required.");
        if (HoursDelta == 0)
            throw new ArgumentException("HoursDelta must not be zero.");
    }
}
