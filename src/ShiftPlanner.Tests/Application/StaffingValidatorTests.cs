using ShiftPlanner.Application.Validation;
using ShiftPlanner.Domain.Scheduling;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class StaffingValidatorTests
{
    [Fact]
    public void BelowMinStaffing_ProducesWarning()
    {
        var shiftType = ShiftType(minStaffing: 2);
        var employee = Employee();
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), shiftType.StartTime, shiftType.EndTime);

        var result = new ValidationResult();
        StaffingValidator.Validate([assignment], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType }, result);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Understaffed", warning.Type);
    }

    [Fact]
    public void AboveMaxStaffing_ProducesWarning()
    {
        var shiftType = ShiftType(maxStaffing: 1);
        var employee1 = Employee();
        var employee2 = Employee();
        var date = new DateOnly(2026, 8, 24);
        var assignments = new[]
        {
            Assignment(employee1.Id, shiftType.Id, date, shiftType.StartTime, shiftType.EndTime),
            Assignment(employee2.Id, shiftType.Id, date, shiftType.StartTime, shiftType.EndTime),
        };

        var result = new ValidationResult();
        StaffingValidator.Validate(assignments, new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType }, result);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Overstaffed", warning.Type);
    }

    [Fact]
    public void WithinMinMaxRange_NoWarning()
    {
        var shiftType = ShiftType(minStaffing: 1, maxStaffing: 2);
        var employee = Employee();
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), shiftType.StartTime, shiftType.EndTime);

        var result = new ValidationResult();
        StaffingValidator.Validate([assignment], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType }, result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void UnconstrainedShiftType_NeverWarns()
    {
        var shiftType = ShiftType(); // no min/max set
        var employee = Employee();
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), shiftType.StartTime, shiftType.EndTime);

        var result = new ValidationResult();
        StaffingValidator.Validate([assignment], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType }, result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void DayWithNoAssignment_NotFlagged()
    {
        // A (ShiftType, Date) pair with zero assignments should never appear — the validator
        // only iterates groups that actually exist in the assignments list.
        var shiftType = ShiftType(minStaffing: 5);
        var result = new ValidationResult();

        StaffingValidator.Validate([], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType }, result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void DistinctEmployeesCounted_NotAssignmentRows()
    {
        // Same employee assigned twice on the same date/shift type still counts as 1 headcount.
        var shiftType = ShiftType(minStaffing: 2);
        var employee = Employee();
        var date = new DateOnly(2026, 8, 24);
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            Assignment(employee.Id, shiftType.Id, date, new TimeOnly(13, 0), new TimeOnly(16, 0)),
        };

        var result = new ValidationResult();
        StaffingValidator.Validate(assignments, new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType }, result);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Understaffed", warning.Type);
    }

    [Fact]
    public void AssignmentReferencingUnknownShiftType_SkippedWithoutThrowing()
    {
        // A ShiftType id not present in the staffing dictionary (e.g. deleted after the
        // assignment was made) must be skipped defensively, not throw or misreport staffing.
        var knownShiftType = ShiftType(minStaffing: 5);
        var unknownShiftTypeId = Guid.NewGuid();
        var employee = Employee();
        var assignment = Assignment(employee.Id, unknownShiftTypeId, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        var exception = Record.Exception(() =>
            StaffingValidator.Validate([assignment], new Dictionary<Guid, ShiftType> { [knownShiftType.Id] = knownShiftType }, result));

        Assert.Null(exception);
        Assert.Empty(result.Warnings);
    }
}
