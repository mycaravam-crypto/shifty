using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Suggestions;

public enum SuggestionReasonCode
{
    NotEligible,
    Absent,
    InsufficientRest,
    TooManyConsecutiveDays,
    AlreadyAssignedThatDay,
    ShiftTypePreferred,
    ShiftTypeAvoided,
    WeekdayPreferred,
    WeekdayAvoided,
    UnderContractTarget,
}

public record SuggestionReason(SuggestionReasonCode Code, string Message);

public record ShiftSuggestion(Guid EmployeeId, bool Eligible, decimal Score, List<SuggestionReason> Reasons);

// issue #63: one proposed pick from AutoFill — a (date, ShiftType) open slot paired with the
// top-ranked eligible employee `Suggest` returned for it, at the moment it was picked.
public record AutoFillProposal(Guid EmployeeId, Guid ShiftTypeId, DateOnly Date, decimal Score, List<SuggestionReason> Reasons);

// issue #99: bundles the data `Suggest`/`AutoFill` need beyond the slot-specific parameters
// (date/ShiftType for `Suggest`, the range/ShiftType list for `AutoFill`) into one type, so the
// compiler catches an accidentally-swapped `ScheduleAssignments`/`HistoryAssignments` (both
// IReadOnlyList<ShiftAssignment>, previously two same-typed positional parameters) or a
// swapped `ScheduleStart`/`ScheduleEnd` the way it never could with a long flat parameter list.
// Pure data bundle, no behavior of its own — everything here is exactly what both methods
// already took, just grouped.
public record SchedulingContext(
    DateOnly ScheduleStart,
    DateOnly ScheduleEnd,
    IReadOnlyList<Employee> CandidateEmployees,
    IReadOnlyList<ShiftAssignment> ScheduleAssignments,
    IReadOnlyList<ShiftAssignment> HistoryAssignments,
    IReadOnlyList<Absence> Absences,
    IReadOnlyList<Contract> Contracts,
    IReadOnlyList<ShiftTypePreference> ShiftTypePreferences,
    IReadOnlyList<WeekdayPreference> WeekdayPreferences);

// readme.md §17's "später können hier Arbeitszeitpräferenzen ergänzt werden" — ranks
// employees for one open (date, ShiftType) slot so a manager filling in a Dienstplan doesn't
// have to check eligibility/rest-time/preferences by hand for every candidate.
//
// `Eligible` mirrors exactly the four rules ScheduleValidator treats as Errors for that
// employee/date/shiftType combination (EligibilityValidator, AbsenceValidator,
// RestTimeValidator, ConsecutiveDaysValidator) — a suggestion engine shouldn't recommend
// something the validator would immediately flag red. Everything ShiftOverlapValidator only
// Warns about (a second shift the same day) stays a scored, non-excluding reason instead, same
// severity split as the validators themselves.
public static class ShiftSuggestionEngine
{
    private const int MinRestHours = 11;
    private const int MaxConsecutiveDays = 6;

    // issue #114: named scoring constants instead of inline magic numbers, matching the
    // MinRestHours/MaxConsecutiveDays convention above.
    private const decimal AlreadyAssignedPenalty = -3m;
    private const decimal ShiftTypePreferredBonus = 2m;
    private const decimal ShiftTypeAvoidPenalty = -2m;
    private const decimal WeekdayPreferredBonus = 1m;
    private const decimal WeekdayAvoidPenalty = -1m;
    private const decimal UnderContractTargetBonus = 1m;

    // issue #115: one rule's verdict — whether it excludes the candidate outright, the score
    // delta it contributes (0 for a purely eligibility-gating rule), and the reason to surface
    // to the caller (null when the rule has nothing to report, e.g. a contract-target check
    // that isn't currently under target).
    internal readonly record struct RuleOutcome(bool Eligible, decimal ScoreDelta, SuggestionReason? Reason)
    {
        internal static readonly RuleOutcome None = new(true, 0m, null);

        internal static RuleOutcome Exclude(SuggestionReasonCode code, string message) =>
            new(false, 0m, new SuggestionReason(code, message));

        internal static RuleOutcome Scored(decimal delta, SuggestionReasonCode code, string message) =>
            new(true, delta, new SuggestionReason(code, message));
    }

    public static List<ShiftSuggestion> Suggest(DateOnly date, ShiftType shiftType, SchedulingContext context)
    {
        var hypotheticalStart = date.ToDateTime(shiftType.StartTime);
        var hypotheticalEnd = date.ToDateTime(shiftType.EndTime);
        var scheduleDays = context.ScheduleEnd.DayNumber - context.ScheduleStart.DayNumber + 1;

        var results = new List<ShiftSuggestion>();

        foreach (var employee in context.CandidateEmployees)
        {
            var employeeHistory = context.HistoryAssignments.Where(a => a.EmployeeId == employee.Id).ToList();

            // Each rule is independent of the others (issue #115) — evaluated in the same order
            // the original inline checks ran, so reason ordering is unchanged.
            var outcomes = new[]
            {
                EvaluateEligibility(employee, shiftType),
                EvaluateAbsence(employee, date, context.Absences),
                EvaluateRestTime(employeeHistory, hypotheticalStart, hypotheticalEnd),
                EvaluateConsecutiveDays(employeeHistory, date),
                EvaluateSameDayOverlap(employeeHistory, date),
                EvaluateShiftTypePreference(employee, shiftType, context.ShiftTypePreferences),
                EvaluateWeekdayPreference(employee, date, context.WeekdayPreferences),
                EvaluateContractTarget(employee, context.ScheduleStart, context.ScheduleEnd, scheduleDays, context.Absences, context.Contracts, context.ScheduleAssignments),
            };

            var eligible = true;
            decimal score = 0;
            var reasons = new List<SuggestionReason>();
            foreach (var outcome in outcomes)
            {
                eligible &= outcome.Eligible;
                score += outcome.ScoreDelta;
                if (outcome.Reason is not null)
                    reasons.Add(outcome.Reason);
            }

            results.Add(new ShiftSuggestion(employee.Id, eligible, score, reasons));
        }

        return results.OrderByDescending(r => r.Eligible).ThenByDescending(r => r.Score).ToList();
    }

    // EligibilityValidator mirror: an employee with a non-empty EligibleShiftTypes list that
    // doesn't include this ShiftType is excluded outright.
    internal static RuleOutcome EvaluateEligibility(Employee employee, ShiftType shiftType) =>
        employee.EligibleShiftTypes.Count > 0 && employee.EligibleShiftTypes.All(s => s.Id != shiftType.Id)
            ? RuleOutcome.Exclude(SuggestionReasonCode.NotEligible, "Nicht für diese Schichtart freigegeben.")
            : RuleOutcome.None;

    // AbsenceValidator mirror: an Absence spanning this date excludes the employee outright.
    internal static RuleOutcome EvaluateAbsence(Employee employee, DateOnly date, IReadOnlyList<Absence> absences) =>
        absences.Any(a => a.EmployeeId == employee.Id && a.From <= date && a.To >= date)
            ? RuleOutcome.Exclude(SuggestionReasonCode.Absent, "Abwesend an diesem Tag.")
            : RuleOutcome.None;

    // RestTimeValidator mirror (issue #8): only the shifts immediately adjacent to the
    // hypothetical one can violate the minimum rest — earlier/later pairs are already covered
    // by the real ScheduleValidator run over the placed assignments.
    internal static RuleOutcome EvaluateRestTime(
        IReadOnlyList<ShiftAssignment> employeeHistory, DateTime hypotheticalStart, DateTime hypotheticalEnd)
    {
        var neighbours = employeeHistory
            // cross-midnight shifts unsupported — issue #11; centralized via
            // WorkingTimeCalculator.IsValidShiftTiming (issue #101), same as
            // RestTimeValidator's identical filter — no behavior change.
            .Where(a => WorkingTimeCalculator.IsValidShiftTiming(a.StartTime, a.EndTime))
            .Select(a => (Start: a.Date.ToDateTime(a.StartTime), End: a.Date.ToDateTime(a.EndTime)))
            .Append((Start: hypotheticalStart, End: hypotheticalEnd))
            .OrderBy(x => x.Start)
            .ToList();
        var hypotheticalIndex = neighbours.FindIndex(x => x.Start == hypotheticalStart && x.End == hypotheticalEnd);
        var insufficientRest =
            (hypotheticalIndex > 0 && neighbours[hypotheticalIndex].Start - neighbours[hypotheticalIndex - 1].End < TimeSpan.FromHours(MinRestHours))
            || (hypotheticalIndex < neighbours.Count - 1 && neighbours[hypotheticalIndex + 1].Start - neighbours[hypotheticalIndex].End < TimeSpan.FromHours(MinRestHours));

        return insufficientRest
            ? RuleOutcome.Exclude(SuggestionReasonCode.InsufficientRest, $"Ruhezeit von {MinRestHours}h wäre unterschritten.")
            : RuleOutcome.None;
    }

    // ConsecutiveDaysValidator mirror (issue #9): longest run of worked days containing `date`.
    internal static RuleOutcome EvaluateConsecutiveDays(IReadOnlyList<ShiftAssignment> employeeHistory, DateOnly date)
    {
        var workedDays = employeeHistory.Select(a => a.Date).Distinct().ToHashSet();
        workedDays.Add(date);
        var streakLength = 1;
        for (var d = date.AddDays(-1); workedDays.Contains(d); d = d.AddDays(-1)) streakLength++;
        for (var d = date.AddDays(1); workedDays.Contains(d); d = d.AddDays(1)) streakLength++;

        return streakLength > MaxConsecutiveDays
            ? RuleOutcome.Exclude(SuggestionReasonCode.TooManyConsecutiveDays, $"Mehr als {MaxConsecutiveDays} aufeinanderfolgende Arbeitstage.")
            : RuleOutcome.None;
    }

    // ShiftOverlapValidator only Warns about a second shift the same day — scored, not excluding.
    internal static RuleOutcome EvaluateSameDayOverlap(IReadOnlyList<ShiftAssignment> employeeHistory, DateOnly date) =>
        employeeHistory.Any(a => a.Date == date)
            ? RuleOutcome.Scored(AlreadyAssignedPenalty, SuggestionReasonCode.AlreadyAssignedThatDay, "Hat an diesem Tag bereits eine Schicht.")
            : RuleOutcome.None;

    internal static RuleOutcome EvaluateShiftTypePreference(
        Employee employee, ShiftType shiftType, IReadOnlyList<ShiftTypePreference> shiftTypePreferences)
    {
        var pref = shiftTypePreferences.FirstOrDefault(p => p.EmployeeId == employee.Id && p.ShiftTypeId == shiftType.Id);
        return pref?.Level switch
        {
            PreferenceLevel.Preferred => RuleOutcome.Scored(ShiftTypePreferredBonus, SuggestionReasonCode.ShiftTypePreferred, "Bevorzugt diese Schichtart."),
            PreferenceLevel.Avoid => RuleOutcome.Scored(ShiftTypeAvoidPenalty, SuggestionReasonCode.ShiftTypeAvoided, "Möchte diese Schichtart vermeiden."),
            _ => RuleOutcome.None,
        };
    }

    internal static RuleOutcome EvaluateWeekdayPreference(
        Employee employee, DateOnly date, IReadOnlyList<WeekdayPreference> weekdayPreferences)
    {
        var pref = weekdayPreferences.FirstOrDefault(p => p.EmployeeId == employee.Id && p.DayOfWeek == date.DayOfWeek);
        return pref?.Level switch
        {
            PreferenceLevel.Preferred => RuleOutcome.Scored(WeekdayPreferredBonus, SuggestionReasonCode.WeekdayPreferred, "Bevorzugt diesen Wochentag."),
            PreferenceLevel.Avoid => RuleOutcome.Scored(WeekdayAvoidPenalty, SuggestionReasonCode.WeekdayAvoided, "Möchte diesen Wochentag vermeiden."),
            _ => RuleOutcome.None,
        };
    }

    // Same expected-vs-actual math as ContractValidator, but as a small nudge toward whoever's
    // furthest under their contracted hours for this Schedule rather than a hard check — the
    // real over-hours enforcement still lives in ContractValidator.
    internal static RuleOutcome EvaluateContractTarget(
        Employee employee,
        DateOnly scheduleStart,
        DateOnly scheduleEnd,
        int scheduleDays,
        IReadOnlyList<Absence> absences,
        IReadOnlyList<Contract> contracts,
        IReadOnlyList<ShiftAssignment> scheduleAssignments)
    {
        var contract = contracts
            .Where(c => c.EmployeeId == employee.Id && c.ValidFrom <= scheduleStart && (c.ValidTo is null || c.ValidTo >= scheduleStart))
            .MaxBy(c => c.ValidFrom);
        if (contract is null)
            return RuleOutcome.None;

        var absenceDays = absences
            .Where(a => a.EmployeeId == employee.Id)
            .Sum(a => WorkingTimeCalculator.OverlapDays(a.From, a.To, scheduleStart, scheduleEnd));
        var effectiveDays = Math.Max(0, scheduleDays - absenceDays);
        var expectedHours = contract.WeeklyHours * effectiveDays / 7m;
        var plannedHours = scheduleAssignments
            .Where(a => a.EmployeeId == employee.Id)
            .Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));

        return plannedHours < expectedHours
            ? RuleOutcome.Scored(UnderContractTargetBonus, SuggestionReasonCode.UnderContractTarget, "Liegt aktuell unter der Vertrags-Sollstunden-Zahl.")
            : RuleOutcome.None;
    }

    // issue #63: bulk/auto-fill mode on top of the single-slot `Suggest` above — walks every
    // open (date, ShiftType) slot in [rangeStart, rangeEnd] (a ShiftType with `MinStaffing` set
    // whose currently-assigned count for that date is below it, StaffingValidator's own
    // grouping) and, for each one, picks the top-ranked ELIGIBLE candidate exactly as a manager
    // clicking "Zuweisen" in ShiftSuggestionModal would. Slots are visited in date-then-
    // ShiftType-name order (deterministic — DB query order otherwise isn't guaranteed) and each
    // pick is folded into a working copy of `context.ScheduleAssignments`/`HistoryAssignments`
    // before the next `Suggest` call, so later slots in the same run correctly see the employee
    // as already-assigned-that-day (scoring), potentially rest-time/consecutive-day-excluded, and
    // further along their contract-hours target — no separate "double-booking" check needed,
    // it falls out of reusing `Suggest` on updated data. A slot with no eligible candidate left
    // is skipped (and, since the candidate pool for that (date, ShiftType) only shrinks as
    // instances are filled, so are any further instances of the same slot) rather than left
    // half-filled with an ineligible pick.
    public static List<AutoFillProposal> AutoFill(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        IReadOnlyList<ShiftType> shiftTypes,
        SchedulingContext context)
    {
        var proposals = new List<AutoFillProposal>();
        var workingSchedule = new List<ShiftAssignment>(context.ScheduleAssignments);
        var workingHistory = new List<ShiftAssignment>(context.HistoryAssignments);

        var staffedShiftTypes = shiftTypes
            .Where(s => s.MinStaffing is not null)
            .OrderBy(s => s.Name).ThenBy(s => s.Id)
            .ToList();

        for (var date = rangeStart; date <= rangeEnd; date = date.AddDays(1))
        {
            foreach (var shiftType in staffedShiftTypes)
            {
                var min = shiftType.MinStaffing!.Value;

                // One `Suggest` call, and at most one pick, per still-open instance of this
                // slot — re-checked from `workingSchedule` every time so a pick just made for
                // an earlier instance today counts toward `min` immediately.
                while (workingSchedule.Count(a => a.Date == date && a.ShiftTypeId == shiftType.Id) < min)
                {
                    var alreadyInSlot = workingSchedule
                        .Where(a => a.Date == date && a.ShiftTypeId == shiftType.Id)
                        .Select(a => a.EmployeeId)
                        .ToHashSet();
                    var candidates = context.CandidateEmployees.Where(e => !alreadyInSlot.Contains(e.Id)).ToList();
                    if (candidates.Count == 0)
                        break;

                    var workingContext = context with
                    {
                        CandidateEmployees = candidates,
                        ScheduleAssignments = workingSchedule,
                        HistoryAssignments = workingHistory,
                    };
                    var ranked = Suggest(date, shiftType, workingContext);
                    var pick = ranked.FirstOrDefault(r => r.Eligible);
                    if (pick is null)
                        break; // no eligible candidate left — skip this and any further instances

                    proposals.Add(new AutoFillProposal(pick.EmployeeId, shiftType.Id, date, pick.Score, pick.Reasons));

                    // Not persisted — just makes this pick visible to the rest of the run, same
                    // shape `SchedulesController.CreateAssignment` would actually write.
                    var hypothetical = new ShiftAssignment
                    {
                        Id = Guid.NewGuid(),
                        ScheduleId = Guid.Empty,
                        EmployeeId = pick.EmployeeId,
                        ShiftTypeId = shiftType.Id,
                        Date = date,
                        StartTime = shiftType.StartTime,
                        EndTime = shiftType.EndTime,
                        BreakMinutes = shiftType.BreakMinutes,
                    };
                    workingSchedule.Add(hypothetical);
                    workingHistory.Add(hypothetical);
                }
            }
        }

        return proposals;
    }
}
