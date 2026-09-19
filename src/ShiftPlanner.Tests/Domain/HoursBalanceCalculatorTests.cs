using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

public class HoursBalanceCalculatorTests
{
    private static Contract MakeContract(Guid employeeId, DateOnly validFrom, decimal weeklyHours) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employeeId,
        ValidFrom = validFrom,
        WeeklyHours = weeklyHours,
        WorkingDaysPerWeek = 5,
        DailyTargetHours = weeklyHours / 5,
    };

    private static Schedule MakeSchedule(DateOnly start, DateOnly end) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test",
        StartDate = start,
        EndDate = end,
    };

    private static ShiftAssignment MakeAssignment(Guid scheduleId, Guid employeeId, DateOnly date, TimeOnly start, TimeOnly end) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = scheduleId,
        EmployeeId = employeeId,
        ShiftTypeId = Guid.NewGuid(),
        Date = date,
        StartTime = start,
        EndTime = end,
        BreakMinutes = 0,
    };

    [Fact]
    public void CumulativeBalance_IgnoresSchedulesNotYetElapsed()
    {
        var employeeId = Guid.NewGuid();
        var schedule = MakeSchedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        var contract = MakeContract(employeeId, new DateOnly(2026, 1, 1), 40m);

        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 15),
            schedules: [schedule], assignments: [], contracts: [contract]);

        Assert.Equal(0m, balance);
    }

    [Fact]
    public void CumulativeBalance_SumsOverAndUnderHoursForElapsedSchedules()
    {
        var employeeId = Guid.NewGuid();
        // 1 week schedule, 20h/week contract, only 7h actually worked -> -13h balance.
        var schedule = MakeSchedule(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11));
        var contract = MakeContract(employeeId, new DateOnly(2026, 1, 1), 20m);
        var assignments = new[]
        {
            MakeAssignment(schedule.Id, employeeId, new DateOnly(2026, 1, 5), new TimeOnly(8, 0), new TimeOnly(15, 0)),
        };

        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 1),
            schedules: [schedule], assignments: assignments, contracts: [contract]);

        Assert.Equal(-13m, balance);
    }

    [Fact]
    public void CumulativeBalance_ExcludesAbsenceDaysFromExpectedHours()
    {
        var employeeId = Guid.NewGuid();
        // 7-day schedule, 7h/day expected (49h/week / 7 days), full week absent -> 0 expected -> 0 balance with no work.
        var schedule = MakeSchedule(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11));
        var contract = MakeContract(employeeId, new DateOnly(2026, 1, 1), 49m);
        var absences = new[]
        {
            new Absence { Id = Guid.NewGuid(), EmployeeId = employeeId, From = new DateOnly(2026, 1, 5), To = new DateOnly(2026, 1, 11), Type = AbsenceType.Vacation },
        };

        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 1),
            schedules: [schedule], assignments: [], contracts: [contract], absences: absences);

        Assert.Equal(0m, balance);
    }

    [Fact]
    public void CumulativeBalance_NoContractCoveringSchedule_Skipped()
    {
        var employeeId = Guid.NewGuid();
        var schedule = MakeSchedule(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11));
        // Contract starts after the schedule's start date -> not applicable.
        var contract = MakeContract(employeeId, new DateOnly(2026, 6, 1), 40m);

        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 1),
            schedules: [schedule], assignments: [], contracts: [contract]);

        Assert.Equal(0m, balance);
    }

    // issue #165: HoursAdjustments fold into the same running balance as elapsed Schedules,
    // gated by the same "before" cutoff.
    [Fact]
    public void CumulativeBalance_AddsPositiveAdjustmentBeforeCutoff()
    {
        var employeeId = Guid.NewGuid();
        var adjustments = new[]
        {
            new HoursAdjustment { EmployeeId = employeeId, Date = new DateOnly(2026, 1, 10), HoursDelta = 2.5m, Reason = "x", CreatedBy = "admin" },
        };

        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 1),
            schedules: [], assignments: [], contracts: [], adjustments: adjustments);

        Assert.Equal(2.5m, balance);
    }

    [Fact]
    public void CumulativeBalance_SubtractsNegativeAdjustmentBeforeCutoff()
    {
        var employeeId = Guid.NewGuid();
        var adjustments = new[]
        {
            new HoursAdjustment { EmployeeId = employeeId, Date = new DateOnly(2026, 1, 10), HoursDelta = -3m, Reason = "x", CreatedBy = "admin" },
        };

        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 1),
            schedules: [], assignments: [], contracts: [], adjustments: adjustments);

        Assert.Equal(-3m, balance);
    }

    [Fact]
    public void CumulativeBalance_IgnoresAdjustmentOnOrAfterCutoff()
    {
        var employeeId = Guid.NewGuid();
        var adjustments = new[]
        {
            new HoursAdjustment { EmployeeId = employeeId, Date = new DateOnly(2026, 8, 1), HoursDelta = 5m, Reason = "x", CreatedBy = "admin" },
        };

        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 1),
            schedules: [], assignments: [], contracts: [], adjustments: adjustments);

        Assert.Equal(0m, balance);
    }

    [Fact]
    public void CumulativeBalance_IgnoresAdjustmentForAnotherEmployee()
    {
        var employeeId = Guid.NewGuid();
        var adjustments = new[]
        {
            new HoursAdjustment { EmployeeId = Guid.NewGuid(), Date = new DateOnly(2026, 1, 10), HoursDelta = 5m, Reason = "x", CreatedBy = "admin" },
        };

        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 1),
            schedules: [], assignments: [], contracts: [], adjustments: adjustments);

        Assert.Equal(0m, balance);
    }

    [Fact]
    public void CumulativeBalance_CombinesScheduleDeviationAndAdjustment()
    {
        var employeeId = Guid.NewGuid();
        var schedule = MakeSchedule(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 11));
        var contract = MakeContract(employeeId, new DateOnly(2026, 1, 1), 20m);
        var assignments = new[]
        {
            MakeAssignment(schedule.Id, employeeId, new DateOnly(2026, 1, 5), new TimeOnly(8, 0), new TimeOnly(15, 0)),
        };
        var adjustments = new[]
        {
            new HoursAdjustment { EmployeeId = employeeId, Date = new DateOnly(2026, 1, 10), HoursDelta = 3m, Reason = "x", CreatedBy = "admin" },
        };

        // -13h from the schedule (see CumulativeBalance_SumsOverAndUnderHoursForElapsedSchedules) + 3h adjustment.
        var balance = HoursBalanceCalculator.CumulativeBalance(
            employeeId, before: new DateOnly(2026, 8, 1),
            schedules: [schedule], assignments: assignments, contracts: [contract], adjustments: adjustments);

        Assert.Equal(-10m, balance);
    }
}
