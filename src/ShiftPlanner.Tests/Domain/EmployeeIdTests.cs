using ShiftPlanner.Domain.Common;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #107 (finding M4): coverage for the new EmployeeId wrapper itself — equality/hashing
// (a `readonly record struct` gets these for free from the compiler, but the whole point of
// introducing the type is that callers can rely on it, so it's worth pinning down explicitly)
// and the Guid conversions that let it drop into existing Guid-typed call sites.
public class EmployeeIdTests
{
    [Fact]
    public void Equality_SameUnderlyingGuid_AreEqual()
    {
        var guid = Guid.NewGuid();
        var a = new EmployeeId(guid);
        var b = new EmployeeId(guid);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DifferentUnderlyingGuid_AreNotEqual()
    {
        var a = new EmployeeId(Guid.NewGuid());
        var b = new EmployeeId(Guid.NewGuid());

        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_SameUnderlyingGuid_ProducesSameHash()
    {
        var guid = Guid.NewGuid();
        var a = new EmployeeId(guid);
        var b = new EmployeeId(guid);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void UsableAsDictionaryKey_ViaValueEquality()
    {
        var guid = Guid.NewGuid();
        var dict = new Dictionary<EmployeeId, string> { [new EmployeeId(guid)] = "anna" };

        Assert.Equal("anna", dict[new EmployeeId(guid)]);
    }

    [Fact]
    public void ImplicitConversion_FromGuid_RoundTrips()
    {
        var guid = Guid.NewGuid();
        EmployeeId id = guid; // implicit Guid -> EmployeeId

        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void ImplicitConversion_ToGuid_RoundTrips()
    {
        var id = new EmployeeId(Guid.NewGuid());
        Guid guid = id; // implicit EmployeeId -> Guid

        Assert.Equal(id.Value, guid);
    }

    [Fact]
    public void ImplicitConversion_PreservesEqualityAcrossTypes()
    {
        var guid = Guid.NewGuid();
        EmployeeId fromGuid = guid;

        Assert.Equal(new EmployeeId(guid), fromGuid);
    }

    [Fact]
    public void New_ProducesDistinctNonEmptyIds()
    {
        var a = EmployeeId.New();
        var b = EmployeeId.New();

        Assert.NotEqual(a, b);
        Assert.NotEqual(EmployeeId.Empty, a);
        Assert.NotEqual(EmployeeId.Empty, b);
    }

    [Fact]
    public void Empty_WrapsGuidEmpty()
    {
        Assert.Equal(Guid.Empty, EmployeeId.Empty.Value);
    }

    [Fact]
    public void ToString_MatchesUnderlyingGuidsToString()
    {
        var guid = Guid.NewGuid();
        var id = new EmployeeId(guid);

        Assert.Equal(guid.ToString(), id.ToString());
    }

    [Fact]
    public void DistinctFromScheduleId_NotInterchangeableAtCompileTime()
    {
        // Documents the actual point of the wrapper: two EmployeeId values built from the
        // same Guid as a ScheduleId are unrelated types, so a swapped argument that used to
        // compile silently now can't — this test only demonstrates the value-level side
        // (they're allowed to wrap equal Guids and still not be the same type), the
        // compile-time distinction itself is enforced by the type system, not runtime-testable.
        var guid = Guid.NewGuid();
        var employeeId = new EmployeeId(guid);
        var scheduleId = new ScheduleId(guid);

        Assert.Equal(guid, employeeId.Value);
        Assert.Equal(guid, scheduleId.Value);
    }
}
