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
        Assert.Equal(ValidationIssueCode.InsufficientBreak, error.Type);
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
        Assert.Equal(ValidationIssueCode.InsufficientBreak, error.Type);
    }

    [Fact]
    public void ExactlySixHours_NoBreakRequired()
    {
        // The >6h check is strict, so a shift lasting exactly 6h gross does not yet cross into
        // the 30min-minimum bracket — only issue #118's currently-untested boundary.
        var employee = Employee();
        var shiftType = ShiftType();
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(14, 0), breakMinutes: 0);

        var result = new ValidationResult();
        BreakMinutesValidator.Validate([assignment], result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ExactlyNineHours_OnlyThirtyMinuteBreakRequired()
    {
        // The >9h check is strict too, so a shift lasting exactly 9h gross still only falls into
        // the 30min bracket (from >6h), not the 45min one — a 30min break must satisfy it.
        var employee = Employee();
        var shiftType = ShiftType();
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(6, 0), new TimeOnly(15, 0), breakMinutes: 30);

        var result = new ValidationResult();
        BreakMinutesValidator.Validate([assignment], result);

        Assert.Empty(result.Errors);
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

    // issue #101: locks down the flip side of the wraparound quirk documented above and in
    // WorkingTimeCalculator.IsValidShiftTiming — a genuinely-backwards EndTime < StartTime is
    // NOT caught by this validator's `grossMinutes <= 0` defensive check (unlike
    // RestTimeValidator/ShiftSuggestionEngine's identical-looking `EndTime > StartTime` filters,
    // which ARE direct TimeOnly comparisons and correctly exclude it). Instead it falls through
    // and gets processed as if it were a real ~22h overnight shift. This is current, unchanged
    // behavior — the write boundary is what actually prevents such data from existing — but is
    // worth pinning down explicitly since it's easy to assume (wrongly) that this validator's
    // defense-in-depth check behaves the same as the other two.
    [Fact]
    public void GenuinelyBackwardsShift_NotSkipped_ProcessedWithWrappedDuration()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        // 10:00 -> 08:00 wraps to a 22h gross span (see WorkingTimeCalculatorTests), well over
        // the 9h/45min threshold, with no break at all.
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(10, 0), new TimeOnly(8, 0), breakMinutes: 0);

        var result = new ValidationResult();
        BreakMinutesValidator.Validate([assignment], result);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationIssueCode.InsufficientBreak, error.Type);
    }
}
