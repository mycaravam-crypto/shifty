using ShiftPlanner.Application.Planning;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Application;

public class PlanningBoardAggregatorTests
{
    private static Contract MakeContract(Guid employeeId, DateOnly validFrom, decimal weeklyHours, DateOnly? validTo = null) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employeeId,
        ValidFrom = validFrom,
        ValidTo = validTo,
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
    public void ResolveTargetContract_NoContracts_ReturnsNull()
    {
        Assert.Null(PlanningBoardAggregator.ResolveTargetContract([], new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void ResolveTargetContract_PrefersContractActiveOnFromDate()
    {
        var employeeId = Guid.NewGuid();
        var older = MakeContract(employeeId, new DateOnly(2026, 1, 1), 20m, new DateOnly(2026, 6, 30));
        var active = MakeContract(employeeId, new DateOnly(2026, 7, 1), 40m);

        var result = PlanningBoardAggregator.ResolveTargetContract([older, active], new DateOnly(2026, 8, 1));

        Assert.Same(active, result);
    }

    [Fact]
    public void ResolveTargetContract_NoneActive_FallsBackToMostRecentlyStarted()
    {
        var employeeId = Guid.NewGuid();
        // Both contracts have already expired before `from` — neither is "active", but the
        // grid still shows the most recent one's target rather than none at all.
        var oldest = MakeContract(employeeId, new DateOnly(2025, 1, 1), 20m, new DateOnly(2025, 6, 30));
        var mostRecent = MakeContract(employeeId, new DateOnly(2025, 7, 1), 30m, new DateOnly(2025, 12, 31));

        var result = PlanningBoardAggregator.ResolveTargetContract([oldest, mostRecent], new DateOnly(2026, 8, 1));

        Assert.Same(mostRecent, result);
    }

    [Fact]
    public void BuildStats_NoContract_TargetHoursNull_PlannedAndBalanceStillComputed()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);
        var scheduleId = Guid.NewGuid();
        var assignments = new[]
        {
            MakeAssignment(scheduleId, employeeId, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };

        var stats = PlanningBoardAggregator.BuildStats(
            employeeId, from, to, assignments, contracts: [], absences: [],
            priorSchedules: [], priorAssignments: []);

        Assert.Null(stats.TargetHours);
        Assert.Equal(8m, stats.PlannedHours);
        Assert.Equal(0m, stats.BalanceHours);
    }

    [Fact]
    public void BuildStats_MatchesHandComputedTargetPlannedAndBalance()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31); // 31 days
        var contract = MakeContract(employeeId, new DateOnly(2026, 1, 1), 35m); // 35h/week
        var scheduleId = Guid.NewGuid();
        var assignments = new[]
        {
            MakeAssignment(scheduleId, employeeId, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(16, 0)),
            MakeAssignment(scheduleId, employeeId, new DateOnly(2026, 8, 10), new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };

        // A fully-elapsed prior schedule with a shortfall, to exercise BalanceHours too:
        // 1 week at 35h/7*7=35h expected, only 8h actually worked -> -27h balance carried in.
        var priorSchedule = MakeSchedule(new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 12));
        var priorAssignments = new[]
        {
            MakeAssignment(priorSchedule.Id, employeeId, new DateOnly(2026, 7, 6), new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };

        var stats = PlanningBoardAggregator.BuildStats(
            employeeId, from, to, assignments, contracts: [contract], absences: [],
            priorSchedules: [priorSchedule], priorAssignments: priorAssignments);

        Assert.Equal(35m * 31 / 7m, stats.TargetHours);
        Assert.Equal(16m, stats.PlannedHours);
        Assert.Equal(-27m, stats.BalanceHours);
    }

    [Fact]
    public void BuildStats_OnlyCountsTheGivenEmployeesOwnAssignments()
    {
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 31);
        var scheduleId = Guid.NewGuid();
        var assignments = new[]
        {
            MakeAssignment(scheduleId, employeeId, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(16, 0)),
            MakeAssignment(scheduleId, otherEmployeeId, new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(18, 0)),
        };

        var stats = PlanningBoardAggregator.BuildStats(
            employeeId, from, to, assignments, contracts: [], absences: [],
            priorSchedules: [], priorAssignments: []);

        Assert.Equal(8m, stats.PlannedHours);
    }

    [Fact]
    public void BuildStats_ExcludesAbsenceDaysFromTargetHours()
    {
        var employeeId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 7); // 7 days
        var contract = MakeContract(employeeId, new DateOnly(2026, 1, 1), 49m); // 7h/day
        var absences = new[]
        {
            new Absence { Id = Guid.NewGuid(), EmployeeId = employeeId, From = from, To = to, Type = AbsenceType.Vacation },
        };

        var stats = PlanningBoardAggregator.BuildStats(
            employeeId, from, to, assignmentsInRange: [], contracts: [contract], absences: absences,
            priorSchedules: [], priorAssignments: []);

        // Full week absent -> 0 effective days -> 0 target hours, not 49h.
        Assert.Equal(0m, stats.TargetHours);
    }
}
