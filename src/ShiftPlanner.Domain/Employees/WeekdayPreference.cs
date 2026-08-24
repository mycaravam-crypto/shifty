namespace ShiftPlanner.Domain.Employees;

// Which weekdays an employee prefers or would rather avoid working. Feeds ShiftSuggestionEngine.
public class WeekdayPreference
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public PreferenceLevel Level { get; set; }
}
