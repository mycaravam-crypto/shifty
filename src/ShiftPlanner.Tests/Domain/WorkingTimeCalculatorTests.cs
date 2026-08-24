using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

public class WorkingTimeCalculatorTests
{
    [Fact]
    public void NetHours_SubtractsBreakFromSpan()
    {
        var hours = WorkingTimeCalculator.NetHours(new TimeOnly(8, 0), new TimeOnly(16, 30), 30);
        Assert.Equal(8m, hours);
    }

    [Fact]
    public void NetHours_NeverGoesNegative()
    {
        var hours = WorkingTimeCalculator.NetHours(new TimeOnly(8, 0), new TimeOnly(8, 30), 45);
        Assert.Equal(0m, hours);
    }

    [Fact]
    public void NetHours_ZeroBreak()
    {
        var hours = WorkingTimeCalculator.NetHours(new TimeOnly(6, 0), new TimeOnly(14, 0), 0);
        Assert.Equal(8m, hours);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-01-31", "2026-01-10", "2026-01-20", 11)]
    [InlineData("2026-01-15", "2026-01-31", "2026-01-01", "2026-01-20", 6)]
    [InlineData("2026-01-01", "2026-01-05", "2026-01-10", "2026-01-20", 0)]
    [InlineData("2026-01-01", "2026-01-31", "2026-01-01", "2026-01-31", 31)]
    public void OverlapDays_ClampsToRange(string from, string to, string rangeStart, string rangeEnd, int expected)
    {
        var days = WorkingTimeCalculator.OverlapDays(
            DateOnly.Parse(from), DateOnly.Parse(to), DateOnly.Parse(rangeStart), DateOnly.Parse(rangeEnd));
        Assert.Equal(expected, days);
    }

    [Fact]
    public void OverlapDays_SingleDayOverlap_CountsAsOne()
    {
        var days = WorkingTimeCalculator.OverlapDays(
            new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Assert.Equal(1, days);
    }
}
