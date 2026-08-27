using ShiftPlanner.Application.Validation;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class ShiftOverlapValidatorTests
{
    [Fact]
    public void NoOverlap_NoWarning()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var date = new DateOnly(2026, 8, 24);
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(6, 0), new TimeOnly(14, 0)),
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(14, 0), new TimeOnly(22, 0)),
        };

        var result = new ValidationResult();
        ShiftOverlapValidator.Validate(assignments, result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void OverlappingShiftsSameEmployeeSameDay_ProducesWarning()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var date = new DateOnly(2026, 8, 24);
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(6, 0), new TimeOnly(14, 0)),
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(10, 0), new TimeOnly(14, 0)),
        };

        var result = new ValidationResult();
        ShiftOverlapValidator.Validate(assignments, result);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ValidationIssueCode.ShiftOverlap, warning.Type);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void OverlapAcrossDifferentEmployees_NotFlagged()
    {
        var employee1 = Employee();
        var employee2 = Employee();
        var shiftType = ShiftType();
        var date = new DateOnly(2026, 8, 24);
        var assignments = new[]
        {
            Assignment(employee1.Id, shiftType.Id, date, new TimeOnly(6, 0), new TimeOnly(14, 0)),
            Assignment(employee2.Id, shiftType.Id, date, new TimeOnly(6, 0), new TimeOnly(14, 0)),
        };

        var result = new ValidationResult();
        ShiftOverlapValidator.Validate(assignments, result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void BackToBackShifts_TouchingNotOverlapping_NoWarning()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var date = new DateOnly(2026, 8, 24);
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(6, 0), new TimeOnly(14, 0)),
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(14, 0), new TimeOnly(18, 0)),
        };

        var result = new ValidationResult();
        ShiftOverlapValidator.Validate(assignments, result);

        Assert.Empty(result.Warnings);
    }

    // issue #157: the case the old per-(EmployeeId, Date) grouping structurally couldn't catch —
    // an overnight shift bleeding into the next calendar day's early morning, overlapping a shift
    // assigned to *that* day.
    [Fact]
    public void OvernightShift_OverlapsEarlyShiftNextCalendarDay_ProducesWarning()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var day1 = new DateOnly(2026, 8, 24);
        var day2 = day1.AddDays(1);
        var assignments = new[]
        {
            // 22:00 day1 -> 07:00 day2
            Assignment(employee.Id, shiftType.Id, day1, new TimeOnly(22, 0), new TimeOnly(7, 0), endsNextDay: true),
            // 06:00-14:00 on day2 — overlaps the tail end of the overnight shift above
            Assignment(employee.Id, shiftType.Id, day2, new TimeOnly(6, 0), new TimeOnly(14, 0)),
        };

        var result = new ValidationResult();
        ShiftOverlapValidator.Validate(assignments, result);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(ValidationIssueCode.ShiftOverlap, warning.Type);
    }

    [Fact]
    public void OvernightShift_NoOverlapWithNextDayShift_NoWarning()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var day1 = new DateOnly(2026, 8, 24);
        var day2 = day1.AddDays(1);
        var assignments = new[]
        {
            // 22:00 day1 -> 06:00 day2
            Assignment(employee.Id, shiftType.Id, day1, new TimeOnly(22, 0), new TimeOnly(6, 0), endsNextDay: true),
            // 08:00-16:00 on day2 — well after the overnight shift ends
            Assignment(employee.Id, shiftType.Id, day2, new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };

        var result = new ValidationResult();
        ShiftOverlapValidator.Validate(assignments, result);

        Assert.Empty(result.Warnings);
    }
}
