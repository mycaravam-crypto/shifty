using ShiftPlanner.Application.Suggestions;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

public class ShiftSuggestionEngineTests
{
    private static readonly DateOnly ScheduleStart = new(2026, 8, 1);
    private static readonly DateOnly ScheduleEnd = new(2026, 8, 31);

    private static List<ShiftSuggestion> Suggest(
        DateOnly date,
        ShiftType shiftType,
        IReadOnlyList<Employee> employees,
        IReadOnlyList<ShiftAssignment>? history = null,
        IReadOnlyList<Absence>? absences = null,
        IReadOnlyList<ShiftAssignment>? scheduleAssignments = null,
        IReadOnlyList<Contract>? contracts = null,
        IReadOnlyList<ShiftTypePreference>? shiftTypePrefs = null,
        IReadOnlyList<WeekdayPreference>? weekdayPrefs = null) =>
        ShiftSuggestionEngine.Suggest(
            date, shiftType, employees,
            history ?? [], absences ?? [],
            ScheduleStart, ScheduleEnd,
            scheduleAssignments ?? [], contracts ?? [],
            shiftTypePrefs ?? [], weekdayPrefs ?? []);

    [Fact]
    public void UnrestrictedEmployee_WithNoData_IsEligibleWithZeroScore()
    {
        var employee = Employee();
        var shiftType = ShiftType();

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [employee]);

        var suggestion = Assert.Single(result);
        Assert.True(suggestion.Eligible);
        Assert.Equal(0, suggestion.Score);
        Assert.Empty(suggestion.Reasons);
    }

    [Fact]
    public void NotInEligibleShiftTypes_IsIneligible()
    {
        var otherShiftType = ShiftType();
        var employee = Employee();
        employee.EligibleShiftTypes.Add(otherShiftType);
        var shiftType = ShiftType();

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [employee]);

        var suggestion = Assert.Single(result);
        Assert.False(suggestion.Eligible);
        Assert.Contains(suggestion.Reasons, r => r.Code == SuggestionReasonCode.NotEligible);
    }

    [Fact]
    public void AbsentOnDate_IsIneligible()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var absences = new[] { Absence(employee.Id, new DateOnly(2026, 8, 9), new DateOnly(2026, 8, 11)) };

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [employee], absences: absences);

        var suggestion = Assert.Single(result);
        Assert.False(suggestion.Eligible);
        Assert.Contains(suggestion.Reasons, r => r.Code == SuggestionReasonCode.Absent);
    }

    [Fact]
    public void InsufficientRestBeforeHypotheticalShift_IsIneligible()
    {
        var employee = Employee();
        var shiftType = ShiftType(new TimeOnly(6, 0), new TimeOnly(14, 0));
        // ends 22:00 the day before -> only 8h rest before a 06:00 start
        var history = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 9), new TimeOnly(14, 0), new TimeOnly(22, 0)),
        };

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [employee], history: history);

        var suggestion = Assert.Single(result);
        Assert.False(suggestion.Eligible);
        Assert.Contains(suggestion.Reasons, r => r.Code == SuggestionReasonCode.InsufficientRest);
    }

    [Fact]
    public void SeventhConsecutiveDay_IsIneligible()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var history = Enumerable.Range(3, 6) // Aug 3-8, six consecutive days
            .Select(day => Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, day), shiftType.StartTime, shiftType.EndTime))
            .ToList();

        var result = Suggest(new DateOnly(2026, 8, 9), shiftType, [employee], history: history);

        var suggestion = Assert.Single(result);
        Assert.False(suggestion.Eligible);
        Assert.Contains(suggestion.Reasons, r => r.Code == SuggestionReasonCode.TooManyConsecutiveDays);
    }

    [Fact]
    public void AlreadyAssignedThatDay_StaysEligibleButScoredDown()
    {
        // Far enough apart same-day that the 11h rest rule isn't also tripped, isolating the
        // AlreadyAssignedThatDay scoring from InsufficientRest.
        var employee = Employee();
        var shiftType = ShiftType(new TimeOnly(20, 0), new TimeOnly(23, 0));
        var otherShiftType = ShiftType(new TimeOnly(0, 0), new TimeOnly(4, 0));
        var history = new[]
        {
            Assignment(employee.Id, otherShiftType.Id, new DateOnly(2026, 8, 10), otherShiftType.StartTime, otherShiftType.EndTime),
        };

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [employee], history: history);

        var suggestion = Assert.Single(result);
        Assert.True(suggestion.Eligible); // ShiftOverlapValidator only Warns, so this shouldn't hard-exclude
        Assert.Equal(-3, suggestion.Score);
        Assert.Contains(suggestion.Reasons, r => r.Code == SuggestionReasonCode.AlreadyAssignedThatDay);
    }

    [Fact]
    public void PreferredShiftType_ScoresHigherThanAvoided()
    {
        var preferring = Employee("Anna", "Preferred");
        var avoiding = Employee("Ben", "Avoiding");
        var shiftType = ShiftType();
        var prefs = new[]
        {
            new ShiftTypePreference { Id = Guid.NewGuid(), EmployeeId = preferring.Id, ShiftTypeId = shiftType.Id, Level = PreferenceLevel.Preferred },
            new ShiftTypePreference { Id = Guid.NewGuid(), EmployeeId = avoiding.Id, ShiftTypeId = shiftType.Id, Level = PreferenceLevel.Avoid },
        };

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [preferring, avoiding], shiftTypePrefs: prefs);

        Assert.Equal(preferring.Id, result[0].EmployeeId); // ranked first
        Assert.Equal(2, result[0].Score);
        Assert.Equal(-2, result[1].Score);
    }

    [Fact]
    public void PreferredWeekday_AddsPositiveScore()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var monday = new DateOnly(2026, 8, 10); // a Monday
        var prefs = new[]
        {
            new WeekdayPreference { Id = Guid.NewGuid(), EmployeeId = employee.Id, DayOfWeek = DayOfWeek.Monday, Level = PreferenceLevel.Preferred },
        };

        var result = Suggest(monday, shiftType, [employee], weekdayPrefs: prefs);

        var suggestion = Assert.Single(result);
        Assert.Equal(1, suggestion.Score);
        Assert.Contains(suggestion.Reasons, r => r.Code == SuggestionReasonCode.WeekdayPreferred);
    }

    [Fact]
    public void UnderContractTarget_AddsPositiveScore()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        var contract = Contract(employee.Id, ScheduleStart, weeklyHours: 40);

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [employee], contracts: [contract]);

        var suggestion = Assert.Single(result);
        Assert.Equal(1, suggestion.Score);
        Assert.Contains(suggestion.Reasons, r => r.Code == SuggestionReasonCode.UnderContractTarget);
    }

    [Fact]
    public void AtOrOverContractTarget_NoUnderTargetBonus()
    {
        var employee = Employee();
        var shiftType = ShiftType();
        // 1 day's worth of hours already scaled to the full month's contract target
        var contract = Contract(employee.Id, ScheduleStart, weeklyHours: 0.01m);
        var scheduleAssignments = new[]
        {
            Assignment(employee.Id, shiftType.Id, new DateOnly(2026, 8, 1), shiftType.StartTime, shiftType.EndTime),
        };

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [employee], contracts: [contract], scheduleAssignments: scheduleAssignments);

        var suggestion = Assert.Single(result);
        Assert.DoesNotContain(suggestion.Reasons, r => r.Code == SuggestionReasonCode.UnderContractTarget);
    }

    [Fact]
    public void Results_OrderedByEligibleThenScoreDescending()
    {
        var ineligible = Employee("Ina", "Eligible");
        var otherShiftType = ShiftType();
        ineligible.EligibleShiftTypes.Add(otherShiftType);
        var lowScore = Employee("Lena", "LowScore");
        var highScore = Employee("Hana", "HighScore");
        var shiftType = ShiftType();
        var prefs = new[]
        {
            new ShiftTypePreference { Id = Guid.NewGuid(), EmployeeId = highScore.Id, ShiftTypeId = shiftType.Id, Level = PreferenceLevel.Preferred },
            new ShiftTypePreference { Id = Guid.NewGuid(), EmployeeId = lowScore.Id, ShiftTypeId = shiftType.Id, Level = PreferenceLevel.Avoid },
        };

        var result = Suggest(new DateOnly(2026, 8, 10), shiftType, [lowScore, ineligible, highScore], shiftTypePrefs: prefs);

        Assert.Equal([highScore.Id, lowScore.Id, ineligible.Id], result.Select(r => r.EmployeeId));
    }
}
