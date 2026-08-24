using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;

namespace ShiftPlanner.Domain.Scheduling;

// Single source of truth for net worked hours — readme.md §14.
public static class WorkingTimeCalculator
{
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
}
