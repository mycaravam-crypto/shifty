using ShiftPlanner.Domain.Contracts;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #116: Contract.ValidFrom/ValidTo previously had no invariant enforcement at the object
// level — these tests exercise the new Create factory / Validate method (ValidTo, if set, must
// not precede ValidFrom).
public class ContractTests
{
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
