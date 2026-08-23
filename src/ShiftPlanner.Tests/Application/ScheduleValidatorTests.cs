using ShiftPlanner.Application.Validation;
using ShiftPlanner.Domain.Employees;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class ScheduleValidatorTests
{
    [Fact]
    public void CleanSchedule_IsValidWithNoIssues()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 40m);
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), shiftType.StartTime, shiftType.EndTime, scheduleId: schedule.Id);

        var result = ScheduleValidator.Validate(
            schedule, [assignment], [employee], [shiftType], [contract]);

        Assert.True(result.IsValid);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CombinesMultipleRuleViolationsInOneResult()
    {
        var employee = Employee();
        var shiftType = ShiftType(minStaffing: 5); // will always be understaffed with 1 assignment
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 1m); // trivially exceeded
        // 10h gross, 0 break -> InsufficientBreak (needs 45min over 9h) AND exceeds the 1h contract.
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), new TimeOnly(6, 0), new TimeOnly(16, 0), breakMinutes: 0, scheduleId: schedule.Id);

        var result = ScheduleValidator.Validate(
            schedule, [assignment], [employee], [shiftType], [contract]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Type == "InsufficientBreak");
        Assert.Contains(result.Errors, e => e.Type == "ContractHoursExceeded");
        Assert.Contains(result.Warnings, w => w.Type == "Understaffed");
    }

    [Fact]
    public void RestTimeAndConsecutiveDays_UseHistoryWindowWhenSupplied()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 3));
        // Schedule itself has just one clean assignment...
        var scheduleAssignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(16, 0), scheduleId: schedule.Id);
        // ...but the history (spanning into a previous schedule) shows a rest-time violation the day before.
        var historyAssignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 2), new TimeOnly(20, 0), new TimeOnly(23, 59));
        var history = new[] { historyAssignment, scheduleAssignment };

        var result = ScheduleValidator.Validate(
            schedule, [scheduleAssignment], [employee], [shiftType], [],
            historyAssignments: history);

        Assert.Contains(result.Errors, e => e.Type == "InsufficientRest");
    }

    [Fact]
    public void AbsenceOverlappingAssignment_ProducesError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        var absence = Absence(employee.Id, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 3));
        var assignment = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), shiftType.StartTime, shiftType.EndTime, scheduleId: schedule.Id);

        var result = ScheduleValidator.Validate(
            schedule, [assignment], [employee], [shiftType], [], absences: [absence]);

        Assert.Contains(result.Errors, e => e.Type == "AssignedDuringAbsence");
    }
}
