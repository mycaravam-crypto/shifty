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
        Assert.Contains(result.Errors, e => e.Type == ValidationIssueCode.InsufficientBreak);
        Assert.Contains(result.Errors, e => e.Type == ValidationIssueCode.ContractHoursExceeded);
        Assert.Contains(result.Warnings, w => w.Type == ValidationIssueCode.Understaffed);
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

        Assert.Contains(result.Errors, e => e.Type == ValidationIssueCode.InsufficientRest);
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

        Assert.Contains(result.Errors, e => e.Type == ValidationIssueCode.AssignedDuringAbsence);
    }

    // issue #117: CombinesMultipleRuleViolationsInOneResult above only combines 3 of the 8
    // distinct ValidationIssue.Type values the app's 8 validator classes can produce
    // (ShiftOverlapValidator, EligibilityValidator, BreakMinutesValidator, StaffingValidator,
    // ContractValidator, AbsenceValidator, RestTimeValidator, ConsecutiveDaysValidator) —
    // Eligibility, ShiftOverlap and ConsecutiveDays were never exercised together with the
    // others in one realistic "messy schedule", only in isolation in their own test classes.
    // This test deliberately trips all 8 at once and checks two things a single-rule test
    // can't: that none of the rules mask/suppress one another when they all fire on the same
    // ValidationResult, and that per-employee rules don't bleed onto an unrelated employee
    // (Ben) who happens to share the same understaffed ShiftType/dates as the "messy" one
    // (Anna) but is otherwise clean.
    [Fact]
    public void KitchenSink_MessySchedule_TripsAllEightRuleTypesWithoutMaskingOrBleed()
    {
        var normal = ShiftType(new TimeOnly(8, 0), new TimeOnly(16, 0), minStaffing: 3);
        var spaet = ShiftType(new TimeOnly(14, 0), new TimeOnly(22, 0));

        var anna = Employee("Anna", "Schmidt");
        anna.EligibleShiftTypes = [normal]; // not spaet -> ShiftTypeNotEligible below
        var ben = Employee("Ben", "Krause");

        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 10));
        var annaContract = Contract(anna.Id, new DateOnly(2026, 1, 1), weeklyHours: 10m); // trivially exceeded
        var benContract = Contract(ben.Id, new DateOnly(2026, 1, 1), weeklyHours: 40m); // generous, stays clean
        var annaAbsence = Absence(anna.Id, new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 5));

        // Anna: a run of consecutive workdays (Aug 1-7), one day (Aug 3) double-booked into an
        // ineligible ShiftType with no break, and a shift scheduled right on top of her own
        // absence (Aug 5).
        var annaAssignments = new[]
        {
            Assignment(anna.Id, normal.Id, new DateOnly(2026, 8, 1), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id),
            Assignment(anna.Id, normal.Id, new DateOnly(2026, 8, 2), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id),
            Assignment(anna.Id, normal.Id, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0, scheduleId: schedule.Id), // InsufficientBreak
            Assignment(anna.Id, spaet.Id, new DateOnly(2026, 8, 3), new TimeOnly(14, 0), new TimeOnly(22, 0), breakMinutes: 30, scheduleId: schedule.Id), // overlaps the shift above + ShiftTypeNotEligible
            Assignment(anna.Id, normal.Id, new DateOnly(2026, 8, 4), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id),
            Assignment(anna.Id, normal.Id, new DateOnly(2026, 8, 5), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id), // AssignedDuringAbsence
            Assignment(anna.Id, normal.Id, new DateOnly(2026, 8, 6), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id),
            Assignment(anna.Id, normal.Id, new DateOnly(2026, 8, 7), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id),
        };
        // A late shift the evening before the schedule starts: <11h rest before Aug 1's 08:00
        // start, and it extends Anna's consecutive-day streak to 8 days (Jul 31 - Aug 7).
        var annaLateNightBefore = Assignment(anna.Id, normal.Id, new DateOnly(2026, 7, 31), new TimeOnly(22, 0), new TimeOnly(23, 59), breakMinutes: 0);

        // Ben: three well-spaced, well-rested, well-under-contract shifts on the same
        // ShiftType/some of the same dates as Anna. Should end up with zero issues of his own.
        var benAssignments = new[]
        {
            Assignment(ben.Id, normal.Id, new DateOnly(2026, 8, 1), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id),
            Assignment(ben.Id, normal.Id, new DateOnly(2026, 8, 4), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id),
            Assignment(ben.Id, normal.Id, new DateOnly(2026, 8, 8), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 30, scheduleId: schedule.Id),
        };

        var scheduleAssignments = annaAssignments.Concat(benAssignments).ToList();
        var historyAssignments = scheduleAssignments.Append(annaLateNightBefore).ToList();

        var result = ScheduleValidator.Validate(
            schedule,
            scheduleAssignments,
            [anna, ben],
            [normal, spaet],
            [annaContract, benContract],
            historyAssignments: historyAssignments,
            absences: [annaAbsence]);

        Assert.False(result.IsValid);

        // All 8 distinct issue types fire in the same combined result...
        Assert.Contains(result.Warnings, w => w.Type == "ShiftOverlap");
        Assert.Contains(result.Warnings, w => w.Type == "Understaffed");
        Assert.Contains(result.Errors, e => e.Type == "ShiftTypeNotEligible");
        Assert.Contains(result.Errors, e => e.Type == "InsufficientBreak");
        Assert.Contains(result.Errors, e => e.Type == "ContractHoursExceeded");
        Assert.Contains(result.Errors, e => e.Type == "AssignedDuringAbsence");
        Assert.Contains(result.Errors, e => e.Type == "InsufficientRest");
        Assert.Contains(result.Errors, e => e.Type == "TooManyConsecutiveDays");

        // ...and every per-employee one of them is actually scoped to Anna, not just present
        // somewhere in the result.
        Assert.Contains(result.Errors, e => e.Type == "ShiftTypeNotEligible" && e.EmployeeId == anna.Id);
        Assert.Contains(result.Errors, e => e.Type == "InsufficientBreak" && e.EmployeeId == anna.Id);
        Assert.Contains(result.Errors, e => e.Type == "ContractHoursExceeded" && e.EmployeeId == anna.Id);
        Assert.Contains(result.Errors, e => e.Type == "AssignedDuringAbsence" && e.EmployeeId == anna.Id);
        Assert.Contains(result.Errors, e => e.Type == "InsufficientRest" && e.EmployeeId == anna.Id);
        Assert.Contains(result.Errors, e => e.Type == "TooManyConsecutiveDays" && e.EmployeeId == anna.Id);

        // Ben shares the understaffed ShiftType/dates with Anna and is otherwise clean,
        // well-rested, and well under his own contract — none of Anna's per-employee
        // violations should bleed onto him.
        Assert.DoesNotContain(result.Errors, e => e.EmployeeId == ben.Id);
        Assert.DoesNotContain(result.Warnings, w => w.EmployeeId == ben.Id);
    }
}
