using ShiftPlanner.Domain.Common;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #55: server-side refresh-token revocation. The DB-lookup half of validation
// (JwtTokenFactory.ValidateRefreshTokenAsync) needs a live ApplicationDbContext and isn't
// covered here — these tests exercise the pure logic around it: hashing (never compare/store
// raw token values) and the active/expired/revoked state machine.
public class RefreshTokenTests
{
    [Fact]
    public void Hash_IsDeterministic()
    {
        Assert.Equal(RefreshToken.Hash("some-token-value"), RefreshToken.Hash("some-token-value"));
    }

    [Fact]
    public void Hash_DiffersForDifferentInput()
    {
        Assert.NotEqual(RefreshToken.Hash("token-a"), RefreshToken.Hash("token-b"));
    }

    [Fact]
    public void Hash_NeverReturnsTheRawToken()
    {
        var raw = "super-secret-refresh-token";
        Assert.NotEqual(raw, RefreshToken.Hash(raw));
    }

    [Fact]
    public void Hash_IsSha256HexEncoded()
    {
        // 32 bytes -> 64 hex chars, upper-case per Convert.ToHexString.
        var hash = RefreshToken.Hash("anything");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9A-F]{64}$", hash);
    }

    [Fact]
    public void IsActive_TrueWhenNotRevokedAndNotExpired()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            TokenHash = RefreshToken.Hash("t"),
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(6),
            RevokedAt = null,
        };

        Assert.True(token.IsActive);
    }

    [Fact]
    public void IsActive_FalseWhenRevoked()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            TokenHash = RefreshToken.Hash("t"),
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(6),
            RevokedAt = DateTimeOffset.UtcNow,
        };

        Assert.False(token.IsActive);
    }

    [Fact]
    public void IsActive_FalseWhenExpired()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            TokenHash = RefreshToken.Hash("t"),
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-8),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = null,
        };

        Assert.False(token.IsActive);
    }

    [Fact]
    public void IsActive_FalseWhenRevokedAndExpired()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            TokenHash = RefreshToken.Hash("t"),
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-8),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
            RevokedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };

        Assert.False(token.IsActive);
    }

    [Fact]
    public void IsActive_TrueRightUpToExpiry()
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            TokenHash = RefreshToken.Hash("t"),
            IssuedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1),
            RevokedAt = null,
        };

        Assert.True(token.IsActive);
    }
}
