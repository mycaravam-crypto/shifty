using ShiftPlanner.Application.Validation;
using ShiftPlanner.Domain.Employees;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class AbsenceValidatorTests
{
    [Fact]
    public void AssignmentInsideAbsenceRange_ProducesError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var absence = Absence(employee.Id, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27));
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        AbsenceValidator.Validate([assignment], [absence], new Dictionary<Guid, Employee> { [employee.Id] = employee }, result);

        var error = Assert.Single(result.Errors);
        Assert.Equal("AssignedDuringAbsence", error.Type);
    }

    [Fact]
    public void AssignmentOutsideAbsenceRange_NoError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var absence = Absence(employee.Id, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27));
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 28), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        AbsenceValidator.Validate([assignment], [absence], new Dictionary<Guid, Employee> { [employee.Id] = employee }, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AbsenceForDifferentEmployee_NotFlagged()
    {
        var employee1 = Employee();
        var employee2 = Employee();
        var shiftType = ShiftType();
        var absence = Absence(employee1.Id, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27));
        var assignment = Assignment(employee2.Id, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        AbsenceValidator.Validate([assignment], [absence],
            new Dictionary<Guid, Employee> { [employee1.Id] = employee1, [employee2.Id] = employee2 }, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AssignmentOnAbsenceBoundaryDates_Flagged()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var absence = Absence(employee.Id, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27));
        var lastDayAssignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 27), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        AbsenceValidator.Validate([lastDayAssignment], [absence], new Dictionary<Guid, Employee> { [employee.Id] = employee }, result);

        Assert.Single(result.Errors);
    }

    [Fact]
    public void UnknownEmployee_FallsBackToPlaceholderName()
    {
        // The employee lookup can miss (stale id, or a caller that didn't pre-load it) — the
        // validator should still produce a usable message rather than throwing.
        var employeeId = Guid.NewGuid();
        var shiftType = ShiftType();
        var absence = Absence(employeeId, new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27));
        var assignment = Assignment(employeeId, shiftType.Id, new DateOnly(2026, 8, 24), new TimeOnly(8, 0), new TimeOnly(16, 0));

        var result = new ValidationResult();
        AbsenceValidator.Validate([assignment], [absence], new Dictionary<Guid, Employee>(), result);

        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Mitarbeiter ist am", error.Message);
    }
}
