using ShiftPlanner.Application.Validation;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Application.Dashboard;

// issue #95: pure, DB-free aggregation logic extracted out of DashboardController (the
// controller now only loads raw data via EF Core and maps this class's DTOs onto the HTTP
// response), same pattern ScheduleValidator/WageCalculator already establish elsewhere in
// this layer — a static class over plain data in, DTOs out, no dependency on ASP.NET Core or
// ApplicationDbContext. Every method here is a straight, behavior-preserving move of what used
// to be a private static method on DashboardController; none of the logic itself changed.
public static class DashboardAggregator
{
    public static (DateOnly From, DateOnly To) ResolvePeriod(DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (from is { } f && to is { } t)
            return (f, t);

        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return (monday, monday.AddDays(6));
    }

    public static decimal? DeltaPercent(decimal current, decimal previous) =>
        previous == 0 ? null : Math.Round((current - previous) / previous * 100m, 1);

    public static List<CoverageDayDto> BuildCoverage(
        IReadOnlyList<ShiftAssignment> assignments, IReadOnlyDictionary<Guid, ShiftType> shiftTypesById,
        IReadOnlyList<StaffingRequirement> requirements, IReadOnlyDictionary<Guid, Employee> employeesById,
        DateOnly periodFrom, DateOnly periodTo, Guid? teamId, Guid? shiftTypeId)
    {
        var result = new List<CoverageDayDto>();
        var seen = new HashSet<(Guid ShiftTypeId, DateOnly Date)>();

        foreach (var group in assignments.GroupBy(a => (a.ShiftTypeId, a.Date)))
        {
            if (!shiftTypesById.TryGetValue(group.Key.ShiftTypeId, out var shiftType) || shiftType.MinStaffing is not { } min || min == 0)
                continue;

            var scheduled = group.Select(a => a.EmployeeId).Distinct().Count();
            var percent = Math.Round(scheduled * 100m / min, 1);
            var status = percent >= 95 ? CoverageStatus.Green : percent >= 85 ? CoverageStatus.Yellow : CoverageStatus.Red;
            result.Add(new CoverageDayDto(group.Key.Date, shiftType.Id, shiftType.Name, scheduled, min, percent, status));
            seen.Add(group.Key);
        }

        // issue #69: StaffingRequirement-driven rows the loop above can never produce, since it
        // only ever iterates (ShiftType, Date) pairs that already have at least one assignment —
        // a slot with a configured requirement and zero assignments was previously invisible
        // to this coverage list entirely. `assignments` here is already team/shiftType-filtered
        // by the caller (`current`), so a requirement with no TeamId of its own is counted
        // against exactly that same filtered pool; a requirement with a TeamId narrows further.
        foreach (var date in DateRange(periodFrom, periodTo))
        {
            foreach (var requirement in requirements)
            {
                if (requirement.DayOfWeek != date.DayOfWeek)
                    continue;
                if (shiftTypeId is { } filterShiftType && requirement.ShiftTypeId != filterShiftType)
                    continue;
                if (teamId is { } filterTeam && requirement.TeamId is { } requirementTeam && requirementTeam != filterTeam)
                    continue;
                if (!shiftTypesById.TryGetValue(requirement.ShiftTypeId, out var shiftType))
                    continue;
                if (!seen.Add((requirement.ShiftTypeId, date)))
                    continue;

                var scheduled = assignments
                    .Where(a => a.ShiftTypeId == requirement.ShiftTypeId && a.Date == date)
                    .Where(a => requirement.TeamId is not { } requirementTeamId
                        || (employeesById.TryGetValue(a.EmployeeId, out var employee) && employee.TeamId == requirementTeamId))
                    .Select(a => a.EmployeeId)
                    .Distinct()
                    .Count();

                var percent = Math.Round(scheduled * 100m / requirement.MinimumStaffing, 1);
                var status = percent >= 95 ? CoverageStatus.Green : percent >= 85 ? CoverageStatus.Yellow : CoverageStatus.Red;
                result.Add(new CoverageDayDto(date, shiftType.Id, shiftType.Name, scheduled, requirement.MinimumStaffing, percent, status));
            }
        }

        return result.OrderBy(c => c.Date).ThenBy(c => c.ShiftTypeName).ToList();
    }

    private static IEnumerable<DateOnly> DateRange(DateOnly from, DateOnly to)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
            yield return date;
    }

    // Average coverage across every (ShiftType, Date) pair that had at least one assignment
    // (capped at 100% per pair so heavy overstaffing on one slot can't mask understaffing on
    // another) — DashboardController.Get's own KPI-level rollup of BuildCoverage's list above.
    public static decimal CoveragePercent(IReadOnlyList<CoverageDayDto> coverage) =>
        coverage.Count == 0 ? 100m : Math.Round(coverage.Average(c => Math.Min(100m, c.CoveragePercent)), 1);

    public static List<PainPointDto> BuildPainPoints(
        IReadOnlyList<Schedule> schedules,
        IReadOnlyDictionary<Guid, IReadOnlyList<ShiftAssignment>> assignmentsByScheduleId,
        IReadOnlyList<ShiftAssignment> historyPool,
        IReadOnlyDictionary<Guid, Employee> employeesById,
        IReadOnlyList<ShiftType> shiftTypes,
        IReadOnlyList<Contract> contracts,
        IReadOnlyList<Absence> absences,
        IReadOnlyList<StaffingRequirement> staffingRequirements,
        Guid? teamId)
    {
        var points = new List<PainPointDto>();
        foreach (var schedule in schedules)
        {
            var assignments = assignmentsByScheduleId.GetValueOrDefault(schedule.Id, []);
            // issue #56: sliced back down to this schedule's own ±6-day window (same window
            // SchedulesController's /validate endpoint uses) rather than the omitted-entirely
            // lookback this endpoint shipped with originally — RestTimeValidator/
            // ConsecutiveDaysValidator now see the same cross-schedule-boundary history /validate
            // would, instead of only this schedule's own assignments.
            var historyStart = schedule.StartDate.AddDays(-6);
            var historyEnd = schedule.EndDate.AddDays(6);
            var historyAssignments = historyPool.Where(a => a.Date >= historyStart && a.Date <= historyEnd).ToList();
            var result = ScheduleValidator.Validate(schedule, assignments, employeesById.Values.ToList(), shiftTypes, contracts, historyAssignments, absences, staffingRequirements);

            foreach (var (issue, severity) in result.Errors.Select(e => (e, PainSeverity.Error)).Concat(result.Warnings.Select(w => (w, PainSeverity.Warning))))
            {
                if (teamId is not null && issue.EmployeeId is { } employeeId
                    && employeesById.TryGetValue(employeeId, out var emp) && emp.TeamId != teamId)
                    continue;

                var employeeName = issue.EmployeeId is { } id && employeesById.TryGetValue(id, out var e)
                    ? $"{e.FirstName} {e.LastName}" : null;
                points.Add(new PainPointDto(issue.Type.ToString(), severity, issue.Message, schedule.Id, schedule.Name, issue.EmployeeId, employeeName));
            }
        }
        return points.OrderByDescending(p => p.Severity == PainSeverity.Error).ToList();
    }

    public static PlanningStatusDto BuildPlanningStatus(IReadOnlyList<Schedule> schedules, IReadOnlyList<PainPointDto> painPoints)
    {
        var conflictedScheduleIds = painPoints.Where(p => p.Severity == PainSeverity.Error).Select(p => p.ScheduleId).ToHashSet();
        var affected = schedules.Where(s => conflictedScheduleIds.Contains(s.Id))
            .Select(s => new ScheduleRefDto(s.Id, s.Name, s.StartDate, s.Status)).ToList();
        var published = schedules.Count(s => s.Status == ScheduleStatus.Published);
        var completion = schedules.Count == 0 ? 0m : Math.Round(published * 100m / schedules.Count, 1);

        return new PlanningStatusDto(
            schedules.Count(s => s.Status == ScheduleStatus.Draft), published, affected.Count, completion, affected);
    }

    // issue #56: aggregated per-surcharge-type so the dashboard can show a cost breakdown, not
    // just a total — WageCalculator.Breakdown (not the plain LaborCost total) is the single
    // source of truth for the split, same as BuildCost always was for the total alone.
    public static CostBreakdownDto BuildCostBreakdown(
        IReadOnlyList<ShiftAssignment> assignments, IReadOnlyList<Contract> contracts,
        IReadOnlyDictionary<Guid, Bundesland?> bundeslandByEmployee,
        IReadOnlyDictionary<Bundesland, HashSet<DateOnly>> holidaysByBundesland,
        HashSet<DateOnly> nationwideHolidays)
    {
        decimal regular = 0, night = 0, sunday = 0, holiday = 0;
        foreach (var a in assignments)
        {
            var contract = Contract.ActiveOn(contracts, a.EmployeeId, a.Date);
            var netHours = WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes);
            var land = bundeslandByEmployee.GetValueOrDefault(a.EmployeeId);
            var isHoliday = (land is { } bl ? holidaysByBundesland[bl] : nationwideHolidays).Contains(a.Date);
            // issue #58: BreakMinutes/BreakStartTime threaded through so the night-surcharge
            // portion of the breakdown gets the same break-adjusted precision LaborCostWithSurcharges'
            // other call sites already have.
            var timing = new ShiftTiming(a.StartTime, a.EndTime, a.BreakMinutes, a.BreakStartTime);
            var breakdown = WageCalculator.Breakdown(timing, a.Date.DayOfWeek, isHoliday, netHours, contract?.HourlyRate);
            if (breakdown is not { } b)
                continue;
            regular += b.Regular;
            night += b.Night;
            sunday += b.Sunday;
            holiday += b.Holiday;
        }
        return new CostBreakdownDto(regular, night, sunday, holiday);
    }

    // issue #56: per-employee utilization restored from the trimmed mockup scope — same
    // WorkingTimeCalculator.ExpectedHours formula ContractValidator/HoursBalanceCalculator use,
    // grouped by employee instead of summed. The schedule-wide UtilizationDto/OvertimeHours KPI
    // are now just aggregates over this list rather than a separate pass over the same data.
    //
    // issue #70: used to resolve one Contract at `from` and scale it across the whole [from,to]
    // period — wrong for any employee whose contract changes mid-period. ExpectedHours now
    // takes the full contracts list and resolves the applicable one per day itself.
    public static List<EmployeeUtilizationDto> BuildEmployeeUtilization(
        IReadOnlyList<Employee> employees, IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyList<Contract> contracts, IReadOnlyList<Absence> absences, DateOnly from, DateOnly to)
    {
        var result = new List<EmployeeUtilizationDto>();
        foreach (var employee in employees)
        {
            var expected = WorkingTimeCalculator.ExpectedHours(contracts, absences, employee.Id, from, to);
            var actual = assignments.Where(a => a.EmployeeId == employee.Id)
                .Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
            var percent = expected == 0 ? 0m : Math.Round(actual * 100m / expected, 1);
            result.Add(new EmployeeUtilizationDto(
                employee.Id, $"{employee.FirstName} {employee.LastName}", expected, actual, percent, Math.Max(0, actual - expected)));
        }
        return result.OrderByDescending(u => u.PlannedHours).ToList();
    }

    public static UtilizationDto BuildUtilization(IReadOnlyList<EmployeeUtilizationDto> byEmployee) => new(
        byEmployee.Sum(u => u.ContractCapacityHours),
        byEmployee.Sum(u => u.PlannedHours),
        UtilizationPercent(byEmployee),
        byEmployee.ToList());

    public static decimal UtilizationPercent(IReadOnlyList<EmployeeUtilizationDto> byEmployee)
    {
        var capacity = byEmployee.Sum(u => u.ContractCapacityHours);
        var planned = byEmployee.Sum(u => u.PlannedHours);
        return capacity == 0 ? 0m : Math.Round(planned * 100m / capacity, 1);
    }
}
