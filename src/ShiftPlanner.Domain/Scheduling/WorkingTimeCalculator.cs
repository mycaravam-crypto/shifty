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
    //
    // issue #70: this used to take a single already-resolved Contract, picked once at the
    // caller's chosen anchor date (the schedule's *start* date in every caller) and applied to
    // the whole [from,to] span — wrong for any employee whose contract changes mid-span (a
    // Schedule is often a full calendar month). Now takes the full contracts list and resolves
    // the applicable one PER DAY via Contract.ActiveOn, so a mid-schedule contract change is
    // reflected exactly on the day it takes effect rather than for the whole period either way.
    // A day with no contract covering it (a gap between two Contract rows) contributes 0 —
    // there's no obligation to derive an expected-hours figure from.
    public static decimal ExpectedHours(
        IReadOnlyList<Contract> contracts, IReadOnlyList<Absence> absences, Guid employeeId, DateOnly from, DateOnly to)
    {
        var employeeContracts = contracts.Where(c => c.EmployeeId == employeeId).ToList();
        if (employeeContracts.Count == 0)
            return 0m;

        var absenceDays = new HashSet<DateOnly>();
        foreach (var a in absences.Where(a => a.EmployeeId == employeeId))
        {
            var start = a.From > from ? a.From : from;
            var end = a.To < to ? a.To : to;
            for (var day = start; day <= end; day = day.AddDays(1))
                absenceDays.Add(day);
        }

        // Group each non-absence day by whichever contract applies (if any) rather than
        // applying WeeklyHours/7 per day directly — this keeps the single-contract common case
        // doing the exact same "WeeklyHours * effectiveDays / 7" multiply-then-divide-once
        // arithmetic the pre-#70 flat formula used (no decimal-rounding drift from repeatedly
        // summing an unrounded 1/7th share), while still correctly blending multiple segments
        // when a contract changes mid-span: each segment gets its own single multiply/divide.
        var daysByContract = new Dictionary<Guid, int>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (absenceDays.Contains(day))
                continue;

            var contract = Contract.ActiveOn(employeeContracts, employeeId, day);
            if (contract is null)
                continue;

            daysByContract[contract.Id] = daysByContract.GetValueOrDefault(contract.Id) + 1;
        }

        decimal total = 0m;
        foreach (var (contractId, days) in daysByContract)
        {
            var contract = employeeContracts.First(c => c.Id == contractId);
            total += contract.WeeklyHours * days / 7m;
        }
        return total;
    }
}
