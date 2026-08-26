using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShiftPlanner.Domain.Common;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Authentication;

// Issues the two JWTs described in readme.md §23: a short-lived access token (roles embedded,
// sent as Authorization: Bearer) and a longer-lived refresh token (httpOnly cookie, distinguished
// by a "token_use" claim so one can't be used in place of the other).
//
// issue #55: refresh tokens are still self-contained JWTs (so a syntactically/cryptographically
// valid, unexpired one is always accepted on that axis alone), but each one is now *also*
// tracked as a RefreshToken row keyed by a SHA-256 hash of the token. ValidateRefreshTokenAsync
// requires BOTH the JWT check and a live, non-revoked, non-expired DB row to pass, which is
// what makes server-side revocation (logout / logout-all) possible.
public static class JwtTokenFactory
{
    private const string TokenUseClaim = "token_use";
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public static string CreateAccessToken(ApplicationUser user, IEnumerable<string> roles, JwtOptions options) =>
        Create(user, options, TimeSpan.FromMinutes(15), [new Claim(TokenUseClaim, "access"), .. roles.Select(r => new Claim(ClaimTypes.Role, r))]);

    public static string CreateRefreshToken(ApplicationUser user, JwtOptions options) =>
        Create(user, options, RefreshTokenLifetime, [new Claim(TokenUseClaim, "refresh")]);

    // Builds the DB-backed row for a refresh token just issued via CreateRefreshToken above —
    // caller is responsible for adding it to the DbContext and saving. Kept as a separate step
    // (rather than folded into CreateRefreshToken) so callers can choose whether/when to persist,
    // same "no DI service layer" shape as the rest of the codebase.
    public static RefreshToken CreateRefreshTokenRecord(string refreshToken, string userId)
    {
        var now = DateTimeOffset.UtcNow;
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = RefreshToken.Hash(refreshToken),
            IssuedAt = now,
            ExpiresAt = now.Add(RefreshTokenLifetime),
        };
    }

    public static ClaimsPrincipal? ValidateRefreshToken(string token, JwtOptions options)
    {
        var principal = Validate(token, options);
        return principal?.FindFirstValue(TokenUseClaim) == "refresh" ? principal : null;
    }

    // Full refresh-token validation: JWT signature/expiry/claims AND a matching, still-active
    // (not revoked, not expired) DB row for the same user. Both must pass. Does not revoke or
    // rotate the row itself — that's the caller's decision (see AuthController.Refresh/Logout).
    public static async Task<RefreshTokenValidationResult?> ValidateRefreshTokenAsync(
        string token, JwtOptions options, ApplicationDbContext db)
    {
        var principal = ValidateRefreshToken(token, options);
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return null;

        var hash = RefreshToken.Hash(token);
        var record = await db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == hash && r.UserId == userId);

        return record is not null && record.IsActive ? new RefreshTokenValidationResult(userId, record) : null;
    }

    private static string Create(ApplicationUser user, JwtOptions options, TimeSpan lifetime, IEnumerable<Claim> extraClaims)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Email ?? user.UserName ?? user.Id),
            // issue #75's integration-test pass surfaced this as a real bug, not just a test
            // artifact: JwtSecurityToken has no other source of entropy — `exp`/`iat` are both
            // second-granularity DateTime.UtcNow, so two tokens issued to the same user with the
            // same roles within the same UTC second (e.g. two rapid logins, a double-clicked
            // login button, two browser tabs) were byte-for-byte identical. Refresh tokens hash
            // that byte string as their DB primary lookup key (RefreshToken.TokenHash, unique
            // index since issue #55) — a same-second collision meant the second login's
            // INSERT threw a raw 500 (unique constraint violation) instead of succeeding. A
            // per-token jti guarantees a distinct token (and hash) regardless of timing.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(extraClaims);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ClaimsPrincipal? Validate(string token, JwtOptions options)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        };

        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
        }
        // SecurityTokenException covers invalid/expired/bad-signature tokens; a token that
        // isn't even JWT-shaped (e.g. a tampered or garbage cookie value) throws
        // SecurityTokenMalformedException, which actually derives from ArgumentException.
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }
}

public record RefreshTokenValidationResult(string UserId, RefreshToken Record);
