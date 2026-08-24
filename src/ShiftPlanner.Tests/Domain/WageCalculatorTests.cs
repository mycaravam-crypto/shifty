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

    // issue #56: Breakdown backs the dashboard's cost-breakdown KPI — Regular/Night/Sunday/
    // Holiday should always sum to exactly what the LaborCost overload returns.
    [Fact]
    public void Breakdown_NullRate_ReturnsNull()
    {
        var breakdown = WageCalculator.Breakdown(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Tuesday, isHoliday: false, netHours: 8m, hourlyRate: null);
        Assert.Null(breakdown);
    }

    [Fact]
    public void Breakdown_PlainWeekday_OnlyRegular()
    {
        var breakdown = WageCalculator.Breakdown(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Tuesday, isHoliday: false, netHours: 8m, hourlyRate: 15m);
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
        var breakdown = WageCalculator.Breakdown(
            new TimeOnly(18, 0), new TimeOnly(22, 0), DayOfWeek.Wednesday, isHoliday: false, netHours: 4m, hourlyRate: 10m);
        Assert.NotNull(breakdown);
        Assert.Equal(40m, breakdown.Value.Regular);
        Assert.Equal(5m, breakdown.Value.Night); // 2h * 10 * 0.25
        Assert.Equal(0m, breakdown.Value.Sunday);
        Assert.Equal(0m, breakdown.Value.Holiday);
    }

    [Fact]
    public void Breakdown_Sunday_SplitOutSeparately()
    {
        var breakdown = WageCalculator.Breakdown(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Sunday, isHoliday: false, netHours: 8m, hourlyRate: 10m);
        Assert.NotNull(breakdown);
        Assert.Equal(80m, breakdown.Value.Regular);
        Assert.Equal(0m, breakdown.Value.Night);
        Assert.Equal(40m, breakdown.Value.Sunday); // 8 * 10 * 0.5
        Assert.Equal(0m, breakdown.Value.Holiday);
    }

    [Fact]
    public void Breakdown_Holiday_SplitOutSeparately_HolidayWinsOverSunday()
    {
        var breakdown = WageCalculator.Breakdown(
            new TimeOnly(8, 0), new TimeOnly(16, 0), DayOfWeek.Sunday, isHoliday: true, netHours: 8m, hourlyRate: 10m);
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
        var breakdown = WageCalculator.Breakdown(start, end, dayOfWeek, isHoliday, netHours, hourlyRate);
        var cost = WageCalculator.LaborCost(start, end, dayOfWeek, isHoliday, netHours, hourlyRate);

        Assert.NotNull(breakdown);
        Assert.Equal(cost, breakdown.Value.Total);
    }
}
