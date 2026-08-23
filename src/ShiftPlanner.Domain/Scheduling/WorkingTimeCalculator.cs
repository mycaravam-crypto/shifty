namespace ShiftPlanner.Domain.Scheduling;

// Single source of truth for net worked hours — readme.md §14.
public static class WorkingTimeCalculator
{
    public static decimal NetHours(TimeOnly start, TimeOnly end, int breakMinutes)
    {
        var minutes = (end - start).TotalMinutes - breakMinutes;
        return (decimal)Math.Max(0, minutes) / 60m;
    }
}
