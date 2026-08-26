using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

public class WorkingTimeCalculatorTests
{
    // issue #101: IsValidShiftTiming is the central "is this shift's timing the supported
    // same-day kind" check — cross-midnight shifts (EndTime <= StartTime) are rejected at the
    // write boundary (issue #11) and out of scope for v1 (issue #81 covers real overnight
    // support). This is a direct TimeOnly comparison, so both the exact-equal-times case and a
    // genuinely-backwards EndTime < StartTime are correctly identified as invalid.
    [Fact]
    public void IsValidShiftTiming_EndAfterStart_ReturnsTrue()
    {
        Assert.True(WorkingTimeCalculator.IsValidShiftTiming(new TimeOnly(8, 0), new TimeOnly(16, 30)));
    }

    [Fact]
    public void IsValidShiftTiming_EndEqualsStart_ReturnsFalse()
    {
        Assert.False(WorkingTimeCalculator.IsValidShiftTiming(new TimeOnly(8, 0), new TimeOnly(8, 0)));
    }

    [Fact]
    public void IsValidShiftTiming_EndBeforeStart_ReturnsFalse()
    {
        Assert.False(WorkingTimeCalculator.IsValidShiftTiming(new TimeOnly(22, 0), new TimeOnly(6, 0)));
    }

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

    // issue #101: locks down NetHours' current (unchanged) behavior on the two cross-midnight
    // shapes it can be handed, since neither is guarded by IsValidShiftTiming — the write
    // boundary (SchedulesController/ShiftTypesController) is what actually prevents this data,
    // NetHours itself has no opinion.
    [Fact]
    public void NetHours_ZeroLengthShift_ClampsToZero()
    {
        // EndTime == StartTime -> gross span is exactly 0 minutes -> Math.Max(0, -breakMinutes)
        // clamps to 0. This is the one shape that actually matches the "returns 0 hours"
        // description of NetHours' cross-midnight handling.
        var hours = WorkingTimeCalculator.NetHours(new TimeOnly(8, 0), new TimeOnly(8, 0), 30);
        Assert.Equal(0m, hours);
    }

    [Fact]
    public void NetHours_GenuinelyBackwardsShift_WrapsToPositiveDuration_DoesNotReturnZero()
    {
        // TimeOnly's `-` operator (unlike its `<`/`>` comparison operators, which IsValidShiftTiming
        // uses) wraps a negative difference by adding a full day, so a genuinely-backwards
        // EndTime < StartTime does NOT clamp to 0 the way the exact-equal-times case above does
        // — it silently computes as if it were a real overnight span instead. 22:00 -> 06:00 is
        // an 8h wrapped span, minus a 30min break = 7.5h.
        var hours = WorkingTimeCalculator.NetHours(new TimeOnly(22, 0), new TimeOnly(6, 0), 30);
        Assert.Equal(7.5m, hours);
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

    // issue #102: SchedulesController.CopyMonth's day-of-month clamping (extracted here so it's
    // unit-testable) — pins down the currently-shipped clamp-and-collide behavior explicitly.
    [Fact]
    public void ClampDayToMonth_SameLengthMonth_NoClamping()
    {
        // August (31 days) -> September (30 days), a mid-month day that exists in both.
        var date = WorkingTimeCalculator.ClampDayToMonth(new DateOnly(2026, 8, 15), 2026, 9);
        Assert.Equal(new DateOnly(2026, 9, 15), date);
    }

    [Fact]
    public void ClampDayToMonth_Jan31IntoFebruary_NonLeapYear_ClampsTo28()
    {
        // 2026 is not a leap year (28-day February).
        var date = WorkingTimeCalculator.ClampDayToMonth(new DateOnly(2026, 1, 31), 2026, 2);
        Assert.Equal(new DateOnly(2026, 2, 28), date);
    }

    [Fact]
    public void ClampDayToMonth_Jan31IntoFebruary_LeapYear_ClampsTo29()
    {
        // 2028 is a leap year (29-day February).
        var date = WorkingTimeCalculator.ClampDayToMonth(new DateOnly(2028, 1, 31), 2028, 2);
        Assert.Equal(new DateOnly(2028, 2, 29), date);
    }

    [Fact]
    public void ClampDayToMonth_Jan30And31_BothCollideOnFeb28()
    {
        // Documents CopyMonth's actual, pre-existing "clamp-and-collide" behavior: two distinct
        // source dates (30th, 31st) both clamp onto the same target date once the target month
        // is shorter than both — this is intentional/accepted (see the ClampDayToMonth XML
        // comment), not a bug this issue is fixing, just a gap this issue is closing.
        var jan30 = WorkingTimeCalculator.ClampDayToMonth(new DateOnly(2026, 1, 30), 2026, 2);
        var jan31 = WorkingTimeCalculator.ClampDayToMonth(new DateOnly(2026, 1, 31), 2026, 2);

        Assert.Equal(new DateOnly(2026, 2, 28), jan30);
        Assert.Equal(new DateOnly(2026, 2, 28), jan31);
        Assert.Equal(jan30, jan31);
    }
}
