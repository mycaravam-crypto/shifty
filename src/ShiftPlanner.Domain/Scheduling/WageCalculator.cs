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
        bool isHoliday, decimal netHours, decimal? hourlyRate, int breakMinutes = 0, TimeOnly? breakStartTime = null)
    {
        if (hourlyRate is null)
            return null;

        var wholeShiftRate = isHoliday ? HolidaySurchargeRate
            : dayOfWeek == DayOfWeek.Sunday ? SundaySurchargeRate
            : 0m;
        var nightHours = NightOverlapHours(startTime, endTime, breakMinutes, breakStartTime);

        return netHours * hourlyRate.Value * (1 + wholeShiftRate) + nightHours * hourlyRate.Value * NightSurchargeRate;
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
