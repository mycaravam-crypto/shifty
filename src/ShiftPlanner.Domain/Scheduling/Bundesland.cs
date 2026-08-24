namespace ShiftPlanner.Domain.Scheduling;

// issue #57: the 16 German states — keys both Team.Bundesland (which state a team operates
// in) and GermanPublicHolidays' optional per-state additions on top of the 9 nationwide
// gesetzliche Feiertage.
public enum Bundesland
{
    BadenWuerttemberg,
    Bayern,
    Berlin,
    Brandenburg,
    Bremen,
    Hamburg,
    Hessen,
    MecklenburgVorpommern,
    Niedersachsen,
    NordrheinWestfalen,
    RheinlandPfalz,
    Saarland,
    Sachsen,
    SachsenAnhalt,
    SchleswigHolstein,
    Thueringen
}
