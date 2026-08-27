namespace ShiftPlanner.Domain.Scheduling;

// issue #100: bundles the timing-related parameters WageCalculator's surcharge methods need
// (start/end/break) so they can't be silently transposed against each other or against the
// decimal netHours/hourlyRate parameters at a call site — a swapped positional TimeOnly/TimeOnly
// or decimal/decimal pair used to compile fine and silently miscalculate wages.
// issue #157: EndsNextDay defaults to false so every pre-existing call site (built before real
// cross-midnight support existed) keeps compiling and behaving exactly as before.
public record ShiftTiming(TimeOnly Start, TimeOnly End, int BreakMinutes, TimeOnly? BreakStart, bool EndsNextDay = false);

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

    // issue #100: renamed from the overloaded "LaborCost" (plain multiplication, no surcharges)
    // to make it unambiguous at every call site which of the two very different calculations is
    // being invoked.
    public static decimal? SimpleLaborCost(decimal netHours, decimal? hourlyRate) =>
        hourlyRate is null ? null : netHours * hourlyRate.Value;

    // Adds night/Sunday/holiday surcharges on top of the base rate. Sunday and holiday don't
    // stack (holiday wins when a holiday falls on a Sunday) but night stacks with either, matching
    // common German practice. Night hours are the raw StartTime–EndTime overlap with 20:00–06:00.
    // issue #58: when BreakStart is known, the break's own overlap with the night window is
    // subtracted from that raw figure for a precise count; when it's null (unknown/unspecified
    // break timing — the only case before issue #58 existed), this keeps the original
    // approximation exactly as before, so existing data's computed cost doesn't change.
    // issue #100: renamed from the overloaded "LaborCost" (full surcharge calculation) to make it
    // unambiguous at every call site, and timing takes one ShiftTiming instead of four positional
    // parameters (two TimeOnly, one int, one TimeOnly?) that could otherwise be transposed.
    public static decimal? LaborCostWithSurcharges(ShiftTiming timing, DayOfWeek dayOfWeek,
        bool isHoliday, decimal netHours, decimal? hourlyRate) =>
        Breakdown(timing, dayOfWeek, isHoliday, netHours, hourlyRate)?.Total;

    // issue #56: the per-surcharge-type split behind the total LaborCostWithSurcharges above —
    // the dashboard's cost-breakdown KPI (regular/night/Sunday/holiday) reuses this instead of
    // re-deriving the surcharge math a second time. Regular + Night + Sunday + Holiday always
    // sums to exactly what LaborCostWithSurcharges above returns (Sunday/Holiday are mutually
    // exclusive per the same "holiday wins" rule, so at most one of them is non-zero for a given
    // shift).
    public static LaborCostBreakdown? Breakdown(ShiftTiming timing, DayOfWeek dayOfWeek,
        bool isHoliday, decimal netHours, decimal? hourlyRate)
    {
        if (hourlyRate is null)
            return null;

        var regular = netHours * hourlyRate.Value;
        var nightHours = NightOverlapHours(timing.Start, timing.End, timing.BreakMinutes, timing.BreakStart, timing.EndsNextDay);
        var night = nightHours * hourlyRate.Value * NightSurchargeRate;
        var holiday = isHoliday ? regular * HolidaySurchargeRate : 0m;
        var sunday = !isHoliday && dayOfWeek == DayOfWeek.Sunday ? regular * SundaySurchargeRate : 0m;

        return new LaborCostBreakdown(regular, night, sunday, holiday);
    }

    // issue #157: endsNextDay puts EndTime (and possibly BreakStartTime) on the calendar day
    // after Start — NightMinutesIn below already generalizes to a >24h absolute range, so this
    // just needs to resolve every clock time onto the right side of that range first.
    private static decimal NightOverlapHours(TimeOnly start, TimeOnly end, int breakMinutes, TimeOnly? breakStartTime, bool endsNextDay)
    {
        var startMinute = start.Hour * 60 + start.Minute;
        var endMinute = ResolveMinute(end, startMinute, endsNextDay);
        var shiftNightMinutes = NightMinutesIn(startMinute, endMinute);

        if (breakStartTime is null)
            return shiftNightMinutes / 60m;

        // Precise path: subtract the break's own overlap with the night window from the raw
        // shift/night-window overlap, rather than leaving it folded into shiftNightMinutes above.
        // issue #157: BreakStartTime carries no day of its own — a clock value at or before
        // StartTime is assumed to fall on the day the shift ENDS (e.g. a 22:00->06:00 shift with
        // BreakStartTime 01:00 means 01:00 the next morning, not 01:00 before the shift even
        // starts), same resolution ResolveMinute already applies to EndTime.
        var breakStartMinute = ResolveMinute(breakStartTime.Value, startMinute, endsNextDay);
        var breakEndMinute = Math.Min(breakStartMinute + breakMinutes, endsNextDay ? endMinute : 24 * 60);
        var breakNightMinutes = NightMinutesIn(breakStartMinute, breakEndMinute);

        return Math.Max(0, shiftNightMinutes - breakNightMinutes) / 60m;
    }

    // A clock time <= the shift's own start minute is assumed to fall on the following calendar
    // day when the shift crosses midnight (true for EndTime by construction — IsValidShiftTiming
    // requires End < Start whenever EndsNextDay — and applied the same way to BreakStartTime).
    private static int ResolveMinute(TimeOnly time, int shiftStartMinute, bool endsNextDay)
    {
        var minute = time.Hour * 60 + time.Minute;
        return endsNextDay && minute <= shiftStartMinute ? minute + 24 * 60 : minute;
    }

    // The 20:00-06:00 night window recurs every calendar day; a shift/break can span at most one
    // midnight (endsNextDay is exactly one crossing, never more), so its absolute minute range
    // never reaches past the second day's own night window — these three fixed intervals cover
    // every case: today's early morning, the (contiguous, since 24:00 day N = 00:00 day N+1)
    // 20:00-day-N -> 06:00-day-N+1 block, and — only reachable by a shift starting very late and
    // running nearly a full 24h — day N+1's own evening. Reduces to the exact pre-#157 two-term
    // formula whenever endMinute stays under 24*60 (every same-day, non-crossing shift).
    private static int NightMinutesIn(int startMinute, int endMinute) =>
        OverlapMinutes(startMinute, endMinute, 0, NightEndMinute)
        + OverlapMinutes(startMinute, endMinute, NightStartMinute, 24 * 60 + NightEndMinute)
        + OverlapMinutes(startMinute, endMinute, 24 * 60 + NightStartMinute, 48 * 60);

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
