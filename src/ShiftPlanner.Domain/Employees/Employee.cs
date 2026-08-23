namespace ShiftPlanner.Domain.Employees;

public class Employee
{
    public Guid Id { get; set; }
    public required string PersonnelNumber { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public bool Active { get; set; } = true;
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }
}
