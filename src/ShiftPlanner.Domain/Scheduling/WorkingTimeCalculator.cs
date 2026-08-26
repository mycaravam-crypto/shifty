using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;

namespace ShiftPlanner.Domain.Scheduling;

// Single source of truth for net worked hours — readme.md §14.
public static class WorkingTimeCalculator
{
    // issue #101: the single central place to check whether a shift's times are the
    // "supported" same-day kind — cross-midnight shifts (EndTime <= StartTime) are rejected
    // outright at the write boundary (issue #11, SchedulesController/ShiftTypesController) and
    // are out of scope for v1 (real overnight-shift support is the separate issue #81). Several
    // read-side consumers (RestTimeValidator, ShiftSuggestionEngine, and BreakMinutesValidator's
    // own related-but-not-identical check — see its file) independently duplicated an
    // `EndTime > StartTime` comparison as defense-in-depth for pre-existing/malformed data —
    // this gives the controllers and the two exactly-equivalent validators one place to
    // reference instead of three copy-pasted comparisons. This is a direct TimeOnly comparison
    // (no wraparound): `TimeOnly`'s `<`/`>` operators compare ticks directly, unlike its `-`
    // operator (used by NetHours below), which wraps a negative result by adding a full day —
    // so this correctly identifies EndTime == StartTime *and* a genuinely-backwards
    // EndTime < StartTime as invalid.
    public static bool IsValidShiftTiming(TimeOnly start, TimeOnly end) => end > start;

    public static decimal NetHours(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        var minutes = (end - start).TotalMinutes - breakMinutes;
        return (decimal)Math.Max(0, minutes) / 60m;
    }

    // Shared by ContractValidator/HoursBalanceCalculator/DashboardController — how many days
    // of [from,to] fall inside [rangeStart,rangeEnd], used to exclude Absence days from an
    // expected-hours calculation.
    public static int OverlapDays(DateOnly from, DateOnly to, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var start = from > rangeStart ? from : rangeStart;
        var end = to < rangeEnd ? to : rangeEnd;
        return end >= start ? end.DayNumber - start.DayNumber + 1 : 0;
    }

    // issue #56: the "expected hours" half of every expected-vs-actual comparison in this
    // codebase — Contract.WeeklyHours scaled to [from,to]'s day-span, minus Absence days
    // overlapping that range (issue #17). Was duplicated verbatim in ContractValidator,
    // HoursBalanceCalculator, and DashboardController's own private ExpectedHours — extracted
    // here once a 3rd/4th caller (the dashboard's per-employee utilization breakdown) needed
    // the exact same formula, same reasoning OverlapDays itself was extracted for.
    public static decimal ExpectedHours(
        Contract? contract, IReadOnlyList<Absence> absences, Guid employeeId, DateOnly from, DateOnly to)
    {
        if (contract is null)
            return 0m;

        var days = to.DayNumber - from.DayNumber + 1;
        var absenceDays = absences.Where(a => a.EmployeeId == employeeId)
            .Sum(a => OverlapDays(a.From, a.To, from, to));
        var effectiveDays = Math.Max(0, days - absenceDays);
        return contract.WeeklyHours * effectiveDays / 7m;
    }

    // issue #102: extracted from SchedulesController.CopyMonth's inline day-of-month arithmetic
    // so it's unit-testable (a controller method touching EF Core isn't, per this codebase's
    // testing pattern — see CLAUDE.md). "Same day-of-month, one/more months later" clamped into
    // the target month when it's shorter than the source (e.g. Jan 31 -> Feb 28/29). This is a
    // pin-down of CopyMonth's pre-existing behavior, not a behavior change: when two source
    // dates both clamp onto the same target day (e.g. the 30th and 31st both -> Feb 28), that is
    // accepted as-is — two source shifts landing on the same target date after clamping is a
    // legitimate "day doesn't exist in the target month" outcome, not itself treated as a bug
    // here (see ScheduleValidator's ShiftOverlapValidator, which already only Warns on same-
    // employee/same-day overlap rather than rejecting it).
    public static DateOnly ClampDayToMonth(DateOnly sourceDate, int targetYear, int targetMonth)
    {
        var daysInTargetMonth = DateTime.DaysInMonth(targetYear, targetMonth);
        var day = Math.Min(sourceDate.Day, daysInTargetMonth);
        return new DateOnly(targetYear, targetMonth, day);
    }
}
