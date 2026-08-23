namespace ShiftPlanner.Domain.Common;

public enum ApiKeyScope
{
    ReadOnly,
    ReadWrite
}

public class ApiKey
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string HashedKey { get; set; }
    public ApiKeyScope Scope { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null;
}
