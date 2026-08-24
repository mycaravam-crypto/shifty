using System.Security.Cryptography;
using System.Text;

namespace ShiftPlanner.Domain.Common;

// DB-backed record of an issued refresh token (see Api/Authentication/JwtTokenFactory.cs),
// closing the gap that refresh tokens were previously self-contained JWTs with no server-side
// revocation path (issue #55, the original ask behind issue #3). Only a SHA-256 hash of the
// token is ever stored — never the raw value — same pattern as ApiKey.HashedKey above.
public class RefreshToken
{
    public Guid Id { get; set; }

    // The Identity user id (ApplicationUser.Id, Infrastructure layer) this token was issued
    // to. A plain string FK, not a navigation property — same reason AuditLog.UserId is a
    // plain string: Domain can't reference Infrastructure's ApplicationUser type. The actual
    // FK constraint is configured in ApplicationDbContext.OnModelCreating.
    public required string UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt;

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
