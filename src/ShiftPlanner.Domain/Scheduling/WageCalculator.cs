namespace ShiftPlanner.Domain.Scheduling;

// Single source of truth for labor cost — mirrors WorkingTimeCalculator (readme.md §14).
// HourlyRate is optional (Contract.HourlyRate, issue #14), so cost is null when unset.
public static class WageCalculator
{
    // issue #16: global percentages, not per-ShiftType or configurable — German surcharge
    // rates are driven by *when* a shift falls, not what kind of shift it is, and nothing
    // yet needs these tunable without a deploy. Typical Tarifvertrag baseline figures.
    // ponytail: hardcoded, move to appsettings if a deployment needs different rates.
    private const decimal NightSurchargeRate = 0.25m;
    private const decimal SundaySurchargeRate = 0.50m;
    private const decimal HolidaySurchargeRate = 1.25m;
    private const int NightStartMinute = 20 * 60; // 20:00
    private const int NightEndMinute = 6 * 60; // 06:00

    public static decimal? LaborCost(decimal netHours, decimal? hourlyRate) =>
        hourlyRate is null ? null : netHours * hourlyRate.Value;

    // Adds night/Sunday/holiday surcharges on top of the base rate. Sunday and holiday don't
    // stack (holiday wins when a holiday falls on a Sunday) but night stacks with either, matching
    // common German practice. Night hours are the raw StartTime–EndTime overlap with 20:00–06:00.
    // issue #58: when BreakStartTime is known, the break's own overlap with the night window is
    // subtracted from that raw figure for a precise count; when it's null (unknown/unspecified
    // break timing — the only case before issue #58 existed), this keeps the original
    // approximation exactly as before, so existing data's computed cost doesn't change.
    public static decimal? LaborCost(TimeOnly startTime, TimeOnly endTime, DayOfWeek dayOfWeek,
        bool isHoliday, decimal netHours, decimal? hourlyRate, int breakMinutes = 0, TimeOnly? breakStartTime = null) =>
        Breakdown(startTime, endTime, dayOfWeek, isHoliday, netHours, hourlyRate, breakMinutes, breakStartTime)?.Total;

    // issue #56: the per-surcharge-type split behind the total LaborCost above — the dashboard's
    // cost-breakdown KPI (regular/night/Sunday/holiday) reuses this instead of re-deriving the
    // surcharge math a second time. Regular + Night + Sunday + Holiday always sums to exactly
    // what the LaborCost overload above returns (Sunday/Holiday are mutually exclusive per the
    // same "holiday wins" rule, so at most one of them is non-zero for a given shift).
    public static LaborCostBreakdown? Breakdown(TimeOnly startTime, TimeOnly endTime, DayOfWeek dayOfWeek,
        bool isHoliday, decimal netHours, decimal? hourlyRate, int breakMinutes = 0, TimeOnly? breakStartTime = null)
    {
        if (hourlyRate is null)
            return null;

        var regular = netHours * hourlyRate.Value;
        var nightHours = NightOverlapHours(startTime, endTime, breakMinutes, breakStartTime);
        var night = nightHours * hourlyRate.Value * NightSurchargeRate;
        var holiday = isHoliday ? regular * HolidaySurchargeRate : 0m;
        var sunday = !isHoliday && dayOfWeek == DayOfWeek.Sunday ? regular * SundaySurchargeRate : 0m;

        return new LaborCostBreakdown(regular, night, sunday, holiday);
    }

    private static decimal NightOverlapHours(TimeOnly start, TimeOnly end, int breakMinutes, TimeOnly? breakStartTime)
    {
        var startMinute = start.Hour * 60 + start.Minute;
        var endMinute = end.Hour * 60 + end.Minute;
        var shiftNightMinutes = OverlapMinutes(startMinute, endMinute, NightStartMinute, 24 * 60)
            + OverlapMinutes(startMinute, endMinute, 0, NightEndMinute);

        if (breakStartTime is null)
            return shiftNightMinutes / 60m;

        // Precise path: subtract the break's own overlap with the night window from the raw
        // shift/night-window overlap, rather than leaving it folded into shiftNightMinutes above.
        // Same same-day assumption as the shift's own StartTime/EndTime (issue #11 already
        // rejects cross-midnight shifts, so a break within one never wraps past midnight either).
        var breakStartMinute = breakStartTime.Value.Hour * 60 + breakStartTime.Value.Minute;
        var breakEndMinute = Math.Min(breakStartMinute + breakMinutes, 24 * 60);
        var breakNightMinutes = OverlapMinutes(breakStartMinute, breakEndMinute, NightStartMinute, 24 * 60)
            + OverlapMinutes(breakStartMinute, breakEndMinute, 0, NightEndMinute);

        return Math.Max(0, shiftNightMinutes - breakNightMinutes) / 60m;
    }

    private static int OverlapMinutes(int aStart, int aEnd, int bStart, int bEnd) =>
        Math.Max(0, Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart));
}

// issue #56: Regular is the unsurcharged NetHours × HourlyRate amount; Night/Sunday/Holiday are
// each surcharge's own share on top of it. Sunday and Holiday are mutually exclusive (see
// WageCalculator.Breakdown) so at most one of them is non-zero per shift.
public readonly record struct LaborCostBreakdown(decimal Regular, decimal Night, decimal Sunday, decimal Holiday)
{
    public decimal Total => Regular + Night + Sunday + Holiday;
}
