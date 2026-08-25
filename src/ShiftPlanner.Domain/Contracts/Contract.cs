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

    // issue #70: "which contract applies on date X for employee Y" was independently duplicated
    // in five places (SchedulesController.HourlyRateOn, DashboardController.ActiveContract,
    // HoursBalanceCalculator, ShiftSuggestionEngine, ContractValidator) — one Domain-layer helper
    // now backs all of them. A later contract with the same ValidFrom would tie under MaxBy; that
    // can no longer happen going forward since ContractsController now rejects overlapping
    // ranges, but historical data predating that check could still tie, so this stays a
    // best-effort "most recently starting" pick rather than a hard invariant assumption.
    public static Contract? ActiveOn(IEnumerable<Contract> contracts, Guid employeeId, DateOnly date) =>
        contracts
            .Where(c => c.EmployeeId == employeeId && c.ValidFrom <= date && (c.ValidTo is null || c.ValidTo >= date))
            .MaxBy(c => c.ValidFrom);

    // issue #70: two Contract rows for the same employee must not have overlapping validity
    // ranges — ActiveOn's own MaxBy(ValidFrom) would otherwise silently pick one and hide what
    // is almost certainly a data-entry mistake. Null ValidTo means "still active" (open-ended).
    public static bool Overlaps(DateOnly fromA, DateOnly? toA, DateOnly fromB, DateOnly? toB) =>
        fromA <= (toB ?? DateOnly.MaxValue) && fromB <= (toA ?? DateOnly.MaxValue);
}
