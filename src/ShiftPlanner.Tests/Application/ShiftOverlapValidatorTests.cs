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
        Assert.Equal("ShiftOverlap", warning.Type);
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
}
