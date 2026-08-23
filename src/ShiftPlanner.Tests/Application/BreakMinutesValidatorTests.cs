using ShiftPlanner.Application.Validation;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class BreakMinutesValidatorTests
{
    [Fact]
    public void ShortShift_NoBreakRequired()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        // 5h gross, under the 6h threshold — no break required.
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(13, 0), breakMinutes: 0);

        var result = new ValidationResult();
        BreakMinutesValidator.Validate([assignment], result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void OverSixHours_RequiresThirtyMinuteBreak()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        // 7h gross with only 15min break -> violates the 30min minimum.
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(15, 0), breakMinutes: 15);

        var result = new ValidationResult();
        BreakMinutesValidator.Validate([assignment], result);

        var error = Assert.Single(result.Errors);
        Assert.Equal("InsufficientBreak", error.Type);
    }

    [Fact]
    public void OverSixHours_ThirtyMinuteBreak_Satisfied()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(15, 0), breakMinutes: 30);

        var result = new ValidationResult();
        BreakMinutesValidator.Validate([assignment], result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void OverNineHours_RequiresFortyFiveMinuteBreak()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        // 10h gross with only 30min break -> violates the 45min minimum.
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(6, 0), new TimeOnly(16, 0), breakMinutes: 30);

        var result = new ValidationResult();
        BreakMinutesValidator.Validate([assignment], result);

        var error = Assert.Single(result.Errors);
        Assert.Equal("InsufficientBreak", error.Type);
    }

    [Fact]
    public void ZeroLengthShift_SkippedDefensively()
    {
        // TimeOnly's `-` operator wraps to a non-negative TimeSpan, so grossMinutes <= 0 is only
        // reachable when EndTime == StartTime exactly (a genuinely backwards EndTime < StartTime
        // instead wraps to a positive duration) — the write boundary is what actually rejects
        // cross-midnight assignments (issue #11); this is defense-in-depth for the zero-length case.
        var employee = Employee();
        var shiftType = ShiftType();
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(8, 0), breakMinutes: 0);

        var result = new ValidationResult();
        BreakMinutesValidator.Validate([assignment], result);

        Assert.Empty(result.Errors);
    }
}
