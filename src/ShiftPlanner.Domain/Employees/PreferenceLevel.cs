namespace ShiftPlanner.Domain.Employees;

// Arbeitszeitpräferenzen (readme.md §17). Neutral isn't a stored value — the absence of a
// preference row for a given ShiftType/DayOfWeek already means neutral, so this only needs
// the two poles ShiftSuggestionEngine scores on.
public enum PreferenceLevel
{
    Avoid = -1,
    Preferred = 1,
}
