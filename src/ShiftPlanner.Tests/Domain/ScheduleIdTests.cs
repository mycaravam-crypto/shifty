using ShiftPlanner.Domain.Common;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #107 (finding M4): mirrors EmployeeIdTests for the sibling ScheduleId wrapper — see
// that file's comments for the rationale behind each case.
public class ScheduleIdTests
{
    [Fact]
    public void Equality_SameUnderlyingGuid_AreEqual()
    {
        var guid = Guid.NewGuid();
        var a = new ScheduleId(guid);
        var b = new ScheduleId(guid);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DifferentUnderlyingGuid_AreNotEqual()
    {
        var a = new ScheduleId(Guid.NewGuid());
        var b = new ScheduleId(Guid.NewGuid());

        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void GetHashCode_SameUnderlyingGuid_ProducesSameHash()
    {
        var guid = Guid.NewGuid();
        var a = new ScheduleId(guid);
        var b = new ScheduleId(guid);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void UsableAsDictionaryKey_ViaValueEquality()
    {
        var guid = Guid.NewGuid();
        var dict = new Dictionary<ScheduleId, string> { [new ScheduleId(guid)] = "august-2026" };

        Assert.Equal("august-2026", dict[new ScheduleId(guid)]);
    }

    [Fact]
    public void ImplicitConversion_FromGuid_RoundTrips()
    {
        var guid = Guid.NewGuid();
        ScheduleId id = guid; // implicit Guid -> ScheduleId

        Assert.Equal(guid, id.Value);
    }

    [Fact]
    public void ImplicitConversion_ToGuid_RoundTrips()
    {
        var id = new ScheduleId(Guid.NewGuid());
        Guid guid = id; // implicit ScheduleId -> Guid

        Assert.Equal(id.Value, guid);
    }

    [Fact]
    public void ImplicitConversion_PreservesEqualityAcrossTypes()
    {
        var guid = Guid.NewGuid();
        ScheduleId fromGuid = guid;

        Assert.Equal(new ScheduleId(guid), fromGuid);
    }

    [Fact]
    public void New_ProducesDistinctNonEmptyIds()
    {
        var a = ScheduleId.New();
        var b = ScheduleId.New();

        Assert.NotEqual(a, b);
        Assert.NotEqual(ScheduleId.Empty, a);
        Assert.NotEqual(ScheduleId.Empty, b);
    }

    [Fact]
    public void Empty_WrapsGuidEmpty()
    {
        Assert.Equal(Guid.Empty, ScheduleId.Empty.Value);
    }

    [Fact]
    public void ToString_MatchesUnderlyingGuidsToString()
    {
        var guid = Guid.NewGuid();
        var id = new ScheduleId(guid);

        Assert.Equal(guid.ToString(), id.ToString());
    }
}
