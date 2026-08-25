using ShiftPlanner.Application.Validation;
using ShiftPlanner.Domain.Employees;
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
}

// issue #69: StaffingRequirement models demand independently of existing assignments —
// ValidateRequirements iterates the schedule's own date range × active requirements, so it can
// flag a slot with a configured requirement and literally zero assignments, which the
// assignment-groupby-based Validate() above can never see.
public class StaffingValidatorValidateRequirementsTests
{
    private static readonly DateOnly Monday = new(2026, 8, 24);

    [Fact]
    public void RequirementWithZeroAssignments_FlagsUnderstaffed()
    {
        var shiftType = ShiftType();
        var requirement = StaffingRequirement(shiftType.Id, DayOfWeek.Monday, minimumStaffing: 2);

        var result = new ValidationResult();
        StaffingValidator.ValidateRequirements(
            Monday, Monday, [], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType },
            new Dictionary<Guid, Employee>(), [requirement], result);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal("Understaffed", warning.Type);
        Assert.Contains("0/2", warning.Message);
    }

    [Fact]
    public void NoRequirementConfigured_UntrackedComboNotFlagged()
    {
        // Preserves today's behavior for a (ShiftType, Date) combo nobody ever configured a
        // requirement for — StaffingRequirement is opt-in, not a blanket check.
        var shiftType = ShiftType();

        var result = new ValidationResult();
        StaffingValidator.ValidateRequirements(
            Monday, Monday, [], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType },
            new Dictionary<Guid, Employee>(), [], result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void RequirementMet_NoWarning()
    {
        var shiftType = ShiftType();
        var employee = Employee();
        var requirement = StaffingRequirement(shiftType.Id, DayOfWeek.Monday, minimumStaffing: 1);
        var assignment = Assignment(employee.Id, shiftType.Id, Monday, shiftType.StartTime, shiftType.EndTime);

        var result = new ValidationResult();
        StaffingValidator.ValidateRequirements(
            Monday, Monday, [assignment], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType },
            new Dictionary<Guid, Employee> { [employee.Id] = employee }, [requirement], result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void DayOfWeekMismatch_NotFlagged()
    {
        // The requirement only applies on Mondays — a Tuesday in the schedule's range with no
        // assignments shouldn't trip it.
        var shiftType = ShiftType();
        var tuesday = Monday.AddDays(1);
        var requirement = StaffingRequirement(shiftType.Id, DayOfWeek.Monday, minimumStaffing: 2);

        var result = new ValidationResult();
        StaffingValidator.ValidateRequirements(
            tuesday, tuesday, [], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType },
            new Dictionary<Guid, Employee>(), [requirement], result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void TeamScopedRequirement_OnlyCountsThatTeam()
    {
        var shiftType = ShiftType();
        var teamId = Guid.NewGuid();
        var otherTeamId = Guid.NewGuid();
        var inTeam = Employee();
        inTeam.TeamId = teamId;
        var outOfTeam = Employee();
        outOfTeam.TeamId = otherTeamId;

        var requirement = StaffingRequirement(shiftType.Id, DayOfWeek.Monday, minimumStaffing: 1, teamId: teamId);
        // Only an employee from a different team is assigned — the team-scoped requirement
        // still isn't satisfied even though the (ShiftType, Date) slot has an assignment.
        var assignment = Assignment(outOfTeam.Id, shiftType.Id, Monday, shiftType.StartTime, shiftType.EndTime);

        var result = new ValidationResult();
        StaffingValidator.ValidateRequirements(
            Monday, Monday, [assignment], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType },
            new Dictionary<Guid, Employee> { [inTeam.Id] = inTeam, [outOfTeam.Id] = outOfTeam }, [requirement], result);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("0/1", warning.Message);
    }

    [Fact]
    public void GlobalRequirement_CountsAnyTeam()
    {
        var shiftType = ShiftType();
        var employee = Employee();
        employee.TeamId = Guid.NewGuid();
        // TeamId null on the requirement = applies across all teams.
        var requirement = StaffingRequirement(shiftType.Id, DayOfWeek.Monday, minimumStaffing: 1);
        var assignment = Assignment(employee.Id, shiftType.Id, Monday, shiftType.StartTime, shiftType.EndTime);

        var result = new ValidationResult();
        StaffingValidator.ValidateRequirements(
            Monday, Monday, [assignment], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType },
            new Dictionary<Guid, Employee> { [employee.Id] = employee }, [requirement], result);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void SpansEntireScheduleRange_ChecksEveryMatchingDate()
    {
        // Two Mondays in range, requirement unmet on both — one Warning per date.
        var shiftType = ShiftType();
        var requirement = StaffingRequirement(shiftType.Id, DayOfWeek.Monday, minimumStaffing: 1);
        var nextMonday = Monday.AddDays(7);

        var result = new ValidationResult();
        StaffingValidator.ValidateRequirements(
            Monday, nextMonday, [], new Dictionary<Guid, ShiftType> { [shiftType.Id] = shiftType },
            new Dictionary<Guid, Employee>(), [requirement], result);

        Assert.Equal(2, result.Warnings.Count);
    }
}
