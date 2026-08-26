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

    // Optional — not every deployment tracks wages (issue #14). Null means labor cost isn't
    // computed for shifts covered by this contract.
    public decimal? HourlyRate { get; set; }

    // issue #116: the plain setters above stay unvalidated on purpose — EF Core materializes
    // rows straight through them, and the DB is assumed to already hold valid data, so forcing
    // a check into every load would be pure overhead. `Create` is the validating entry point
    // controllers (and any other direct consumer) should use when building a new Contract;
    // `Validate` is exposed separately so a caller that mutates ValidFrom/ValidTo on an
    // already-tracked instance (e.g. an update) can re-check the invariant before saving.
    public static Contract Create(
        Guid id, Guid employeeId, DateOnly validFrom, DateOnly? validTo,
        decimal weeklyHours, int workingDaysPerWeek, decimal dailyTargetHours, decimal? hourlyRate)
    {
        var contract = new Contract
        {
            Id = id,
            EmployeeId = employeeId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            WeeklyHours = weeklyHours,
            WorkingDaysPerWeek = workingDaysPerWeek,
            DailyTargetHours = dailyTargetHours,
            HourlyRate = hourlyRate,
        };
        contract.Validate();
        return contract;
    }

    // Throws ArgumentException when ValidTo is set and precedes ValidFrom. A null ValidTo means
    // "still active" and is always valid.
    public void Validate()
    {
        if (ValidTo is { } validTo && validTo < ValidFrom)
        {
            throw new ArgumentException(
                $"ValidTo ({validTo:yyyy-MM-dd}) must not be before ValidFrom ({ValidFrom:yyyy-MM-dd}).");
        }
    }
}
