namespace ShiftPlanner.Domain.Scheduling;

public record PublicHoliday(DateOnly Date, string Name);

// issue #15: gesetzliche Feiertage. Computed rather than seeded — most are rule-based (fixed
// calendar dates or Easter-relative), so this stays correct for any year with no yearly seed
// job, matching the codebase's existing "no persisted derived state" pattern (see
// HoursBalanceCalculator/WorkingTimeCalculator).
// First cut covers only the 9 nationwide holidays (readme.md has no §-reference for this
// feature, and per-Bundesland variation is real but adds 16 states' worth of rules with no
// current consumer needing that precision) — the state-specific ones (e.g. Fronleichnam,
// Reformationstag) aren't included yet.
public static class GermanPublicHolidays
{
    public static IReadOnlyList<PublicHoliday> InRange(DateOnly start, DateOnly end)
    {
        var holidays = new List<PublicHoliday>();
        for (var year = start.Year; year <= end.Year; year++)
            holidays.AddRange(ForYear(year));

        return holidays.Where(h => h.Date >= start && h.Date <= end).OrderBy(h => h.Date).ToList();
    }

    private static IEnumerable<PublicHoliday> ForYear(int year)
    {
        var easterSunday = EasterSunday(year);
        yield return new PublicHoliday(new DateOnly(year, 1, 1), "Neujahr");
        yield return new PublicHoliday(easterSunday.AddDays(-2), "Karfreitag");
        yield return new PublicHoliday(easterSunday.AddDays(1), "Ostermontag");
        yield return new PublicHoliday(new DateOnly(year, 5, 1), "Tag der Arbeit");
        yield return new PublicHoliday(easterSunday.AddDays(39), "Christi Himmelfahrt");
        yield return new PublicHoliday(easterSunday.AddDays(50), "Pfingstmontag");
        yield return new PublicHoliday(new DateOnly(year, 10, 3), "Tag der Deutschen Einheit");
        yield return new PublicHoliday(new DateOnly(year, 12, 25), "1. Weihnachtstag");
        yield return new PublicHoliday(new DateOnly(year, 12, 26), "2. Weihnachtstag");
    }

    // Gauss's Easter algorithm (Gregorian calendar).
    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
