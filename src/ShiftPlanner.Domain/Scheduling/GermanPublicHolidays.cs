namespace ShiftPlanner.Domain.Scheduling;

public record PublicHoliday(DateOnly Date, string Name);

// issue #15: gesetzliche Feiertage. Computed rather than seeded — most are rule-based (fixed
// calendar dates or Easter-relative), so this stays correct for any year with no yearly seed
// job, matching the codebase's existing "no persisted derived state" pattern (see
// HoursBalanceCalculator/WorkingTimeCalculator).
// issue #57: extended with the standard per-Bundesland additional holidays, gated behind an
// optional `Bundesland?` parameter that defaults to null — every existing caller that doesn't
// pass one reproduces the original 9-nationwide-holiday-only behavior exactly (see the
// regression test in GermanPublicHolidaysTests). First cut covers the widely-agreed-on
// standard list: Heilige Drei Könige, Fronleichnam, Reformationstag, Allerheiligen, Buß- und
// Bettag (Sachsen only, still observed there after most states dropped it in 1995), and
// Internationaler Frauentag (Berlin only, per the issue's own conservative scoping —
// Mecklenburg-Vorpommern added it too starting 2023, but that's left out of this first cut
// rather than guessing beyond what the issue named). Fronleichnam is included only for the
// six states where it's a full state-wide holiday (BW, BY, HE, NW, RP, SL) — it's also
// observed in parts of Sachsen and Thüringen but only in specific (mostly Catholic)
// municipalities, which this per-Bundesland (not per-Gemeinde) model can't represent, so
// those two are deliberately left out for Fronleichnam specifically. Ostersonntag and
// Pfingstsonntag are always Sundays already, so they aren't separately modeled.
public static class GermanPublicHolidays
{
    public static IReadOnlyList<PublicHoliday> InRange(DateOnly start, DateOnly end, Bundesland? bundesland = null)
    {
        var holidays = new List<PublicHoliday>();
        for (var year = start.Year; year <= end.Year; year++)
            holidays.AddRange(ForYear(year, bundesland));

        return holidays.Where(h => h.Date >= start && h.Date <= end).OrderBy(h => h.Date).ToList();
    }

    private static IEnumerable<PublicHoliday> ForYear(int year, Bundesland? bundesland)
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

        if (bundesland is not { } land)
            yield break;

        if (land is Bundesland.BadenWuerttemberg or Bundesland.Bayern or Bundesland.SachsenAnhalt)
            yield return new PublicHoliday(new DateOnly(year, 1, 6), "Heilige Drei Könige");

        if (land is Bundesland.BadenWuerttemberg or Bundesland.Bayern or Bundesland.Hessen
            or Bundesland.NordrheinWestfalen or Bundesland.RheinlandPfalz or Bundesland.Saarland)
            yield return new PublicHoliday(easterSunday.AddDays(60), "Fronleichnam");

        if (land == Bundesland.Berlin)
            yield return new PublicHoliday(new DateOnly(year, 3, 8), "Internationaler Frauentag");

        if (land is Bundesland.BadenWuerttemberg or Bundesland.Bayern or Bundesland.NordrheinWestfalen
            or Bundesland.RheinlandPfalz or Bundesland.Saarland)
            yield return new PublicHoliday(new DateOnly(year, 11, 1), "Allerheiligen");

        if (land is Bundesland.Brandenburg or Bundesland.Bremen or Bundesland.Hamburg
            or Bundesland.MecklenburgVorpommern or Bundesland.Niedersachsen or Bundesland.Sachsen
            or Bundesland.SachsenAnhalt or Bundesland.SchleswigHolstein or Bundesland.Thueringen)
            yield return new PublicHoliday(new DateOnly(year, 10, 31), "Reformationstag");

        if (land == Bundesland.Sachsen)
            yield return new PublicHoliday(BussUndBettag(year), "Buß- und Bettag");
    }

    // Buß- und Bettag: the Wednesday strictly before Nov 23 (if Nov 23 itself is a Wednesday,
    // the holiday is the Wednesday one week earlier, not that day) — equivalently, 11 days
    // before the fourth Sunday before Christmas (the first Advent Sunday).
    private static DateOnly BussUndBettag(int year)
    {
        var nov23 = new DateOnly(year, 11, 23);
        var diff = ((int)nov23.DayOfWeek - (int)DayOfWeek.Wednesday + 7) % 7;
        if (diff == 0) diff = 7;
        return nov23.AddDays(-diff);
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
