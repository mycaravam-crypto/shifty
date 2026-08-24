using ShiftPlanner.Application.Suggestions;
using ShiftPlanner.Domain.Contracts;
using ShiftPlanner.Domain.Employees;
using ShiftPlanner.Domain.Scheduling;
using Xunit;
using static ShiftPlanner.Tests.Application.TestFactory;

namespace ShiftPlanner.Tests.Application;

// issue #63: AutoFill orchestrates repeated Suggest calls over every open (date, ShiftType)
// slot — these tests focus on the orchestration (which slots are open, one pick per slot,
// picks folded into subsequent scoring/eligibility) rather than re-testing Suggest's own
// scoring rules, already covered by ShiftSuggestionEngineTests.
public class ShiftSuggestionEngineAutoFillTests
{
    private static readonly DateOnly ScheduleStart = new(2026, 8, 1);
    private static readonly DateOnly ScheduleEnd = new(2026, 8, 31);

    private static List<AutoFillProposal> AutoFill(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        IReadOnlyList<ShiftType> shiftTypes,
        IReadOnlyList<Employee> employees,
        IReadOnlyList<ShiftAssignment>? scheduleAssignments = null,
        IReadOnlyList<ShiftAssignment>? history = null,
        IReadOnlyList<Absence>? absences = null,
        IReadOnlyList<Contract>? contracts = null,
        IReadOnlyList<ShiftTypePreference>? shiftTypePrefs = null,
        IReadOnlyList<WeekdayPreference>? weekdayPrefs = null) =>
        ShiftSuggestionEngine.AutoFill(
            rangeStart, rangeEnd, shiftTypes, employees,
            history ?? [], absences ?? [],
            ScheduleStart, ScheduleEnd,
            scheduleAssignments ?? [], contracts ?? [],
            shiftTypePrefs ?? [], weekdayPrefs ?? []);

    [Fact]
    public void OneOpenSlot_FillsWithTopEligibleCandidate()
    {
        var shiftType = ShiftType(minStaffing: 1);
        var preferring = Employee("Anna", "Preferred");
        var neutral = Employee("Ben", "Neutral");
        var prefs = new[]
        {
            new ShiftTypePreference { Id = Guid.NewGuid(), EmployeeId = preferring.Id, ShiftTypeId = shiftType.Id, Level = PreferenceLevel.Preferred },
        };
        var date = new DateOnly(2026, 8, 10);

        var proposals = AutoFill(date, date, [shiftType], [neutral, preferring], shiftTypePrefs: prefs);

        var proposal = Assert.Single(proposals);
        Assert.Equal(preferring.Id, proposal.EmployeeId);
        Assert.Equal(shiftType.Id, proposal.ShiftTypeId);
        Assert.Equal(date, proposal.Date);
        Assert.Equal(2, proposal.Score);
    }

    [Fact]
    public void NoEligibleCandidate_SlotIsSkipped()
    {
        var shiftType = ShiftType(minStaffing: 1);
        var otherShiftType = ShiftType();
        var employee = Employee();
        employee.AddEligibleShiftType(otherShiftType); // restricted away from `shiftType`
        var date = new DateOnly(2026, 8, 10);

        var proposals = AutoFill(date, date, [shiftType], [employee]);

        Assert.Empty(proposals);
    }

    [Fact]
    public void MinStaffingTwo_AssignsTwoDifferentEmployeesNotOneTwice()
    {
        var shiftType = ShiftType(minStaffing: 2);
        var a = Employee("Anna", "A");
        var b = Employee("Ben", "B");
        var date = new DateOnly(2026, 8, 10);

        var proposals = AutoFill(date, date, [shiftType], [a, b]);

        Assert.Equal(2, proposals.Count);
        Assert.Equal(2, proposals.Select(p => p.EmployeeId).Distinct().Count()); // not the same employee twice
        Assert.Equal(new HashSet<Guid> { a.Id, b.Id }, proposals.Select(p => p.EmployeeId).ToHashSet());
    }

    [Fact]
    public void MinStaffingTwo_OnlyOneEligibleCandidate_FillsOneAndSkipsSecond()
    {
        var shiftType = ShiftType(minStaffing: 2);
        var onlyCandidate = Employee();
        var date = new DateOnly(2026, 8, 10);

        var proposals = AutoFill(date, date, [shiftType], [onlyCandidate]);

        var proposal = Assert.Single(proposals);
        Assert.Equal(onlyCandidate.Id, proposal.EmployeeId);
    }

    [Fact]
    public void AlreadyStaffedSlot_NoProposal()
    {
        var shiftType = ShiftType(minStaffing: 1);
        var employee = Employee();
        var otherEmployee = Employee("Ben", "Already");
        var date = new DateOnly(2026, 8, 10);
        var existing = new[]
        {
            Assignment(otherEmployee.Id, shiftType.Id, date, shiftType.StartTime, shiftType.EndTime),
        };

        var proposals = AutoFill(date, date, [shiftType], [employee], scheduleAssignments: existing);

        Assert.Empty(proposals);
    }

    [Fact]
    public void EmptySchedule_NoShiftTypesWithMinStaffing_HasNoOpenSlots()
    {
        var shiftType = ShiftType(); // no MinStaffing set
        var employee = Employee();

        var proposals = AutoFill(ScheduleStart, ScheduleEnd, [shiftType], [employee]);

        Assert.Empty(proposals);
    }

    [Fact]
    public void PickForOneDay_CountsTowardAlreadyAssignedScoring_OnAdjacentSlotSameDay()
    {
        // Two different ShiftTypes, same day, both need staffing, one candidate — the pick
        // from the first slot should show up as "AlreadyAssignedThatDay" scoring for the
        // second, proving picks are folded into scoreAssignments across the run.
        // 11h gap exactly (10:00 -> 21:00) so InsufficientRest isn't also tripped — this test
        // isolates the AlreadyAssignedThatDay cross-slot scoring, not the rest-time rule.
        var morning = ShiftType(new TimeOnly(6, 0), new TimeOnly(10, 0), minStaffing: 1);
        var evening = ShiftType(new TimeOnly(21, 0), new TimeOnly(23, 0), minStaffing: 1);
        var employee = Employee();
        var date = new DateOnly(2026, 8, 10);

        // Ordered alphabetically by ShiftType.Name (both default to "Normal" from the
        // factory) so give them distinct names to make the fill order deterministic.
        morning.Name = "AMorning";
        evening.Name = "BEvening";

        var proposals = AutoFill(date, date, [morning, evening], [employee]);

        Assert.Equal(2, proposals.Count);
        var eveningProposal = proposals.Single(p => p.ShiftTypeId == evening.Id);
        Assert.Contains(eveningProposal.Reasons, r => r.Code == SuggestionReasonCode.AlreadyAssignedThatDay);
        Assert.Equal(-3, eveningProposal.Score);
    }

    [Fact]
    public void OpenSlotsAcrossMultipleDays_AreAllFilled()
    {
        var shiftType = ShiftType(minStaffing: 1);
        var employee = Employee();
        var start = new DateOnly(2026, 8, 10);
        var end = new DateOnly(2026, 8, 12);

        var proposals = AutoFill(start, end, [shiftType], [employee]);

        Assert.Equal(3, proposals.Count);
        Assert.Equal([start, start.AddDays(1), start.AddDays(2)], proposals.Select(p => p.Date));
    }
}
