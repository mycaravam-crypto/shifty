using ShiftPlanner.Application.Validation;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class ContractValidatorTests
{
    [Fact]
    public void PlannedHoursWithinContract_NoError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7)); // 7 days
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 40m);
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0),
        };

        var result = new ValidationResult();
        ContractValidator.Validate(schedule, assignments, [contract], null, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void PlannedHoursExceedContract_ProducesError()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7)); // 7 days -> full 32h available
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 32m);
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0),
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 4), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0),
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 5), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0),
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 6), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0),
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 7), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0),
        }; // 40h planned vs 32h contract

        var result = new ValidationResult();
        ContractValidator.Validate(schedule, assignments, [contract], null, result);

        var error = Assert.Single(result.Errors);
        Assert.Equal("ContractHoursExceeded", error.Type);
    }

    [Fact]
    public void ScalesLimitByScheduleSpan_NotAssumingSevenDays()
    {
        // A month-long schedule (31 days) should scale the weekly limit up, not flag every month.
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)); // 31 days
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 40m);
        // 20 weekdays * 8h = 160h, well under 40 * 31/7 ≈ 177h.
        var assignments = Enumerable.Range(1, 20)
            .Select(day => Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, day), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0))
            .ToArray();

        var result = new ValidationResult();
        ContractValidator.Validate(schedule, assignments, [contract], null, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AbsenceDays_ReduceExpectedHours_SoUnscaledPassButScaledFails()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7)); // 7 days, 32h expected unscaled
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 32m);
        // Employee absent for 6 of the 7 days -> effective days = 1 -> expected = 32/7 ≈ 4.57h.
        var absences = new[] { Absence(employee.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 6)) };
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 7), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0),
        }; // 8h planned > ~4.57h expected after absence-scaling

        var result = new ValidationResult();
        ContractValidator.Validate(schedule, assignments, [contract], absences, result);

        Assert.Single(result.Errors);
    }

    // issue #70: used to resolve the contract active at the schedule's *start* date only and
    // scale the whole span by that one contract's WeeklyHours — wrong once a Contract changes
    // mid-schedule (a month-long Schedule easily outlives a raise/hours change).
    [Fact]
    public void ContractChangesMidSchedule_BlendsBothSegments_DoesNotFalseFlag()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)); // 31 days
        var earlier = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 20m, validTo: new DateOnly(2026, 8, 15));
        var later = Contract(employee.Id, new DateOnly(2026, 8, 16), weeklyHours: 40m);
        // 15 shifts * 8h = 120h planned. Old (buggy) behavior scaled the whole 31-day span by
        // the 20h/week contract active on Aug 1 -> ~88.57h expected, which would incorrectly
        // flag this as ContractHoursExceeded. Correctly blending both segments (20h/week for
        // 15 days + 40h/week for 16 days) -> ~134.29h expected, so 120h planned must NOT flag.
        var assignments = Enumerable.Range(1, 15)
            .Select(day => Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, day), new TimeOnly(8, 0), new TimeOnly(16, 0), breakMinutes: 0))
            .ToArray();

        var result = new ValidationResult();
        ContractValidator.Validate(schedule, assignments, [earlier, later], null, result);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NoContractCoveringSchedule_Skipped()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        var contract = Contract(employee.Id, new DateOnly(2026, 12, 1), weeklyHours: 10m); // starts after schedule
        var assignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(20, 0), breakMinutes: 0),
        };

        var result = new ValidationResult();
        ContractValidator.Validate(schedule, assignments, [contract], null, result);

        Assert.Empty(result.Errors);
    }
}
