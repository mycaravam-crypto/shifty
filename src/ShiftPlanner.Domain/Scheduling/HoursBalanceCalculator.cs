using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;

namespace ShiftPlanner.Domain.Scheduling;

// issue #18: cumulative over/under-hours balance carried into `before`. Derived at read time
// from every fully-elapsed Schedule (same expected-vs-actual math as ContractValidator, just
// accumulated instead of checked against one) rather than a stored running total, so it can
// never drift out of sync — matches WorkingTimeCalculator's stateless pattern.
public static class HoursBalanceCalculator
{
    public static decimal CumulativeBalance(
        Guid employeeId,
        DateOnly before,
        IReadOnlyList<Schedule> schedules,
        IReadOnlyList<ShiftAssignment> assignments,
        IReadOnlyList<Contract> contracts,
        IReadOnlyList<Absence>? absences = null)
    {
        decimal balance = 0;
        foreach (var schedule in schedules.Where(s => s.EndDate < before))
        {
            var contract = contracts
                .Where(c => c.EmployeeId == employeeId && c.ValidFrom <= schedule.StartDate
                    && (c.ValidTo is null || c.ValidTo >= schedule.StartDate))
                .MaxBy(c => c.ValidFrom);
            if (contract is null)
                continue;

            // issue #17: days on Absence within this Schedule's span don't count toward the
            // expected hours, same exclusion ContractValidator applies per-schedule.
            var expected = WorkingTimeCalculator.ExpectedHours(
                contract, absences ?? [], employeeId, schedule.StartDate, schedule.EndDate);
            var actual = assignments
                .Where(a => a.ScheduleId == schedule.Id && a.EmployeeId == employeeId)
                .Sum(a => WorkingTimeCalculator.NetHours(a.StartTime, a.EndTime, a.BreakMinutes));
            balance += actual - expected;
        }
        return balance;
    }
}
