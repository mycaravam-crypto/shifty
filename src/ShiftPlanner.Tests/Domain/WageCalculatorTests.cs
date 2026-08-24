using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

public class WageCalculatorTests
{
    [Fact]
    public void LaborCost_SimpleOverload_NullRate_ReturnsNull()
    {
        Assert.Null(WageCalculator.LaborCost(8m, null));
    }

    [Fact]
    public void LaborCost_SimpleOverload_MultipliesHoursByRate()
    {
        Assert.Equal(120m, WageCalculator.LaborCost(8m, 15m));
    }

    [Fact]
    public void LaborCost_SurchargeOverload_NullRate_ReturnsNull()
    {
        var cost = WageCalculator.LaborCost(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Tuesday, isHoliday: false, netHours: 8m, hourlyRate: null);
        Assert.Null(cost);
    }

    [Fact]
    public void LaborCost_PlainWeekday_NoSurcharge()
    {
        // 08:00-16:00 Tuesday, no night/Sunday/holiday overlap.
        var cost = WageCalculator.LaborCost(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Tuesday, isHoliday: false, netHours: 8m, hourlyRate: 15m);
        Assert.Equal(120m, cost);
    }

    [Fact]
    public void LaborCost_NightOverlap_AddsPartialSurcharge()
    {
        // 18:00-22:00: 2h overlap with the 20:00-06:00 night window.
        var cost = WageCalculator.LaborCost(
            new TimeOnly(18, 0), new TimeOnly(22, 0), DayOfWeek.Wednesday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        // base: 4h * 10 = 40; night surcharge: 2h * 10 * 0.25 = 5
        Assert.Equal(45m, cost);
    }

    [Fact]
    public void LaborCost_Sunday_AddsFiftyPercent()
    {
        var cost = WageCalculator.LaborCost(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Sunday, isHoliday: false, netHours: 8m, hourlyRate: 10m);
        Assert.Equal(120m, cost); // 8 * 10 * 1.5
    }

    [Fact]
    public void LaborCost_Holiday_AddsOneHundredTwentyFivePercent()
    {
        var cost = WageCalculator.LaborCost(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Friday, isHoliday: true, netHours: 8m, hourlyRate: 10m);
        Assert.Equal(180m, cost); // 8 * 10 * 2.25
    }

    [Fact]
    public void LaborCost_HolidayOnSunday_DoesNotStack_HolidayWins()
    {
        var cost = WageCalculator.LaborCost(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Sunday, isHoliday: true, netHours: 8m, hourlyRate: 10m);
        // Holiday (125%) wins over Sunday (50%), not summed to 175%.
        Assert.Equal(180m, cost); // 8 * 10 * 2.25
    }

    [Fact]
    public void LaborCost_NightStacksAdditivelyWithSunday()
    {
        // 18:00-22:00 Sunday: base 4h + 2h night overlap.
        var cost = WageCalculator.LaborCost(
            new TimeOnly(18, 0), new TimeOnly(22, 0), DayOfWeek.Sunday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        // base: 4h * 10 * 1.5 = 60; night: 2h * 10 * 0.25 = 5
        Assert.Equal(65m, cost);
    }

    [Fact]
    public void LaborCost_NightWindowSpanningMidnight_CountsBothSides()
    {
        // 22:00-02:00 style shift can't happen (cross-midnight unsupported), but a shift ending
        // exactly at the window boundary should count the overlap on the early-morning side too.
        var cost = WageCalculator.LaborCost(
            new TimeOnly(4, 0), new TimeOnly(8, 0), DayOfWeek.Monday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        // 04:00-06:00 falls in the 00:00-06:00 night tail: 2h overlap.
        Assert.Equal(45m, cost); // base 40 + 2h*10*0.25 = 5
    }

    // issue #58: BreakStartTime is optional — when it's null (the default, and the only case
    // that existed before issue #58), the night-surcharge overload must behave EXACTLY as it did
    // before, regardless of BreakMinutes. Regression-proofs that existing/unset data's computed
    // cost doesn't change.
    [Fact]
    public void LaborCost_BreakStartTimeUnset_MatchesOldApproximation()
    {
        // Same 18:00-22:00 shift as LaborCost_NightOverlap_AddsPartialSurcharge, now also passing
        // a BreakMinutes but no BreakStartTime — the raw 2h night overlap must stay unadjusted.
        var cost = WageCalculator.LaborCost(
            new TimeOnly(18, 0), new TimeOnly(22, 0), DayOfWeek.Wednesday, isHoliday: false, netHours: 4m, hourlyRate: 10m,
            breakMinutes: 30, breakStartTime: null);
        Assert.Equal(45m, cost); // base 40 + 2h*10*0.25 = 5, identical to the no-break-args case
    }

    [Fact]
    public void LaborCost_BreakEntirelyInsideNightWindow_ReducesNightSurcharge()
    {
        // 18:00-22:00 shift, 30min break 21:00-21:30 — entirely inside the 20:00-06:00 night
        // window and entirely inside the shift's own 2h night overlap.
        var cost = WageCalculator.LaborCost(
            new TimeOnly(18, 0), new TimeOnly(22, 0), DayOfWeek.Wednesday, isHoliday: false, netHours: 3.5m, hourlyRate: 10m,
            breakMinutes: 30, breakStartTime: new TimeOnly(21, 0));
        // raw night overlap 2h, minus the 30min break entirely inside it, leaves 1.5h.
        // base: 3.5h * 10 = 35; night surcharge: 1.5h * 10 * 0.25 = 3.75
        Assert.Equal(38.75m, cost);
    }

    [Fact]
    public void LaborCost_BreakEntirelyOutsideNightWindow_NoReduction()
    {
        // Same shift, 30min break 18:00-18:30 — before the 20:00 night-window start entirely.
        var cost = WageCalculator.LaborCost(
            new TimeOnly(18, 0), new TimeOnly(22, 0), DayOfWeek.Wednesday, isHoliday: false, netHours: 3.5m, hourlyRate: 10m,
            breakMinutes: 30, breakStartTime: new TimeOnly(18, 0));
        // raw night overlap 2h stays unreduced — the break doesn't touch it.
        // base: 3.5h * 10 = 35; night surcharge: 2h * 10 * 0.25 = 5
        Assert.Equal(40m, cost);
    }

    [Fact]
    public void LaborCost_BreakStraddlesNightWindowBoundary_PartialReduction()
    {
        // Same shift, 30min break 19:45-20:15 — straddles the 20:00 night-window start, so only
        // the 19:45-20:00 slice... actually the 20:00-20:15 slice (15min) falls inside the window.
        var cost = WageCalculator.LaborCost(
            new TimeOnly(18, 0), new TimeOnly(22, 0), DayOfWeek.Wednesday, isHoliday: false, netHours: 3.5m, hourlyRate: 10m,
            breakMinutes: 30, breakStartTime: new TimeOnly(19, 45));
        // raw night overlap 2h, minus the 15min of the break that falls inside it, leaves 1h45m.
        // base: 3.5h * 10 = 35; night surcharge: 1.75h * 10 * 0.25 = 4.375
        Assert.Equal(39.375m, cost);
    }
}
