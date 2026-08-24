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
}
