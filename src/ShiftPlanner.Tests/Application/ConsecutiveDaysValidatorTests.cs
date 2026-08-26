using ShiftPlanner.Application.Validation;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class ConsecutiveDaysValidatorTests
{
    private static ShiftPlanner.Domain.Scheduling.ShiftAssignment[] Streak(Guid employeeId, Guid shiftTypeId, DateOnly start, int days) =>
        Enumerable.Range(0, days)
            .Select(offset => Assignment(employeeId, shiftTypeId, start.AddDays(offset), new TimeOnly(8, 0), new TimeOnly(16, 0)))
            .ToArray();

    [Fact]
    public void SixConsecutiveDays_NoError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignments = Streak(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), 6);

        var result = new ValidationResult();
        ConsecutiveDaysValidator.Validate(assignments, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void SevenConsecutiveDays_ProducesError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignments = Streak(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), 7);

        var result = new ValidationResult();
        ConsecutiveDaysValidator.Validate(assignments, result);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationIssueCode.TooManyConsecutiveDays, error.Type);
    }

    [Fact]
    public void SixDaysThenRestThenSixMore_NoError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var firstStreak = Streak(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), 6);
        var secondStreak = Streak(employee.Id, shiftType.Id, new DateOnly(2026, 8, 10), 6); // day 9 is a rest day
        var assignments = firstStreak.Concat(secondStreak).ToArray();

        var result = new ValidationResult();
        ConsecutiveDaysValidator.Validate(assignments, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void DuplicateAssignmentsSameDay_CountedOnce()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var date = new DateOnly(2026, 8, 3);
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(6, 0), new TimeOnly(10, 0)),
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(11, 0), new TimeOnly(15, 0)),
        };

        var result = new ValidationResult();
        ConsecutiveDaysValidator.Validate(assignments, result);

        Assert.Empty(result.Errors);
    }
}
