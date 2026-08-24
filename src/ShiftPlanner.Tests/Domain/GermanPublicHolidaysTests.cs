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

    // issue #57: passing null (or omitting the parameter) must reproduce the original
    // 9-nationwide-holiday-only behavior exactly — a regression check for every existing
    // caller that predates the Bundesland parameter.
    [Fact]
    public void InRange_NullBundesland_ReturnsExactlyTheNineNationwideHolidays()
    {
        var withDefault = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var withExplicitNull = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null);

        Assert.Equal(9, withDefault.Count);
        Assert.Equal(withDefault.Select(h => (h.Date, h.Name)), withExplicitNull.Select(h => (h.Date, h.Name)));
    }

    [Theory]
    [InlineData(Bundesland.BadenWuerttemberg)]
    [InlineData(Bundesland.Bayern)]
    [InlineData(Bundesland.SachsenAnhalt)]
    public void InRange_HeiligeDreiKoenige_AppearsOnJan6InTheRightStates(Bundesland land)
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), land);
        var match = Assert.Single(holidays, h => h.Name == "Heilige Drei Könige");
        Assert.Equal(new DateOnly(2026, 1, 6), match.Date);
    }

    [Fact]
    public void InRange_HeiligeDreiKoenige_AbsentInOtherStates()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), Bundesland.Berlin);
        Assert.DoesNotContain(holidays, h => h.Name == "Heilige Drei Könige");
    }

    [Theory]
    [InlineData(Bundesland.BadenWuerttemberg)]
    [InlineData(Bundesland.Bayern)]
    [InlineData(Bundesland.Hessen)]
    [InlineData(Bundesland.NordrheinWestfalen)]
    [InlineData(Bundesland.RheinlandPfalz)]
    [InlineData(Bundesland.Saarland)]
    public void InRange_Fronleichnam_60DaysAfterEasterInTheRightStates(Bundesland land)
    {
        // Easter Sunday 2026 is 2026-04-05 (cross-checked against the Easter-relative test
        // above); Fronleichnam is 60 days later.
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), land);
        var match = Assert.Single(holidays, h => h.Name == "Fronleichnam");
        Assert.Equal(new DateOnly(2026, 6, 4), match.Date);
    }

    [Theory]
    [InlineData(Bundesland.Sachsen)]
    [InlineData(Bundesland.Thueringen)]
    [InlineData(Bundesland.Berlin)]
    public void InRange_Fronleichnam_AbsentInStatesWithoutAStatewideHoliday(Bundesland land)
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), land);
        Assert.DoesNotContain(holidays, h => h.Name == "Fronleichnam");
    }

    [Theory]
    [InlineData(Bundesland.Brandenburg)]
    [InlineData(Bundesland.Bremen)]
    [InlineData(Bundesland.Hamburg)]
    [InlineData(Bundesland.MecklenburgVorpommern)]
    [InlineData(Bundesland.Niedersachsen)]
    [InlineData(Bundesland.Sachsen)]
    [InlineData(Bundesland.SachsenAnhalt)]
    [InlineData(Bundesland.SchleswigHolstein)]
    [InlineData(Bundesland.Thueringen)]
    public void InRange_Reformationstag_OnOct31InTheRightStates(Bundesland land)
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), land);
        var match = Assert.Single(holidays, h => h.Name == "Reformationstag");
        Assert.Equal(new DateOnly(2026, 10, 31), match.Date);
    }

    [Fact]
    public void InRange_Reformationstag_AbsentInBavaria()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Bundesland.Bayern);
        Assert.DoesNotContain(holidays, h => h.Name == "Reformationstag");
    }

    [Theory]
    [InlineData(Bundesland.BadenWuerttemberg)]
    [InlineData(Bundesland.Bayern)]
    [InlineData(Bundesland.NordrheinWestfalen)]
    [InlineData(Bundesland.RheinlandPfalz)]
    [InlineData(Bundesland.Saarland)]
    public void InRange_Allerheiligen_OnNov1InTheRightStates(Bundesland land)
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), land);
        var match = Assert.Single(holidays, h => h.Name == "Allerheiligen");
        Assert.Equal(new DateOnly(2026, 11, 1), match.Date);
    }

    [Fact]
    public void InRange_Allerheiligen_AbsentInSachsen()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), Bundesland.Sachsen);
        Assert.DoesNotContain(holidays, h => h.Name == "Allerheiligen");
    }

    [Fact]
    public void InRange_InternationalerFrauentag_OnMar8InBerlinOnly()
    {
        var berlin = GermanPublicHolidays.InRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), Bundesland.Berlin);
        var match = Assert.Single(berlin, h => h.Name == "Internationaler Frauentag");
        Assert.Equal(new DateOnly(2026, 3, 8), match.Date);

        var bayern = GermanPublicHolidays.InRange(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), Bundesland.Bayern);
        Assert.DoesNotContain(bayern, h => h.Name == "Internationaler Frauentag");
    }

    [Fact]
    public void InRange_BussUndBettag_SachsenOnly_2026IsNov18()
    {
        // Nov 23 2026 is a Monday; Buß- und Bettag is the Wednesday strictly before it.
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), Bundesland.Sachsen);
        var match = Assert.Single(holidays, h => h.Name == "Buß- und Bettag");
        Assert.Equal(new DateOnly(2026, 11, 18), match.Date);
        Assert.Equal(DayOfWeek.Wednesday, match.Date.DayOfWeek);
    }

    [Fact]
    public void InRange_BussUndBettag_2022WhenNov23IsAWednesday_FallsOneWeekEarlier()
    {
        // Nov 23 2022 is itself a Wednesday (verified: date(2022,11,23).weekday()), and the
        // real-world observed Buß- und Bettag that year was Nov 16 — the Wednesday strictly
        // before Nov 23, one full week earlier, not that day itself.
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2022, 11, 1), new DateOnly(2022, 11, 30), Bundesland.Sachsen);
        var match = Assert.Single(holidays, h => h.Name == "Buß- und Bettag");
        Assert.Equal(new DateOnly(2022, 11, 16), match.Date);
    }

    [Fact]
    public void InRange_BussUndBettag_AbsentOutsideSachsen()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), Bundesland.SachsenAnhalt);
        Assert.DoesNotContain(holidays, h => h.Name == "Buß- und Bettag");
    }

    [Fact]
    public void InRange_WithBundesland_StillIncludesAllNineNationwideHolidays()
    {
        var holidays = GermanPublicHolidays.InRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Bundesland.Bayern);
        Assert.Contains(holidays, h => h.Name == "Neujahr");
        Assert.Contains(holidays, h => h.Name == "1. Weihnachtstag");
        Assert.True(holidays.Count > 9, "Bayern should have more than the 9 nationwide holidays.");
    }
}
