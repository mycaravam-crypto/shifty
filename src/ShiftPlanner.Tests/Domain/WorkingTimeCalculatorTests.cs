using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
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

    // issue #56: ExpectedHours is the formula ContractValidator, HoursBalanceCalculator, and
    // now DashboardController's per-employee utilization all share — extracted here once a 3rd/
    // 4th caller needed it, same reasoning OverlapDays itself was extracted for.
    [Fact]
    public void ExpectedHours_NullContract_ReturnsZero()
    {
        var hours = WorkingTimeCalculator.ExpectedHours(
            null, [], Guid.NewGuid(), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        Assert.Equal(0m, hours);
    }

    [Fact]
    public void ExpectedHours_ScalesWeeklyHoursBySpan()
    {
        var employeeId = Guid.NewGuid();
        var contract = new Contract
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, ValidFrom = new DateOnly(2026, 1, 1),
            WeeklyHours = 40m, WorkingDaysPerWeek = 5, DailyTargetHours = 8m,
        };

        // 31-day span (a month) -> 40 * 31/7 ~= 177.14h, not the raw 40h a naive 7-day
        // assumption would give.
        var hours = WorkingTimeCalculator.ExpectedHours(
            contract, [], employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        Assert.Equal(Math.Round(40m * 31 / 7, 2), Math.Round(hours, 2));
    }

    [Fact]
    public void ExpectedHours_ExcludesAbsenceDaysOverlappingRange()
    {
        var employeeId = Guid.NewGuid();
        var contract = new Contract
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, ValidFrom = new DateOnly(2026, 1, 1),
            WeeklyHours = 35m, WorkingDaysPerWeek = 5, DailyTargetHours = 7m,
        };
        // 7-day range, absent for 6 of the 7 days -> 1 effective day -> 35/7 = 5h expected.
        var absences = new[]
        {
            new Absence { Id = Guid.NewGuid(), EmployeeId = employeeId, From = new DateOnly(2026, 8, 1), To = new DateOnly(2026, 8, 6), Type = AbsenceType.Vacation },
        };

        var hours = WorkingTimeCalculator.ExpectedHours(
            contract, absences, employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        Assert.Equal(5m, hours);
    }

    [Fact]
    public void ExpectedHours_AbsenceForDifferentEmployee_Ignored()
    {
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var contract = new Contract
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, ValidFrom = new DateOnly(2026, 1, 1),
            WeeklyHours = 7m, WorkingDaysPerWeek = 5, DailyTargetHours = 1.4m,
        };
        var absences = new[]
        {
            new Absence { Id = Guid.NewGuid(), EmployeeId = otherEmployeeId, From = new DateOnly(2026, 8, 1), To = new DateOnly(2026, 8, 7), Type = AbsenceType.Vacation },
        };

        var hours = WorkingTimeCalculator.ExpectedHours(
            contract, absences, employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        Assert.Equal(7m, hours);
    }
}
