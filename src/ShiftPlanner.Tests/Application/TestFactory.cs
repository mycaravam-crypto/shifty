using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Tests.Application;

// Shared builders for the validator tests — keeps each test focused on the rule it's exercising
// rather than re-deriving valid POCOs every time.
internal static class TestFactory
{
    public static Employee Employee(string firstName = "Anna", string lastName = "Schmidt") => new()
    {
        Id = Guid.NewGuid(),
        PersonnelNumber = Guid.NewGuid().ToString("N")[..6],
        FirstName = firstName,
        LastName = lastName,
    };

    public static ShiftType ShiftType(TimeOnly? start = null, TimeOnly? end = null, int? minStaffing = null, int? maxStaffing = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Normal",
        StartTime = start ?? new TimeOnly(8, 0),
        EndTime = end ?? new TimeOnly(16, 30),
        BreakMinutes = 30,
        Color = "#3366ff",
        MinStaffing = minStaffing,
        MaxStaffing = maxStaffing,
    };

    public static Schedule Schedule(DateOnly start, DateOnly end) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test-Schedule",
        StartDate = start,
        EndDate = end,
    };

    public static Contract Contract(Guid employeeId, DateOnly validFrom, decimal weeklyHours, DateOnly? validTo = null) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employeeId,
        ValidFrom = validFrom,
        ValidTo = validTo,
        WeeklyHours = weeklyHours,
        // Fixed test default, independent of weeklyHours — WorkingDaysPerWeek isn't read by
        // any production logic (WorkingTimeCalculator/WageCalculator/ContractValidator all
        // scale by WeeklyHours × day-span/7, never by this field), so it doesn't need to vary
        // per test case.
        WorkingDaysPerWeek = 5,
        DailyTargetHours = weeklyHours / 5,
    };

    public static ShiftAssignment Assignment(
        Guid employeeId, Guid shiftTypeId, DateOnly date, TimeOnly start, TimeOnly end, int breakMinutes = 30, Guid? scheduleId = null) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = scheduleId ?? Guid.NewGuid(),
        EmployeeId = employeeId,
        ShiftTypeId = shiftTypeId,
        Date = date,
        StartTime = start,
        EndTime = end,
        BreakMinutes = breakMinutes,
    };

    public static Absence Absence(Guid employeeId, DateOnly from, DateOnly to, AbsenceType type = AbsenceType.Vacation) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employeeId,
        From = from,
        To = to,
        Type = type,
    };

    public static StaffingRequirement StaffingRequirement(
        Guid shiftTypeId, DayOfWeek dayOfWeek, int minimumStaffing, Guid? teamId = null) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = teamId,
        ShiftTypeId = shiftTypeId,
        DayOfWeek = dayOfWeek,
        MinimumStaffing = minimumStaffing,
    };
}
