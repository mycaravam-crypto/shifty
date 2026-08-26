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

    public static List<ShiftSuggestion> Suggest(DateOnly date, ShiftType shiftType, SchedulingContext context)
    {
        var hypotheticalStart = date.ToDateTime(shiftType.StartTime);
        var hypotheticalEnd = date.ToDateTime(shiftType.EndTime);
        var scheduleDays = context.ScheduleEnd.DayNumber - context.ScheduleStart.DayNumber + 1;

        var results = new List<ShiftSuggestion>();

        foreach (var employee in context.CandidateEmployees)
        {
            var reasons = new List<SuggestionReason>();
            var eligible = true;
            decimal score = 0;

            if (employee.EligibleShiftTypes.Count > 0 && employee.EligibleShiftTypes.All(s => s.Id != shiftType.Id))
            {
                eligible = false;
                reasons.Add(new(SuggestionReasonCode.NotEligible, "Nicht für diese Schichtart freigegeben."));
            }

            if (context.Absences.Any(a => a.EmployeeId == employee.Id && a.From <= date && a.To >= date))
            {
                eligible = false;
                reasons.Add(new(SuggestionReasonCode.Absent, "Abwesend an diesem Tag."));
            }

            var employeeHistory = context.HistoryAssignments.Where(a => a.EmployeeId == employee.Id).ToList();

            // issue #8 (RestTimeValidator): only the shifts immediately adjacent to the
            // hypothetical one can violate the minimum rest — earlier/later pairs are already
            // covered by the real ScheduleValidator run over the placed assignments.
            var neighbours = employeeHistory
                .Where(a => a.EndTime > a.StartTime) // cross-midnight shifts unsupported — issue #11
                .Select(a => (Start: a.Date.ToDateTime(a.StartTime), End: a.Date.ToDateTime(a.EndTime)))
                .Append((Start: hypotheticalStart, End: hypotheticalEnd))
                .OrderBy(x => x.Start)
                .ToList();
            var hypotheticalIndex = neighbours.FindIndex(x => x.Start == hypotheticalStart && x.End == hypotheticalEnd);
            var insufficientRest =
                (hypotheticalIndex > 0 && neighbours[hypotheticalIndex].Start - neighbours[hypotheticalIndex - 1].End < TimeSpan.FromHours(MinRestHours))
                || (hypotheticalIndex < neighbours.Count - 1 && neighbours[hypotheticalIndex + 1].Start - neighbours[hypotheticalIndex].End < TimeSpan.FromHours(MinRestHours));
            if (insufficientRest)
            {
                eligible = false;
                reasons.Add(new(SuggestionReasonCode.InsufficientRest, $"Ruhezeit von {MinRestHours}h wäre unterschritten."));
            }

            // issue #9 (ConsecutiveDaysValidator): longest run of worked days containing `date`.
            var workedDays = employeeHistory.Select(a => a.Date).Distinct().ToHashSet();
            workedDays.Add(date);
            var streakLength = 1;
            for (var d = date.AddDays(-1); workedDays.Contains(d); d = d.AddDays(-1)) streakLength++;
            for (var d = date.AddDays(1); workedDays.Contains(d); d = d.AddDays(1)) streakLength++;
            if (streakLength > MaxConsecutiveDays)
            {
                eligible = false;
                reasons.Add(new(SuggestionReasonCode.TooManyConsecutiveDays, $"Mehr als {MaxConsecutiveDays} aufeinanderfolgende Arbeitstage."));
            }

            // ShiftOverlapValidator only Warns about a second shift the same day — scored, not excluding.
            if (employeeHistory.Any(a => a.Date == date))
            {
                score -= 3;
                reasons.Add(new(SuggestionReasonCode.AlreadyAssignedThatDay, "Hat an diesem Tag bereits eine Schicht."));
            }

            var shiftPref = context.ShiftTypePreferences.FirstOrDefault(p => p.EmployeeId == employee.Id && p.ShiftTypeId == shiftType.Id);
            if (shiftPref?.Level == PreferenceLevel.Preferred)
            {
                score += 2;
                reasons.Add(new(SuggestionReasonCode.ShiftTypePreferred, "Bevorzugt diese Schichtart."));
            }
            else if (shiftPref?.Level == PreferenceLevel.Avoid)
            {
                score -= 2;
                reasons.Add(new(SuggestionReasonCode.ShiftTypeAvoided, "Möchte diese Schichtart vermeiden."));
            }

            var weekdayPref = context.WeekdayPreferences.FirstOrDefault(p => p.EmployeeId == employee.Id && p.DayOfWeek == date.DayOfWeek);
            if (weekdayPref?.Level == PreferenceLevel.Preferred)
            {
                score += 1;
                reasons.Add(new(SuggestionReasonCode.WeekdayPreferred, "Bevorzugt diesen Wochentag."));
            }
            else if (weekdayPref?.Level == PreferenceLevel.Avoid)
            {
                score -= 1;
                reasons.Add(new(SuggestionReasonCode.WeekdayAvoided, "Möchte diesen Wochentag vermeiden."));
            }

            // Same expected-vs-actual math as ContractValidator, but as a small nudge toward
            // whoever's furthest under their contracted hours for this Schedule rather than a
            // hard check — the real over-hours enforcement still lives in ContractValidator.
            var contract = context.Contracts
                .Where(c => c.EmployeeId == employee.Id && c.ValidFrom <= context.ScheduleStart && (c.ValidTo is null || c.ValidTo >= context.ScheduleStart))
                .MaxBy(c => c.ValidFrom);
            if (contract is not null)
            {
                var absenceDays = context.Absences
                    .Where(a => a.EmployeeId == employee.Id)
                    .Sum(a => WorkingTimeCalculator.OverlapDays(a.From, a.To, context.ScheduleStart, context.ScheduleEnd));
                var effectiveDays = Math.Max(0, scheduleDays - absenceDays);
                var expectedHours = contract.WeeklyHours * effectiveDays / 7m;
                var plannedHours = context.ScheduleAssignments
                    .Where(a => a.EmployeeId == employee.Id)
                    .Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
                if (plannedHours < expectedHours)
                {
                    score += 1;
                    reasons.Add(new(SuggestionReasonCode.UnderContractTarget, "Liegt aktuell unter der Vertrags-Sollstunden-Zahl."));
                }
            }

            results.Add(new ShiftSuggestion(employee.Id, eligible, score, reasons));
        }

        return results.OrderByDescending(r => r.Eligible).ThenByDescending(r => r.Score).ToList();
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
