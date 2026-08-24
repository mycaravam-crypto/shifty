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

// readme.md §17's "später können hier Arbeitszeitpräferenzen ergänzt werden" — ranks
// employees for one open (date, ShiftType) slot so a manager filling in a Dienstplan doesn't
// have to check eligibility/rest-time/preferences by hand for every candidate.
//
// `Eligible` mirrors exactly the four rules ScheduleValidator treats as Errors for this
// employee/date/shiftType combination (EligibilityValidator, AbsenceValidator,
// RestTimeValidator, ConsecutiveDaysValidator) — a suggestion engine shouldn't recommend
// something the validator would immediately flag red. Everything ShiftOverlapValidator only
// Warns about (a second shift the same day) stays a scored, non-excluding reason instead, same
// severity split as the validators themselves.
public static class ShiftSuggestionEngine
{
    private const int MinRestHours = 11;
    private const int MaxConsecutiveDays = 6;

    public static List<ShiftSuggestion> Suggest(
        DateOnly date,
        ShiftType shiftType,
        IReadOnlyList<Employee> candidateEmployees,
        IReadOnlyList<ShiftAssignment> historyAssignments,
        IReadOnlyList<Absence> absences,
        DateOnly scheduleStart,
        DateOnly scheduleEnd,
        IReadOnlyList<ShiftAssignment> scheduleAssignments,
        IReadOnlyList<Contract> contracts,
        IReadOnlyList<ShiftTypePreference> shiftTypePreferences,
        IReadOnlyList<WeekdayPreference> weekdayPreferences)
    {
        var hypotheticalStart = date.ToDateTime(shiftType.StartTime);
        var hypotheticalEnd = date.ToDateTime(shiftType.EndTime);
        var scheduleDays = scheduleEnd.DayNumber - scheduleStart.DayNumber + 1;

        var results = new List<ShiftSuggestion>();

        foreach (var employee in candidateEmployees)
        {
            var reasons = new List<SuggestionReason>();
            var eligible = true;
            decimal score = 0;

            if (employee.EligibleShiftTypes.Count > 0 && employee.EligibleShiftTypes.All(s => s.Id != shiftType.Id))
            {
                eligible = false;
                reasons.Add(new(SuggestionReasonCode.NotEligible, "Nicht für diese Schichtart freigegeben."));
            }

            if (absences.Any(a => a.EmployeeId == employee.Id && a.From <= date && a.To >= date))
            {
                eligible = false;
                reasons.Add(new(SuggestionReasonCode.Absent, "Abwesend an diesem Tag."));
            }

            var employeeHistory = historyAssignments.Where(a => a.EmployeeId == employee.Id).ToList();

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

            var shiftPref = shiftTypePreferences.FirstOrDefault(p => p.EmployeeId == employee.Id && p.ShiftTypeId == shiftType.Id);
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

            var weekdayPref = weekdayPreferences.FirstOrDefault(p => p.EmployeeId == employee.Id && p.DayOfWeek == date.DayOfWeek);
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
            var contract = contracts
                .Where(c => c.EmployeeId == employee.Id && c.ValidFrom <= scheduleStart && (c.ValidTo is null || c.ValidTo >= scheduleStart))
                .MaxBy(c => c.ValidFrom);
            if (contract is not null)
            {
                var absenceDays = absences
                    .Where(a => a.EmployeeId == employee.Id)
                    .Sum(a => WorkingTimeCalculator.OverlapDays(a.From, a.To, scheduleStart, scheduleEnd));
                var effectiveDays = Math.Max(0, scheduleDays - absenceDays);
                var expectedHours = contract.WeeklyHours * effectiveDays / 7m;
                var plannedHours = scheduleAssignments
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
}
