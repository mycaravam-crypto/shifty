using ShiftPlanner.Domain.Employees;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #165: Create/Validate invariants — a Reason is required, HoursDelta must not be zero
// (a zero-delta "adjustment" wouldn't mean anything on the printed report).
public class HoursAdjustmentTests
{
    [Fact]
    public void Create_SucceedsWithAPositiveDeltaAndReason()
    {
        var adjustment = HoursAdjustment.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), 2.5m, "Korrektur Zeiterfassung",
            "admin@shifty.local", DateTimeOffset.UtcNow);

        Assert.Equal(2.5m, adjustment.HoursDelta);
        Assert.Equal("Korrektur Zeiterfassung", adjustment.Reason);
    }

    [Fact]
    public void Create_SucceedsWithANegativeDelta()
    {
        var adjustment = HoursAdjustment.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), -1.5m, "Zu viel gutgeschrieben",
            "admin@shifty.local", DateTimeOffset.UtcNow);

        Assert.Equal(-1.5m, adjustment.HoursDelta);
    }

    [Fact]
    public void Create_ThrowsWhenReasonIsBlank()
    {
        var ex = Assert.Throws<ArgumentException>(() => HoursAdjustment.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), 1m, "  ",
            "admin@shifty.local", DateTimeOffset.UtcNow));

        Assert.Contains("Reason", ex.Message);
    }

    [Fact]
    public void Create_ThrowsWhenHoursDeltaIsZero()
    {
        var ex = Assert.Throws<ArgumentException>(() => HoursAdjustment.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), 0m, "Kein Effekt",
            "admin@shifty.local", DateTimeOffset.UtcNow));

        Assert.Contains("HoursDelta", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsAfterMutatingReasonToBlank()
    {
        var adjustment = HoursAdjustment.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), 1m, "Ok",
            "admin@shifty.local", DateTimeOffset.UtcNow);

        adjustment.Reason = "";

        Assert.Throws<ArgumentException>(adjustment.Validate);
    }
}
