namespace ShiftPlanner.Domain.Employees;

public class Team
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}
