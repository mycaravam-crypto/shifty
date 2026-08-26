using ShiftPlanner.Application.Validation;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class EligibilityValidatorTests
{
    [Fact]
    public void EmptyEligibilityList_TreatedAsUnrestricted_NoError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        EligibilityValidator.Validate([assignment], new Dictionary<Guid, ShiftPlanner.Domain.Employees.Employee> { [employee.Id] = employee }, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssignedEligibleShiftType_NoError()
    {
        var shiftType = ShiftType();
        var employee = Employee();
        employee.AddEligibleShiftType(shiftType);
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        EligibilityValidator.Validate([assignment], new Dictionary<Guid, ShiftPlanner.Domain.Employees.Employee> { [employee.Id] = employee }, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssignedIneligibleShiftType_ProducesError()
    {
        var eligibleShiftType = ShiftType();
        var otherShiftType = ShiftType();
        var employee = Employee();
        employee.AddEligibleShiftType(eligibleShiftType);
        var assignment = Assignment(employee.Id, otherShiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        EligibilityValidator.Validate([assignment], new Dictionary<Guid, ShiftPlanner.Domain.Employees.Employee> { [employee.Id] = employee }, result);

        var error = Assert.Single(result.Errors);
        Assert.Equal(ValidationIssueCode.ShiftTypeNotEligible, error.Type);
    }

    [Fact]
    public void UnknownEmployee_SkippedWithoutThrowing()
    {
        var shiftType = ShiftType();
        var assignment = Assignment(Guid.NewGuid(), shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        EligibilityValidator.Validate([assignment], new Dictionary<Guid, ShiftPlanner.Domain.Employees.Employee>(), result);

        Assert.Empty(result.Errors);
    }
}
