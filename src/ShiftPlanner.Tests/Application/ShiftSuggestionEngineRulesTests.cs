using ShiftPlanner.Application.Suggestions;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

// issue #114/#115: unit tests against ShiftSuggestionEngine's extracted per-rule internal
// methods directly (InternalsVisibleTo grants this project access), independent of the
// end-to-end ShiftSuggestionEngineTests above which exercise the same rules only through the
// public Suggest(...) entry point.
public class ShiftSuggestionEngineRulesTests
{
    private static readonly DateOnly ScheduleStart = new(2026, 8, 1);
    private static readonly DateOnly ScheduleEnd = new(2026, 8, 31);

    [Fact]
    public void EvaluateEligibility_NoRestriction_IsEligible()
    {
        var employee = Employee();
        var shiftType = ShiftType();

        var outcome = ShiftSuggestionEngine.EvaluateEligibility(employee, shiftType);

        Assert.True(outcome.Eligible);
        Assert.Equal(0m, outcome.ScoreDelta);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void EvaluateEligibility_RestrictedToOtherShiftType_IsIneligible()
    {
        var otherShiftType = ShiftType();
        var employee = Employee();
        employee.AddEligibleShiftType(otherShiftType);
        var shiftType = ShiftType();

        var outcome = ShiftSuggestionEngine.EvaluateEligibility(employee, shiftType);

        Assert.False(outcome.Eligible);
        Assert.Equal(SuggestionReasonCode.NotEligible, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateAbsence_OnDate_IsIneligible()
    {
        var employee = Employee();
        var date = new DateOnly(2026, 8, 10);
        var absences = new[] { Absence(employee.Id, new DateOnly(2026, 8, 9), new DateOnly(2026, 8, 11)) };

        var outcome = ShiftSuggestionEngine.EvaluateAbsence(employee, date, absences);

        Assert.False(outcome.Eligible);
        Assert.Equal(SuggestionReasonCode.Absent, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateAbsence_OutsideRange_IsEligible()
    {
        var employee = Employee();
        var date = new DateOnly(2026, 8, 20);
        var absences = new[] { Absence(employee.Id, new DateOnly(2026, 8, 9), new DateOnly(2026, 8, 11)) };

        var outcome = ShiftSuggestionEngine.EvaluateAbsence(employee, date, absences);

        Assert.True(outcome.Eligible);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void EvaluateRestTime_LessThan11HoursBeforeHypotheticalShift_IsIneligible()
    {
        var employeeId = Guid.NewGuid();
        var shiftType = ShiftType(new TimeOnly(6, 0), new TimeOnly(14, 0));
        var history = new[]
        {
            Assignment(employeeId, shiftType.Id, new DateOnly(2026, 8, 9), new TimeOnly(14, 0), new TimeOnly(22, 0)),
        };
        var hypotheticalStart = new DateOnly(2026, 8, 10).ToDateTime(shiftType.StartTime);
        var hypotheticalEnd = new DateOnly(2026, 8, 10).ToDateTime(shiftType.EndTime);

        var outcome = ShiftSuggestionEngine.EvaluateRestTime(history, hypotheticalStart, hypotheticalEnd);

        Assert.False(outcome.Eligible);
        Assert.Equal(SuggestionReasonCode.InsufficientRest, outcome.Reason!.Code);
    }

    // issue #157: the history shift's real end instant is Date+EndTime pushed a day later when
    // EndsNextDay is set — before this, its EndTime (06:00) would sort as if it ended the SAME
    // morning it started, making the hypothetical 08:00 shift look like it had 2h of rest instead
    // of correctly appearing to have none at all (the overnight shift would still be in progress).
    [Fact]
    public void EvaluateRestTime_EndsNextDayHistoryShift_RestMeasuredFromPushedEndInstant()
    {
        var employeeId = Guid.NewGuid();
        var shiftType = ShiftType(new TimeOnly(8, 0), new TimeOnly(16, 0));
        var history = new[]
        {
            // 22:00 on the 9th -> 06:00 on the 10th (EndsNextDay)
            Assignment(employeeId, shiftType.Id, new DateOnly(2026, 8, 9), new TimeOnly(22, 0), new TimeOnly(6, 0), endsNextDay: true),
        };
        var hypotheticalStart = new DateOnly(2026, 8, 10).ToDateTime(shiftType.StartTime);
        var hypotheticalEnd = new DateOnly(2026, 8, 10).ToDateTime(shiftType.EndTime);

        var outcome = ShiftSuggestionEngine.EvaluateRestTime(history, hypotheticalStart, hypotheticalEnd);

        Assert.False(outcome.Eligible);
        Assert.Equal(SuggestionReasonCode.InsufficientRest, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateRestTime_NoAdjacentShifts_IsEligible()
    {
        var shiftType = ShiftType();
        var hypotheticalStart = new DateOnly(2026, 8, 10).ToDateTime(shiftType.StartTime);
        var hypotheticalEnd = new DateOnly(2026, 8, 10).ToDateTime(shiftType.EndTime);

        var outcome = ShiftSuggestionEngine.EvaluateRestTime([], hypotheticalStart, hypotheticalEnd);

        Assert.True(outcome.Eligible);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void EvaluateConsecutiveDays_SeventhDay_IsIneligible()
    {
        var employeeId = Guid.NewGuid();
        var shiftType = ShiftType();
        var history = Enumerable.Range(3, 6)
            .Select(day => Assignment(employeeId, shiftType.Id, new DateOnly(2026, 8, day), shiftType.StartTime, shiftType.EndTime))
            .ToList();

        var outcome = ShiftSuggestionEngine.EvaluateConsecutiveDays(history, new DateOnly(2026, 8, 9));

        Assert.False(outcome.Eligible);
        Assert.Equal(SuggestionReasonCode.TooManyConsecutiveDays, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateConsecutiveDays_SixthDay_IsEligible()
    {
        var employeeId = Guid.NewGuid();
        var shiftType = ShiftType();
        var history = Enumerable.Range(3, 5)
            .Select(day => Assignment(employeeId, shiftType.Id, new DateOnly(2026, 8, day), shiftType.StartTime, shiftType.EndTime))
            .ToList();

        var outcome = ShiftSuggestionEngine.EvaluateConsecutiveDays(history, new DateOnly(2026, 8, 8));

        Assert.True(outcome.Eligible);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void EvaluateSameDayOverlap_AlreadyAssigned_IsScoredNotExcluded()
    {
        var employeeId = Guid.NewGuid();
        var shiftType = ShiftType();
        var date = new DateOnly(2026, 8, 10);
        var history = new[] { Assignment(employeeId, shiftType.Id, date, shiftType.StartTime, shiftType.EndTime) };

        var outcome = ShiftSuggestionEngine.EvaluateSameDayOverlap(history, date);

        Assert.True(outcome.Eligible); // ShiftOverlapValidator only Warns, doesn't exclude
        Assert.Equal(-3m, outcome.ScoreDelta);
        Assert.Equal(SuggestionReasonCode.AlreadyAssignedThatDay, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateSameDayOverlap_NoneThatDay_NoPenalty()
    {
        var outcome = ShiftSuggestionEngine.EvaluateSameDayOverlap([], new DateOnly(2026, 8, 10));

        Assert.Equal(0m, outcome.ScoreDelta);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void EvaluateShiftTypePreference_Preferred_ScoresPositive()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var prefs = new[]
        {
            new ShiftTypePreference { Id = Guid.NewGuid(), EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Level = PreferenceLevel.Preferred },
        };

        var outcome = ShiftSuggestionEngine.EvaluateShiftTypePreference(employee, shiftType, prefs);

        Assert.Equal(2m, outcome.ScoreDelta);
        Assert.Equal(SuggestionReasonCode.ShiftTypePreferred, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateShiftTypePreference_Avoided_ScoresNegative()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var prefs = new[]
        {
            new ShiftTypePreference { Id = Guid.NewGuid(), EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Level = PreferenceLevel.Avoid },
        };

        var outcome = ShiftSuggestionEngine.EvaluateShiftTypePreference(employee, shiftType, prefs);

        Assert.Equal(-2m, outcome.ScoreDelta);
        Assert.Equal(SuggestionReasonCode.ShiftTypeAvoided, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateWeekdayPreference_Preferred_ScoresPositive()
    {
        var employee = Employee();
        var monday = new DateOnly(2026, 8, 10);
        var prefs = new[]
        {
            new WeekdayPreference { Id = Guid.NewGuid(), EmployeeId = employee.Id, DayOfWeek = DayOfWeek.Monday, Level = PreferenceLevel.Preferred },
        };

        var outcome = ShiftSuggestionEngine.EvaluateWeekdayPreference(employee, monday, prefs);

        Assert.Equal(1m, outcome.ScoreDelta);
        Assert.Equal(SuggestionReasonCode.WeekdayPreferred, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateWeekdayPreference_Avoided_ScoresNegative()
    {
        var employee = Employee();
        var monday = new DateOnly(2026, 8, 10);
        var prefs = new[]
        {
            new WeekdayPreference { Id = Guid.NewGuid(), EmployeeId = employee.Id, DayOfWeek = DayOfWeek.Monday, Level = PreferenceLevel.Avoid },
        };

        var outcome = ShiftSuggestionEngine.EvaluateWeekdayPreference(employee, monday, prefs);

        Assert.Equal(-1m, outcome.ScoreDelta);
        Assert.Equal(SuggestionReasonCode.WeekdayAvoided, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateContractTarget_UnderTarget_ScoresPositive()
    {
        var employee = Employee();
        var contract = Contract(employee.Id, ScheduleStart, weeklyHours: 40);
        var outcome = ShiftSuggestionEngine.EvaluateContractTarget(
            employee, ScheduleStart, ScheduleEnd, [], [contract], []);

        Assert.Equal(1m, outcome.ScoreDelta);
        Assert.Equal(SuggestionReasonCode.UnderContractTarget, outcome.Reason!.Code);
    }

    [Fact]
    public void EvaluateContractTarget_NoContract_NoBonus()
    {
        var employee = Employee();
        var outcome = ShiftSuggestionEngine.EvaluateContractTarget(
            employee, ScheduleStart, ScheduleEnd, [], [], []);

        Assert.Equal(0m, outcome.ScoreDelta);
        Assert.Null(outcome.Reason);
    }

    [Fact]
    public void EvaluateContractTarget_AtOrOverTarget_NoBonus()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        // 1 day's worth of hours already scaled to the full month's contract target.
        var contract = Contract(employee.Id, ScheduleStart, weeklyHours: 0.01m);
        var scheduleAssignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 1), shiftType.StartTime, shiftType.EndTime),
        };
        var outcome = ShiftSuggestionEngine.EvaluateContractTarget(
            employee, ScheduleStart, ScheduleEnd, [], [contract], scheduleAssignments);

        Assert.Equal(0m, outcome.ScoreDelta);
        Assert.Null(outcome.Reason);
    }
}
