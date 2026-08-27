using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

public class WorkingTimeCalculatorTests
{
    // issue #101/#157: IsValidShiftTiming is the central "is this shift's timing structurally
    // valid" check — same-day (EndTime > StartTime) by default, or exactly one midnight crossing
    // (EndTime strictly before StartTime) when endsNextDay is set. These same-day-only cases
    // (endsNextDay left at its default false) are unaffected by #157 — see the endsNextDay=true
    // cases further down for the crossing behavior.
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

    [Fact]
    public void NetHours_ZeroLengthShift_ClampsToZero()
    {
        // EndTime == StartTime -> gross span is exactly 0 minutes -> Math.Max(0, -breakMinutes)
        // clamps to 0.
        var hours = WorkingTimeCalculator.NetHours(new TimeOnly(8, 0), new TimeOnly(8, 0), 30);
        Assert.Equal(0m, hours);
    }

    // issue #157: NetHours now takes an explicit `endsNextDay` and computes via absolute minutes
    // instead of TimeOnly's `-` operator wraparound — a genuinely-backwards EndTime < StartTime
    // with `endsNextDay` left false (its default) now clamps to 0 like any other invalid-looking
    // span, rather than silently wrapping as if it were a real overnight shift. This replaces the
    // old NetHours_GenuinelyBackwardsShift_WrapsToPositiveDuration_DoesNotReturnZero test, which
    // pinned down exactly the wraparound behavior issue #157 deliberately removed.
    [Fact]
    public void NetHours_BackwardsShift_EndsNextDayFalse_ClampsToZero()
    {
        var hours = WorkingTimeCalculator.NetHours(new TimeOnly(22, 0), new TimeOnly(6, 0), 30);
        Assert.Equal(0m, hours);
    }

    [Fact]
    public void NetHours_EndsNextDayTrue_ComputesOvernightSpan()
    {
        // 22:00 -> 06:00 the next day is an 8h span, minus a 30min break = 7.5h.
        var hours = WorkingTimeCalculator.NetHours(new TimeOnly(22, 0), new TimeOnly(6, 0), 30, endsNextDay: true);
        Assert.Equal(7.5m, hours);
    }

    [Fact]
    public void IsValidShiftTiming_EndsNextDayTrue_EndBeforeStart_ReturnsTrue()
    {
        Assert.True(WorkingTimeCalculator.IsValidShiftTiming(new TimeOnly(22, 0), new TimeOnly(6, 0), endsNextDay: true));
    }

    [Fact]
    public void IsValidShiftTiming_EndsNextDayTrue_EndAfterStart_ReturnsFalse()
    {
        // Would be a >24h span — endsNextDay means exactly one midnight crossing, never more.
        Assert.False(WorkingTimeCalculator.IsValidShiftTiming(new TimeOnly(8, 0), new TimeOnly(20, 0), endsNextDay: true));
    }

    [Fact]
    public void IsValidShiftTiming_EndsNextDayTrue_EndEqualsStart_ReturnsFalse()
    {
        Assert.False(WorkingTimeCalculator.IsValidShiftTiming(new TimeOnly(22, 0), new TimeOnly(22, 0), endsNextDay: true));
    }

    [Theory]
    [InlineData("2026-08-29", false, false)] // Saturday, no crossing
    [InlineData("2026-08-30", false, true)] // Sunday itself
    [InlineData("2026-08-29", true, true)] // Saturday shift crossing into Sunday
    [InlineData("2026-08-30", true, true)] // Sunday shift crossing into Monday — start day alone already qualifies
    [InlineData("2026-08-31", true, false)] // Monday shift crossing into Tuesday — neither day is Sunday
    public void TouchesSunday_ChecksBothStartAndNextDayWhenCrossing(string date, bool endsNextDay, bool expected)
    {
        Assert.Equal(expected, WorkingTimeCalculator.TouchesSunday(DateOnly.Parse(date), endsNextDay));
    }

    [Fact]
    public void TouchesHoliday_EndsNextDayTrue_ChecksFollowingDayToo()
    {
        var holidays = new HashSet<DateOnly> { new(2026, 12, 25) };
        Assert.True(WorkingTimeCalculator.TouchesHoliday(new DateOnly(2026, 12, 24), endsNextDay: true, holidays));
        Assert.False(WorkingTimeCalculator.TouchesHoliday(new DateOnly(2026, 12, 24), endsNextDay: false, holidays));
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
    //
    // issue #70: signature changed from a single already-resolved Contract to the full
    // contracts list — ExpectedHours now resolves the applicable one PER DAY internally via
    // Contract.ActiveOn, fixing the mid-schedule contract-change bug (see the dedicated tests
    // below) rather than scaling the whole span by whichever contract was active on day 1.
    [Fact]
    public void ExpectedHours_NoContractForEmployee_ReturnsZero()
    {
        var hours = WorkingTimeCalculator.ExpectedHours(
            [], [], Guid.NewGuid(), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
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

        // 31-day span (a month) -> 40 * 31/7 = 177.14h (pinned literal, not re-derived via
        // production's own formula), not the raw 40h a naive 7-day assumption would give.
        var hours = WorkingTimeCalculator.ExpectedHours(
            [contract], [], employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
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
            [contract], absences, employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
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
            [contract], absences, employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        Assert.Equal(7m, hours);
    }

    // issue #70: the core bug fix — a Contract that changes mid-span must blend both segments'
    // WeeklyHours, not scale the whole span by whichever contract was active on day 1.
    [Fact]
    public void ExpectedHours_ContractChangesMidSpan_BlendsBothSegments()
    {
        var employeeId = Guid.NewGuid();
        // 20h/week through Aug 15, then 40h/week from Aug 16 -- a 31-day August schedule spans
        // both. Old (buggy) behavior: resolves the contract active on Aug 1 (20h/week) and
        // scales that across all 31 days -> 20*31/7 ~= 88.57h, ignoring the raise entirely.
        var earlier = new Contract
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 8, 15), WeeklyHours = 20m, WorkingDaysPerWeek = 3, DailyTargetHours = 6.67m,
        };
        var later = new Contract
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, ValidFrom = new DateOnly(2026, 8, 16),
            WeeklyHours = 40m, WorkingDaysPerWeek = 5, DailyTargetHours = 8m,
        };

        var hours = WorkingTimeCalculator.ExpectedHours(
            [earlier, later], [], employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        // 15 days at 20h/week + 16 days at 40h/week.
        var expected = (20m * 15 / 7m) + (40m * 16 / 7m);
        Assert.Equal(Math.Round(expected, 4), Math.Round(hours, 4));
        // Sanity check this actually differs from the old single-contract-at-start behavior.
        Assert.NotEqual(Math.Round(20m * 31 / 7m, 4), Math.Round(hours, 4));
    }

    [Fact]
    public void ExpectedHours_GapBetweenContracts_ContributesZeroForUncoveredDays()
    {
        var employeeId = Guid.NewGuid();
        // A 3-day gap (Aug 10-12) between two contracts should contribute 0 expected hours for
        // those days, not silently fall back to either neighboring contract.
        var earlier = new Contract
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 8, 9), WeeklyHours = 21m, WorkingDaysPerWeek = 3, DailyTargetHours = 7m,
        };
        var later = new Contract
        {
            Id = Guid.NewGuid(), EmployeeId = employeeId, ValidFrom = new DateOnly(2026, 8, 13),
            WeeklyHours = 35m, WorkingDaysPerWeek = 5, DailyTargetHours = 7m,
        };

        var hours = WorkingTimeCalculator.ExpectedHours(
            [earlier, later], [], employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 20));

        // Aug 1-9 (9 days) at 21h/week + Aug 13-20 (8 days) at 35h/week; Aug 10-12 contribute 0.
        var expected = (21m * 9 / 7m) + (35m * 8 / 7m);
        Assert.Equal(Math.Round(expected, 4), Math.Round(hours, 4));
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
