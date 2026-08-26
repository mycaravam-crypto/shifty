using System.Reflection;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #103: EligibleShiftTypes/ShiftTypePreferences/WeekdayPreferences used to be
// `public List<T> { get; set; }` — fully reassignable and mutable from any caller. These tests
// assert the compile-time contract holds (read-only exposed type, no public setter) and that
// the dedicated Add/Remove/Replace methods are the only way to change the contents.
public class EmployeeCollectionEncapsulationTests
{
    [Theory]
    [InlineData(nameof(Employee.EligibleShiftTypes))]
    [InlineData(nameof(Employee.ShiftTypePreferences))]
    [InlineData(nameof(Employee.WeekdayPreferences))]
    public void CollectionProperty_HasNoPublicSetter_AndIsExposedAsReadOnly(string propertyName)
    {
        var property = typeof(Employee).GetProperty(propertyName)!;

        // No setter at all means external code can't do `employee.X = someOtherList`.
        Assert.Null(property.SetMethod);

        // The declared type itself must be a read-only view, not List<T>/ICollection<T> — a
        // caller holding a reference typed this way has no Add/Remove/Clear available to them.
        Assert.True(property.PropertyType.IsGenericType);
        var genericDefinition = property.PropertyType.GetGenericTypeDefinition();
        Assert.True(
            genericDefinition == typeof(IReadOnlyCollection<>) || genericDefinition == typeof(IReadOnlyList<>),
            $"{propertyName} should be exposed as IReadOnlyCollection<T>/IReadOnlyList<T>, was {property.PropertyType}.");
    }

    [Fact]
    public void AddEligibleShiftType_AddsToTheExposedCollection()
    {
        var employee = new Employee { PersonnelNumber = "1", FirstName = "Anna", LastName = "Schmidt" };
        var shiftType = new ShiftType
        {
            Name = "Früh",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 0),
            BreakMinutes = 30,
            Color = "#3366ff",
        };

        employee.AddEligibleShiftType(shiftType);

        Assert.Contains(shiftType, employee.EligibleShiftTypes);
    }

    [Fact]
    public void RemoveEligibleShiftType_RemovesFromTheExposedCollection()
    {
        var employee = new Employee { PersonnelNumber = "1", FirstName = "Anna", LastName = "Schmidt" };
        var shiftType = new ShiftType
        {
            Name = "Früh",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 0),
            BreakMinutes = 30,
            Color = "#3366ff",
        };
        employee.AddEligibleShiftType(shiftType);

        employee.RemoveEligibleShiftType(shiftType);

        Assert.DoesNotContain(shiftType, employee.EligibleShiftTypes);
    }

    [Fact]
    public void ReplaceEligibleShiftTypes_ClearsAndReplacesWholesale()
    {
        var employee = new Employee { PersonnelNumber = "1", FirstName = "Anna", LastName = "Schmidt" };
        var oldShiftType = new ShiftType
        {
            Name = "Früh",
            StartTime = new TimeOnly(8, 0),
            EndTime = new TimeOnly(16, 0),
            BreakMinutes = 30,
            Color = "#3366ff",
        };
        var newShiftType = new ShiftType
        {
            Name = "Spät",
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(22, 0),
            BreakMinutes = 30,
            Color = "#ff6633",
        };
        employee.AddEligibleShiftType(oldShiftType);

        employee.ReplaceEligibleShiftTypes([newShiftType]);

        Assert.DoesNotContain(oldShiftType, employee.EligibleShiftTypes);
        Assert.Contains(newShiftType, employee.EligibleShiftTypes);
        Assert.Single(employee.EligibleShiftTypes);
    }
}
