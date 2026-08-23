namespace ShiftPlanner.Domain.Common;

public class AuditLog
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
