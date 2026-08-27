using ShiftPlanner.Application.Validation;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class RestTimeValidatorTests
{
    [Fact]
    public void ElevenHoursRest_NoError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(6, 0), new TimeOnly(14, 0)),
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 25), new TimeOnly(1, 0), new TimeOnly(9, 0)),
        }; // rest = 14:00 -> next day 01:00 = 11h exactly

        var result = new ValidationResult();
        RestTimeValidator.Validate(assignments, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LessThanElevenHoursRest_ProducesError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(14, 0), new TimeOnly(22, 0)),
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 25), new TimeOnly(6, 0), new TimeOnly(14, 0)),
        }; // rest = 22:00 -> next day 06:00 = 8h

        var result = new ValidationResult();
        RestTimeValidator.Validate(assignments, result);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationIssueCode.InsufficientRest, error.Type);
    }

    [Fact]
    public void DifferentEmployees_NotComparedToEachOther()
    {
        var employee1 = Employee();
        var employee2 = Employee();
        var shiftType = ShiftType();
        var assignments = new[]
        {
            Assignment(employee1.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(14, 0), new TimeOnly(22, 0)),
            Assignment(employee2.Id, shiftType.Id, new DateOnly(2026, 8, 25), new TimeOnly(6, 0), new TimeOnly(14, 0)),
        };

        var result = new ValidationResult();
        RestTimeValidator.Validate(assignments, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NonConsecutiveShiftsWithGapDay_NoError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(14, 0), new TimeOnly(22, 0)),
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 26), new TimeOnly(6, 0), new TimeOnly(14, 0)),
        };

        var result = new ValidationResult();
        RestTimeValidator.Validate(assignments, result);

        Assert.Empty(result.Errors);
    }

    // issue #101: pins down the pre-existing `EndTime > StartTime` filter (now centralized via
    // WorkingTimeCalculator.IsValidShiftTiming) — cross-midnight rows are excluded from the
    // rest-time comparison entirely, not just treated as if they had zero duration. Proven by a
    // scenario where, if the invalid row were left in, its (wrong-but-present) EndTime would put
    // it right next to the valid shift with less than 11h between them and would incorrectly
    // trip InsufficientRest.
    [Fact]
    public void CrossMidnightAssignment_ExcludedFromRestTimeCheck_NoFalseViolation()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignments = new[]
        {
            // Genuinely backwards (EndTime < StartTime) — unsupported, issue #11. If this were
            // NOT filtered out, ordering would place its EndTime (22:00 on the 9th) 8h before
            // the valid shift's StartTime (06:00 on the 10th) — under the 11h minimum.
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 9), new TimeOnly(23, 0), new TimeOnly(22, 0)),
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 10), new TimeOnly(6, 0), new TimeOnly(14, 0)),
        };

        var result = new ValidationResult();
        RestTimeValidator.Validate(assignments, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CrossMidnightAssignment_ExcludedWithoutThrowing()
    {
        // A cross-midnight EndTime <= StartTime assignment is rejected by the write endpoints
        // (issue #11), but the validator itself still filters it out defensively via its own
        // EndTime > StartTime check. Construct one directly (bypassing that normal validation)
        // to confirm it's silently excluded rather than crashing or corrupting the rest-time math.
        var employee = Employee();
        var shiftType = ShiftType();
        var crossMidnight = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(22, 0), new TimeOnly(6, 0));
        var normal = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 25), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        var exception = Record.Exception(() => RestTimeValidator.Validate([crossMidnight, normal], result));

        Assert.Null(exception);
        // Only the normal assignment survives the filter — with a single remaining assignment
        // per employee, there's no adjacent pair to compare, so no error is produced.
        Assert.Empty(result.Errors);
    }

    // issue #157: a real EndsNextDay row is no longer filtered out as malformed — its end
    // instant is Date+EndTime pushed a day later, so rest time is measured from there.
    [Fact]
    public void EndsNextDayAssignment_RestMeasuredFromPushedEndInstant()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignments = new[]
        {
            // 22:00 on the 24th -> 06:00 on the 25th (EndsNextDay)
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(22, 0), new TimeOnly(6, 0), endsNextDay: true),
            // Starts 08:00 on the 25th — only 2h after the overnight shift's real end (06:00 the 25th)
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 25), new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };

        var result = new ValidationResult();
        RestTimeValidator.Validate(assignments, result);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationIssueCode.InsufficientRest, error.Type);
    }

    [Fact]
    public void EndsNextDayAssignment_SufficientRestFromPushedEndInstant_NoError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignments = new[]
        {
            // 22:00 on the 24th -> 06:00 on the 25th (EndsNextDay)
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(22, 0), new TimeOnly(6, 0), endsNextDay: true),
            // Starts 17:00 on the 25th — 11h after the overnight shift's real end (06:00 the 25th)
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 25), new TimeOnly(17, 0), new TimeOnly(22, 0)),
        };

        var result = new ValidationResult();
        RestTimeValidator.Validate(assignments, result);

        Assert.Empty(result.Errors);
    }
}
