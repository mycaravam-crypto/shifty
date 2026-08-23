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
        Assert.Equal("InsufficientRest", error.Type);
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
}
