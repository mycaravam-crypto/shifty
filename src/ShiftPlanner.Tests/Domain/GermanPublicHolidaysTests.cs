using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

public class GermanPublicHolidaysTests
{
    // Known Easter Sundays, cross-checked by hand: 2024-03-31, 2025-04-20, 2026-04-05, 2027-03-28.
    [Theory]
    [InlineData(2024, "2024-03-29", "Karfreitag")]
    [InlineData(2024, "2024-04-01", "Ostermontag")]
    [InlineData(2025, "2025-04-18", "Karfreitag")]
    [InlineData(2025, "2025-04-21", "Ostermontag")]
    [InlineData(2026, "2026-04-03", "Karfreitag")]
    [InlineData(2026, "2026-04-06", "Ostermontag")]
    [InlineData(2027, "2027-03-26", "Karfreitag")]
    [InlineData(2027, "2027-03-29", "Ostermontag")]
    public void InRange_EasterRelativeHolidays_MatchKnownDates(int year, string expectedDate, string name)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var holidays = GermanPublicHolidays.InRange(start, end);

        var match = Assert.Single(holidays, h => h.Name == name);
        Assert.Equal(DateOnly.Parse(expectedDate), match.Date);
    }

    [Fact]
    public void InRange_ReturnsAllNineNationwideHolidaysForFullYear()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.Equal(9, holidays.Count);
    }

    [Fact]
    public void InRange_NoHolidayInPlainAugustRange()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        Assert.Empty(holidays);
    }

    [Fact]
    public void InRange_CrossesYearBoundary_IncludesChristmasAndNewYears()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 5));
        Assert.Contains(holidays, h => h.Name == "1. Weihnachtstag");
        Assert.Contains(holidays, h => h.Name == "2. Weihnachtstag");
        Assert.Contains(holidays, h => h.Name == "Neujahr" && h.Date.Year == 2027);
    }

    [Fact]
    public void InRange_IsOrderedByDate()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var dates = holidays.Select(h => h.Date).ToList();
        Assert.Equal(dates.OrderBy(d => d), dates);
    }
}
