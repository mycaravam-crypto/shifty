using ShiftPlanner.Domain.Contracts;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #70: Contract.ActiveOn/Overlaps centralize logic that used to be independently
// duplicated in five places (SchedulesController.HourlyRateOn, DashboardController's own
// ActiveContract, HoursBalanceCalculator, ShiftSuggestionEngine, ContractValidator).
// issue #116: Contract.ValidFrom/ValidTo previously had no invariant enforcement at the object
// level — these tests exercise the new Create factory / Validate method (ValidTo, if set, must
// not precede ValidFrom).
public class ContractTests
{
    private static Contract MakeContract(Guid employeeId, DateOnly validFrom, DateOnly? validTo = null) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = employeeId,
        ValidFrom = validFrom,
        ValidTo = validTo,
        WeeklyHours = 40m,
        WorkingDaysPerWeek = 5,
        DailyTargetHours = 8m,
    };

    [Fact]
    public void ActiveOn_PicksContractCoveringDate()
    {
        var employeeId = Guid.NewGuid();
        var earlier = MakeContract(employeeId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var later = MakeContract(employeeId, new DateOnly(2026, 7, 1));

        Assert.Equal(earlier.Id, Contract.ActiveOn([earlier, later], employeeId, new DateOnly(2026, 3, 1))!.Id);
        Assert.Equal(later.Id, Contract.ActiveOn([earlier, later], employeeId, new DateOnly(2026, 8, 1))!.Id);
    }

    [Fact]
    public void ActiveOn_NoContractCoveringDate_ReturnsNull()
    {
        var employeeId = Guid.NewGuid();
        var contract = MakeContract(employeeId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Null(Contract.ActiveOn([contract], employeeId, new DateOnly(2026, 1, 1)));
        Assert.Null(Contract.ActiveOn([contract], employeeId, new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void ActiveOn_IgnoresOtherEmployeesContracts()
    {
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var contract = MakeContract(otherEmployeeId, new DateOnly(2026, 1, 1));

        Assert.Null(Contract.ActiveOn([contract], employeeId, new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void ActiveOn_OpenEndedContract_CoversEveryFutureDate()
    {
        var employeeId = Guid.NewGuid();
        var contract = MakeContract(employeeId, new DateOnly(2026, 1, 1));

        Assert.NotNull(Contract.ActiveOn([contract], employeeId, new DateOnly(2099, 1, 1)));
    }

    [Fact]
    public void ActiveOn_TwoCandidates_PicksMostRecentlyStarting()
    {
        // Both cover the date (an overlap that shouldn't exist post-#70's ContractsController
        // check, but historical data predating it could still have one) — ActiveOn stays a
        // best-effort MaxBy(ValidFrom) rather than throwing.
        var employeeId = Guid.NewGuid();
        var older = MakeContract(employeeId, new DateOnly(2026, 1, 1));
        var newer = MakeContract(employeeId, new DateOnly(2026, 3, 1));

        Assert.Equal(newer.Id, Contract.ActiveOn([older, newer], employeeId, new DateOnly(2026, 6, 1))!.Id);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-06-30", "2026-07-01", null, false)] // adjacent, no overlap
    [InlineData("2026-01-01", "2026-06-30", "2026-06-30", null, true)] // shared boundary day
    [InlineData("2026-01-01", null, "2026-06-01", null, true)] // both open-ended
    [InlineData("2026-01-01", "2026-12-31", "2026-06-01", "2026-06-30", true)] // fully contains
    [InlineData("2026-01-01", "2026-03-31", "2026-06-01", "2026-08-31", false)] // disjoint
    public void Overlaps_DetectsRangeOverlap(string fromA, string? toA, string fromB, string? toB, bool expected)
    {
        var result = Contract.Overlaps(
            DateOnly.Parse(fromA), toA is null ? null : DateOnly.Parse(toA),
            DateOnly.Parse(fromB), toB is null ? null : DateOnly.Parse(toB));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Overlaps_IsSymmetric()
    {
        var a = (from: new DateOnly(2026, 1, 1), to: (DateOnly?)new DateOnly(2026, 6, 30));
        var b = (from: new DateOnly(2026, 6, 1), to: (DateOnly?)null);

        Assert.True(Contract.Overlaps(a.from, a.to, b.from, b.to));
        Assert.True(Contract.Overlaps(b.from, b.to, a.from, a.to));
    }

    [Fact]
    public void Create_SucceedsWhenValidToIsOnOrAfterValidFrom()
    {
        var contract = Contract.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            40m, 5, 8m, 15m);

        Assert.Equal(new DateOnly(2026, 1, 1), contract.ValidFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), contract.ValidTo);
    }

    [Fact]
    public void Create_SucceedsWhenValidToEqualsValidFrom()
    {
        var day = new DateOnly(2026, 6, 1);

        var contract = Contract.Create(Guid.NewGuid(), Guid.NewGuid(), day, day, 40m, 5, 8m, null);

        Assert.Equal(day, contract.ValidTo);
    }

    [Fact]
    public void Create_SucceedsWhenValidToIsNull()
    {
        var contract = Contract.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 40m, 5, 8m, null);

        Assert.Null(contract.ValidTo);
    }

    [Fact]
    public void Create_ThrowsWhenValidToPrecedesValidFrom()
    {
        var ex = Assert.Throws<ArgumentException>(() => Contract.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 31),
            40m, 5, 8m, null));

        Assert.Contains("ValidTo", ex.Message);
        Assert.Contains("ValidFrom", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsAfterMutatingValidToBeforeValidFrom()
    {
        var contract = Contract.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 40m, 5, 8m, null);

        contract.ValidTo = new DateOnly(2025, 12, 31);

        Assert.Throws<ArgumentException>(contract.Validate);
    }

    [Fact]
    public void Validate_DoesNotThrowForAValidRange()
    {
        var contract = Contract.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), null, 40m, 5, 8m, null);

        contract.ValidTo = new DateOnly(2026, 12, 31);

        contract.Validate(); // should not throw
    }
}
