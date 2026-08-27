using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

public class WageCalculatorTests
{
    [Fact]
    public void SimpleLaborCost_NullRate_ReturnsNull()
    {
        Assert.Null(WageCalculator.SimpleLaborCost(8m, null));
    }

    [Fact]
    public void SimpleLaborCost_MultipliesHoursByRate()
    {
        Assert.Equal(120m, WageCalculator.SimpleLaborCost(8m, 15m));
    }

    [Fact]
    public void LaborCostWithSurcharges_NullRate_ReturnsNull()
    {
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Tuesday, isHoliday: false, netHours: 8m, hourlyRate: null);
        Assert.Null(cost);
    }

    [Fact]
    public void LaborCostWithSurcharges_PlainWeekday_NoSurcharge()
    {
        // 08:00-16:00 Tuesday, no night/Sunday/holiday overlap.
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Tuesday, isHoliday: false, netHours: 8m, hourlyRate: 15m);
        Assert.Equal(120m, cost);
    }

    [Fact]
    public void LaborCostWithSurcharges_NightOverlap_AddsPartialSurcharge()
    {
        // 18:00-22:00: 2h overlap with the 20:00-06:00 night window.
        var timing = new ShiftTiming(new TimeOnly(18, 0), new TimeOnly(22, 0), 0, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        // base: 4h * 10 = 40; night surcharge: 2h * 10 * 0.25 = 5
        Assert.Equal(45m, cost);
    }

    [Fact]
    public void LaborCostWithSurcharges_Sunday_AddsFiftyPercent()
    {
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Sunday, isHoliday: false, netHours: 8m, hourlyRate: 10m);
        Assert.Equal(120m, cost); // 8 * 10 * 1.5
    }

    [Fact]
    public void LaborCostWithSurcharges_Holiday_AddsOneHundredTwentyFivePercent()
    {
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Friday, isHoliday: true, netHours: 8m, hourlyRate: 10m);
        Assert.Equal(180m, cost); // 8 * 10 * 2.25
    }

    [Fact]
    public void LaborCostWithSurcharges_HolidayOnSunday_DoesNotStack_HolidayWins()
    {
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Sunday, isHoliday: true, netHours: 8m, hourlyRate: 10m);
        // Holiday (125%) wins over Sunday (50%), not summed to 175%.
        Assert.Equal(180m, cost); // 8 * 10 * 2.25
    }

    [Fact]
    public void LaborCostWithSurcharges_NightStacksAdditivelyWithSunday()
    {
        // 18:00-22:00 Sunday: base 4h + 2h night overlap.
        var timing = new ShiftTiming(new TimeOnly(18, 0), new TimeOnly(22, 0), 0, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Sunday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        // base: 4h * 10 * 1.5 = 60; night: 2h * 10 * 0.25 = 5
        Assert.Equal(65m, cost);
    }

    [Fact]
    public void LaborCostWithSurcharges_NightWindowSpanningMidnight_CountsBothSides()
    {
        // 22:00-02:00 style shift can't happen (cross-midnight unsupported), but a shift ending
        // exactly at the window boundary should count the overlap on the early-morning side too.
        var timing = new ShiftTiming(new TimeOnly(4, 0), new TimeOnly(8, 0), 0, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Monday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        // 04:00-06:00 falls in the 00:00-06:00 night tail: 2h overlap.
        Assert.Equal(45m, cost); // base 40 + 2h*10*0.25 = 5
    }

    // issue #56: Breakdown backs the dashboard's cost-breakdown KPI — Regular/Night/Sunday/
    // Holiday should always sum to exactly what LaborCostWithSurcharges returns.
    [Fact]
    public void Breakdown_NullRate_ReturnsNull()
    {
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var breakdown = WageCalculator.Breakdown(
            timing, DayOfWeek.Tuesday, isHoliday: false, netHours: 8m, hourlyRate: null);
        Assert.Null(breakdown);
    }

    [Fact]
    public void Breakdown_PlainWeekday_OnlyRegular()
    {
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var breakdown = WageCalculator.Breakdown(
            timing, DayOfWeek.Tuesday, isHoliday: false, netHours: 8m, hourlyRate: 15m);
        Assert.NotNull(breakdown);
        Assert.Equal(120m, breakdown.Value.Regular);
        Assert.Equal(0m, breakdown.Value.Night);
        Assert.Equal(0m, breakdown.Value.Sunday);
        Assert.Equal(0m, breakdown.Value.Holiday);
        Assert.Equal(120m, breakdown.Value.Total);
    }

    [Fact]
    public void Breakdown_NightOverlap_SplitOutSeparately()
    {
        var timing = new ShiftTiming(new TimeOnly(18, 0), new TimeOnly(22, 0), 0, null);
        var breakdown = WageCalculator.Breakdown(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        Assert.NotNull(breakdown);
        Assert.Equal(40m, breakdown.Value.Regular);
        Assert.Equal(5m, breakdown.Value.Night); // 2h * 10 * 0.25
        Assert.Equal(0m, breakdown.Value.Sunday);
        Assert.Equal(0m, breakdown.Value.Holiday);
    }

    [Fact]
    public void Breakdown_Sunday_SplitOutSeparately()
    {
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var breakdown = WageCalculator.Breakdown(
            timing, DayOfWeek.Sunday, isHoliday: false, netHours: 8m, hourlyRate: 10m);
        Assert.NotNull(breakdown);
        Assert.Equal(80m, breakdown.Value.Regular);
        Assert.Equal(0m, breakdown.Value.Night);
        Assert.Equal(40m, breakdown.Value.Sunday); // 8 * 10 * 0.5
        Assert.Equal(0m, breakdown.Value.Holiday);
    }

    [Fact]
    public void Breakdown_Holiday_SplitOutSeparately_HolidayWinsOverSunday()
    {
        var timing = new ShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 0), 0, null);
        var breakdown = WageCalculator.Breakdown(
            timing, DayOfWeek.Sunday, isHoliday: true, netHours: 8m, hourlyRate: 10m);
        Assert.NotNull(breakdown);
        Assert.Equal(80m, breakdown.Value.Regular);
        Assert.Equal(0m, breakdown.Value.Night);
        Assert.Equal(0m, breakdown.Value.Sunday); // holiday wins, no double-charge
        Assert.Equal(100m, breakdown.Value.Holiday); // 8 * 10 * 1.25
    }

    [Theory]
    [InlineData(8, 0, 16, 0, DayOfWeek.Tuesday, false, 8, 15)]
    [InlineData(18, 0, 22, 0, DayOfWeek.Wednesday, false, 4, 10)]
    [InlineData(8, 0, 16, 0, DayOfWeek.Sunday, false, 8, 10)]
    [InlineData(8, 0, 16, 0, DayOfWeek.Friday, true, 8, 10)]
    [InlineData(8, 0, 16, 0, DayOfWeek.Sunday, true, 8, 10)]
    [InlineData(18, 0, 22, 0, DayOfWeek.Sunday, false, 4, 10)]
    public void Breakdown_TotalAlwaysMatchesLaborCost(
        int startHour, int startMinute, int endHour, int endMinute, DayOfWeek dayOfWeek, bool isHoliday, decimal netHours, decimal hourlyRate)
    {
        var start = new TimeOnly(startHour, startMinute);
        var end = new TimeOnly(endHour, endMinute);
        var timing = new ShiftTiming(start, end, 0, null);
        var breakdown = WageCalculator.Breakdown(timing, dayOfWeek, isHoliday, netHours, hourlyRate);
        var cost = WageCalculator.LaborCostWithSurcharges(timing, dayOfWeek, isHoliday, netHours, hourlyRate);

        Assert.NotNull(breakdown);
        Assert.Equal(cost, breakdown.Value.Total);
    }

    // issue #58: BreakStart is optional — when it's null (the default, and the only case
    // that existed before issue #58), the night-surcharge overload must behave EXACTLY as it did
    // before, regardless of BreakMinutes. Regression-proofs that existing/unset data's computed
    // cost doesn't change.
    [Fact]
    public void LaborCostWithSurcharges_BreakStartTimeUnset_MatchesOldApproximation()
    {
        // Same 18:00-22:00 shift as LaborCostWithSurcharges_NightOverlap_AddsPartialSurcharge,
        // now also passing a BreakMinutes but no BreakStart — the raw 2h night overlap must
        // stay unadjusted.
        var timing = new ShiftTiming(new TimeOnly(18, 0), new TimeOnly(22, 0), 30, null);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        Assert.Equal(45m, cost); // base 40 + 2h*10*0.25 = 5, identical to the no-break-args case
    }

    [Fact]
    public void LaborCostWithSurcharges_BreakEntirelyInsideNightWindow_ReducesNightSurcharge()
    {
        // 18:00-22:00 shift, 30min break 21:00-21:30 — entirely inside the 20:00-06:00 night
        // window and entirely inside the shift's own 2h night overlap.
        var timing = new ShiftTiming(new TimeOnly(18, 0), new TimeOnly(22, 0), 30, new TimeOnly(21, 0));
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 3.5m, hourlyRate: 10m);
        // raw night overlap 2h, minus the 30min break entirely inside it, leaves 1.5h.
        // base: 3.5h * 10 = 35; night surcharge: 1.5h * 10 * 0.25 = 3.75
        Assert.Equal(38.75m, cost);
    }

    [Fact]
    public void LaborCostWithSurcharges_BreakEntirelyOutsideNightWindow_NoReduction()
    {
        // Same shift, 30min break 18:00-18:30 — before the 20:00 night-window start entirely.
        var timing = new ShiftTiming(new TimeOnly(18, 0), new TimeOnly(22, 0), 30, new TimeOnly(18, 0));
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 3.5m, hourlyRate: 10m);
        // raw night overlap 2h stays unreduced — the break doesn't touch it.
        // base: 3.5h * 10 = 35; night surcharge: 2h * 10 * 0.25 = 5
        Assert.Equal(40m, cost);
    }

    [Fact]
    public void LaborCostWithSurcharges_BreakStraddlesNightWindowBoundary_PartialReduction()
    {
        // Same shift, 30min break 19:45-20:15 — straddles the 20:00 night-window start, so only
        // the 19:45-20:00 slice... actually the 20:00-20:15 slice (15min) falls inside the window.
        var timing = new ShiftTiming(new TimeOnly(18, 0), new TimeOnly(22, 0), 30, new TimeOnly(19, 45));
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 3.5m, hourlyRate: 10m);
        // raw night overlap 2h, minus the 15min of the break that falls inside it, leaves 1h45m.
        // base: 3.5h * 10 = 35; night surcharge: 1.75h * 10 * 0.25 = 4.375
        Assert.Equal(39.375m, cost);
    }

    [Fact]
    public void LaborCost_BreakSpanningPastMidnight_ClampsToEndOfDay()
    {
        // 20:00-23:30 shift, entirely within the night window (3.5h raw night overlap). A 90min
        // break starting at 23:00 would compute an unclamped end of 24:30 (breakStartMinute +
        // breakMinutes = 1470 > 24*60) — NightOverlapHours clamps that to 24:00 (1440) before
        // computing the break's own night-window overlap.
        var timing = new ShiftTiming(new TimeOnly(20, 0), new TimeOnly(23, 30), 90, new TimeOnly(23, 0));
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Thursday, isHoliday: false, netHours: 2m, hourlyRate: 10m);
        // raw shift night overlap: 3.5h. Break's own night overlap (23:00 up to the clamped
        // 24:00) is 1h, leaving 2.5h net night overlap.
        // base: 2h * 10 = 20; night surcharge: 2.5h * 10 * 0.25 = 6.25
        Assert.Equal(26.25m, cost);
    }

    // issue #157: EndsNextDay shifts. Before this, a 22:00->06:00 shift computed ZERO night hours
    // (the pre-#157 formula had no way to know EndTime belonged to the following day, so its raw
    // minute-of-day comparison against the night window silently came up empty) — these lock down
    // the corrected behavior.
    [Fact]
    public void LaborCostWithSurcharges_EndsNextDay_FullyWithinNightWindow()
    {
        // 22:00 -> 06:00 next day: entirely inside 20:00-06:00, so all 8h (minus break) is night.
        var timing = new ShiftTiming(new TimeOnly(22, 0), new TimeOnly(6, 0), 0, null, EndsNextDay: true);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 8m, hourlyRate: 10m);
        // base: 8h * 10 = 80; night surcharge: 8h * 10 * 0.25 = 20
        Assert.Equal(100m, cost);
    }

    [Fact]
    public void LaborCostWithSurcharges_EndsNextDay_PartialNightOverlapOnBothSides()
    {
        // 18:00 -> 08:00 next day: 2h (18:00-20:00, no) wait — night window is 20:00-06:00, so
        // 18:00-20:00 is NOT night (2h), 20:00-06:00 IS night (10h), 06:00-08:00 is NOT night (2h).
        var timing = new ShiftTiming(new TimeOnly(18, 0), new TimeOnly(8, 0), 0, null, EndsNextDay: true);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 14m, hourlyRate: 10m);
        // base: 14h * 10 = 140; night surcharge: 10h * 10 * 0.25 = 25
        Assert.Equal(165m, cost);
    }

    [Fact]
    public void LaborCostWithSurcharges_EndsNextDay_BreakOnFollowingDayReducesNightOverlap()
    {
        // 22:00 -> 06:00 next day, 60min break starting at 01:00 (next day, since 01:00 <= the
        // shift's own start minute of 22:00 puts it on the following calendar day).
        var timing = new ShiftTiming(new TimeOnly(22, 0), new TimeOnly(6, 0), 60, new TimeOnly(1, 0), EndsNextDay: true);
        var cost = WageCalculator.LaborCostWithSurcharges(
            timing, DayOfWeek.Wednesday, isHoliday: false, netHours: 7m, hourlyRate: 10m);
        // raw night overlap 8h, minus the 1h break (entirely inside the night window) = 7h.
        // base: 7h * 10 = 70; night surcharge: 7h * 10 * 0.25 = 17.5
        Assert.Equal(87.5m, cost);
    }
}
