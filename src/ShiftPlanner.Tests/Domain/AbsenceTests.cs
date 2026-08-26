using ShiftPlanner.Domain.Employees;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #116: Absence.From/To previously had no invariant enforcement at the object level —
// these tests exercise the new Create factory / Validate method (To must not precede From).
public class AbsenceTests
{
    [Fact]
    public void Create_SucceedsWhenToIsAfterFrom()
    {
        var absence = Absence.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27),
            AbsenceType.Vacation, null);

        Assert.Equal(new DateOnly(2026, 8, 20), absence.From);
        Assert.Equal(new DateOnly(2026, 8, 27), absence.To);
    }

    [Fact]
    public void Create_SucceedsWhenToEqualsFrom()
    {
        var day = new DateOnly(2026, 8, 20);

        var absence = Absence.Create(Guid.NewGuid(), Guid.NewGuid(), day, day, AbsenceType.Sick, "single day");

        Assert.Equal(day, absence.To);
    }

    [Fact]
    public void Create_ThrowsWhenToPrecedesFrom()
    {
        var ex = Assert.Throws<ArgumentException>(() => Absence.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 19),
            AbsenceType.Vacation, null));

        Assert.Contains("To", ex.Message);
        Assert.Contains("From", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsAfterMutatingToBeforeFrom()
    {
        var absence = Absence.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27),
            AbsenceType.Vacation, null);

        absence.To = new DateOnly(2026, 8, 1);

        Assert.Throws<ArgumentException>(absence.Validate);
    }

    [Fact]
    public void Validate_DoesNotThrowForAValidRange()
    {
        var absence = Absence.Create(
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 27),
            AbsenceType.Vacation, null);

        absence.To = new DateOnly(2026, 9, 1);

        absence.Validate(); // should not throw
    }
}
