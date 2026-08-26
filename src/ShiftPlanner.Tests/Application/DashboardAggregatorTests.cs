using ShiftPlanner.Application.Dashboard;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

// issue #95: DashboardAggregator was extracted out of DashboardController's private static
// methods precisely so this aggregation logic could be unit-tested in isolation, the same way
// ScheduleValidator/WageCalculator already are — these tests are the whole point of that issue.
public class DashboardAggregatorTests
{
    [Fact]
    public void BuildCoverage_ComputesPercentAndStatusPerShiftTypeAndDate()
    {
        var fullyStaffed = ShiftType(minStaffing: 2);
        var understaffed = ShiftType(minStaffing: 4);
        var unconstrained = ShiftType(); // no MinStaffing -> excluded entirely
        var e1 = Employee("Anna", "Schmidt");
        var e2 = Employee("Ben", "Muller");
        var date = new DateOnly(2026, 8, 3);

        var assignments = new[]
        {
            Assignment(e1.Id, fullyStaffed.Id, date, new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Assignment(e2.Id, fullyStaffed.Id, date, new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Assignment(e1.Id, understaffed.Id, date, new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Assignment(e1.Id, unconstrained.Id, date, new TimeOnly(8, 0), new TimeOnly(16, 0)),
        };
        var shiftTypesById = new[] { fullyStaffed, understaffed, unconstrained }.ToDictionary(s => s.Id);

        var coverage = DashboardAggregator.BuildCoverage(assignments, shiftTypesById);

        Assert.Equal(2, coverage.Count); // the unconstrained ShiftType never produces a row
        var full = Assert.Single(coverage, c => c.ShiftTypeId == fullyStaffed.Id);
        Assert.Equal(2, full.Scheduled);
        Assert.Equal(100m, full.CoveragePercent);
        Assert.Equal(CoverageStatus.Green, full.Status);
        var partial = Assert.Single(coverage, c => c.ShiftTypeId == understaffed.Id);
        Assert.Equal(1, partial.Scheduled);
        Assert.Equal(25m, partial.CoveragePercent);
        Assert.Equal(CoverageStatus.Red, partial.Status);
    }

    [Fact]
    public void CoveragePercent_AveragesAcrossRowsAndCapsAt100_AndDefaultsTo100WhenEmpty()
    {
        Assert.Equal(100m, DashboardAggregator.CoveragePercent([]));

        var shiftType = ShiftType();
        var rows = new List<CoverageDayDto>
        {
            new(new DateOnly(2026, 8, 1), shiftType.Id, shiftType.Name, 2, 2, 100m, CoverageStatus.Green),
            // Overstaffed (150%) is capped at 100% so it can't mask an understaffed row elsewhere.
            new(new DateOnly(2026, 8, 2), shiftType.Id, shiftType.Name, 3, 2, 150m, CoverageStatus.Green),
        };

        Assert.Equal(100m, DashboardAggregator.CoveragePercent(rows));
    }

    [Fact]
    public void BuildPainPoints_CollectsValidatorIssuesAndRespectsTeamFilter()
    {
        var team = new Team { Id = Guid.NewGuid(), Name = "Team A" };
        var employee = Employee();
        employee.TeamId = team.Id;
        var otherTeamEmployee = Employee("Clara", "Weber");
        otherTeamEmployee.TeamId = Guid.NewGuid();

        var shiftType = ShiftType();
        var schedule = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        // No break at all over a >9h shift -> BreakMinutesValidator Error for `employee`.
        var violating = Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 3), new TimeOnly(6, 0), new TimeOnly(16, 0), breakMinutes: 0, scheduleId: schedule.Id);
        var clean = Assignment(otherTeamEmployee.Id, shiftType.Id, new DateOnly(2026, 8, 3), shiftType.StartTime, shiftType.EndTime, scheduleId: schedule.Id);

        var assignmentsByScheduleId = new Dictionary<Guid, IReadOnlyList<ShiftAssignment>>
        {
            [schedule.Id] = [violating, clean],
        };
        var employeesById = new[] { employee, otherTeamEmployee }.ToDictionary(e => e.Id);

        var allPoints = DashboardAggregator.BuildPainPoints(
            [schedule], assignmentsByScheduleId, [violating, clean], employeesById, [shiftType], [], [], teamId: null);
        Assert.Contains(allPoints, p => p.Type == "InsufficientBreak" && p.EmployeeId == employee.Id);

        var filteredToOtherTeam = DashboardAggregator.BuildPainPoints(
            [schedule], assignmentsByScheduleId, [violating, clean], employeesById, [shiftType], [], [], teamId: otherTeamEmployee.TeamId);
        Assert.DoesNotContain(filteredToOtherTeam, p => p.EmployeeId == employee.Id);
    }

    [Fact]
    public void BuildPlanningStatus_CountsDraftPublishedAndConflicts()
    {
        var draft = Schedule(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));
        var published = Schedule(new DateOnly(2026, 8, 8), new DateOnly(2026, 8, 14));
        published.Publish("test", DateTimeOffset.UtcNow);
        var conflicted = Schedule(new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 21));
        conflicted.Publish("test", DateTimeOffset.UtcNow);

        var painPoints = new List<PainPointDto>
        {
            new("ContractHoursExceeded", PainSeverity.Error, "boom", conflicted.Id, conflicted.Name, null, null),
            new("Understaffed", PainSeverity.Warning, "meh", draft.Id, draft.Name, null, null),
        };

        var status = DashboardAggregator.BuildPlanningStatus([draft, published, conflicted], painPoints);

        Assert.Equal(1, status.DraftCount);
        Assert.Equal(2, status.PublishedCount);
        Assert.Equal(1, status.ConflictCount);
        Assert.Equal(conflicted.Id, Assert.Single(status.AffectedSchedules).Id);
        Assert.Equal(66.7m, status.CompletionPercent); // 2 published / 3 total
    }

    [Fact]
    public void BuildCostBreakdown_MatchesWageCalculatorBreakdownPerAssignment()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 40m);
        contract.HourlyRate = 20m;
        // Sunday shift -> WageCalculator applies the 50% Sunday surcharge.
        var sunday = new DateOnly(2026, 8, 2);
        Assert.Equal(DayOfWeek.Sunday, sunday.DayOfWeek);
        var assignment = Assignment(employee.Id, shiftType.Id, sunday, new TimeOnly(8, 0), new TimeOnly(16, 0));

        var bundeslandByEmployee = new Dictionary<Guid, Bundesland?> { [employee.Id] = null };
        var breakdown = DashboardAggregator.BuildCostBreakdown(
            [assignment], [contract], bundeslandByEmployee, new Dictionary<Bundesland, HashSet<DateOnly>>(), []);

        var netHours = WorkingTimeCalculator.NetHours(assignment.StartTime, assignment.EndTime, assignment.BreakMinutes);
        var timing = new ShiftTiming(assignment.StartTime, assignment.EndTime, assignment.BreakMinutes, assignment.BreakStartTime);
        var expected = WageCalculator.Breakdown(timing, sunday.DayOfWeek, false, netHours, contract.HourlyRate);

        Assert.NotNull(expected);
        Assert.Equal(expected.Value.Regular, breakdown.Regular);
        Assert.Equal(expected.Value.Sunday, breakdown.Sunday);
        Assert.Equal(0m, breakdown.Night);
        Assert.Equal(0m, breakdown.Holiday);
        Assert.Equal(expected.Value.Total, breakdown.Total);
    }

    [Fact]
    public void BuildCostBreakdown_UsesPerBundeslandHolidaySetWhenEmployeeHasOne()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 40m);
        contract.HourlyRate = 10m;
        var date = new DateOnly(2026, 1, 6); // Heilige Drei Konige - only a holiday in some states
        var assignment = Assignment(employee.Id, shiftType.Id, date, new TimeOnly(8, 0), new TimeOnly(16, 0));

        var bundeslandByEmployee = new Dictionary<Guid, Bundesland?> { [employee.Id] = Bundesland.Bayern };
        var holidaysByBundesland = new Dictionary<Bundesland, HashSet<DateOnly>> { [Bundesland.Bayern] = [date] };
        var nationwideHolidays = new HashSet<DateOnly>(); // date is not a nationwide holiday

        var breakdown = DashboardAggregator.BuildCostBreakdown(
            [assignment], [contract], bundeslandByEmployee, holidaysByBundesland, nationwideHolidays);

        Assert.True(breakdown.Holiday > 0m); // resolved via the employee's own Bundesland, not the (empty) nationwide set
    }

    [Fact]
    public void BuildEmployeeUtilization_ComputesExpectedActualAndOvertimePerEmployee()
    {
        var employee = Employee();
        var contract = Contract(employee.Id, new DateOnly(2026, 1, 1), weeklyHours: 20m); // 1 week -> 20h expected
        var from = new DateOnly(2026, 8, 3);
        var to = new DateOnly(2026, 8, 9);
        var assignments = new[]
        {
            Assignment(employee.Id, Guid.NewGuid(), new DateOnly(2026, 8, 3), new TimeOnly(8, 0), new TimeOnly(20, 0), breakMinutes: 0), // 12h
            Assignment(employee.Id, Guid.NewGuid(), new DateOnly(2026, 8, 4), new TimeOnly(8, 0), new TimeOnly(20, 0), breakMinutes: 0), // 12h
        };

        var result = DashboardAggregator.BuildEmployeeUtilization([employee], assignments, [contract], [], from, to);

        var row = Assert.Single(result);
        Assert.Equal(20m, row.ContractCapacityHours);
        Assert.Equal(24m, row.PlannedHours);
        Assert.Equal(120m, row.UtilizationPercent);
        Assert.Equal(4m, row.OvertimeHours);
    }

    [Fact]
    public void BuildEmployeeUtilization_ZeroExpectedHoursDoesNotDivideByZero()
    {
        var employee = Employee();
        var result = DashboardAggregator.BuildEmployeeUtilization(
            [employee], [], [], [], new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7));

        var row = Assert.Single(result);
        Assert.Equal(0m, row.ContractCapacityHours);
        Assert.Equal(0m, row.UtilizationPercent);
        Assert.Equal(0m, row.OvertimeHours);
    }

    [Fact]
    public void BuildUtilization_SumsCapacityAndPlannedAcrossEmployees()
    {
        var byEmployee = new List<EmployeeUtilizationDto>
        {
            new(Guid.NewGuid(), "Anna Schmidt", 20m, 24m, 120m, 4m),
            new(Guid.NewGuid(), "Ben Muller", 40m, 30m, 75m, 0m),
        };

        var utilization = DashboardAggregator.BuildUtilization(byEmployee);

        Assert.Equal(60m, utilization.ContractCapacityHours);
        Assert.Equal(54m, utilization.PlannedHours);
        Assert.Equal(90m, utilization.UtilizationPercent); // 54/60
        Assert.Equal(byEmployee, utilization.ByEmployee);
    }

    [Fact]
    public void UtilizationPercent_ZeroCapacityDoesNotDivideByZero()
    {
        Assert.Equal(0m, DashboardAggregator.UtilizationPercent([]));
    }

    [Theory]
    [InlineData(120, 100, 20.0)]
    [InlineData(80, 100, -20.0)]
    public void DeltaPercent_ComputesRelativeChange(decimal current, decimal previous, double expected)
    {
        Assert.Equal((decimal)expected, DashboardAggregator.DeltaPercent(current, previous));
    }

    [Fact]
    public void DeltaPercent_PreviousZero_ReturnsNullRatherThanDivideByZero()
    {
        Assert.Null(DashboardAggregator.DeltaPercent(50m, 0m));
    }

    [Fact]
    public void ResolvePeriod_WithNoExplicitRange_ReturnsCurrentMondayToSunday()
    {
        var aWednesday = new DateOnly(2026, 8, 12);

        var (from, to) = DashboardAggregator.ResolvePeriod(null, null, aWednesday);

        Assert.Equal(new DateOnly(2026, 8, 10), from); // Monday of that week
        Assert.Equal(new DateOnly(2026, 8, 16), to); // Sunday
    }

    [Fact]
    public void ResolvePeriod_WithExplicitRange_PassesItThrough()
    {
        var from = new DateOnly(2026, 3, 1);
        var to = new DateOnly(2026, 3, 31);

        var result = DashboardAggregator.ResolvePeriod(from, to, new DateOnly(2026, 8, 12));

        Assert.Equal((from, to), result);
    }
}
