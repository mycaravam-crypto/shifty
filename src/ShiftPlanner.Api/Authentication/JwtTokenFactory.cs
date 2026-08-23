using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Authentication;

// Issues the two JWTs described in readme.md §23: a short-lived access token (roles embedded,
// sent as Authorization: Bearer) and a longer-lived refresh token (httpOnly cookie, distinguished
// by a "token_use" claim so one can't be used in place of the other).
//
// ponytail: refresh tokens are self-contained JWTs, not DB-backed, so there's no server-side
// revocation (logout / password change can't invalidate one before it expires). Upgrade path:
// a RefreshToken table (UserId, TokenHash, ExpiresAt, RevokedAt) once that's actually needed.
public static class JwtTokenFactory
{
    private const string TokenUseClaim = "token_use";

    public static string CreateAccessToken(ApplicationUser user, IEnumerable<string> roles, IConfiguration config) =>
        Create(user, config, TimeSpan.FromMinutes(15), [new Claim(TokenUseClaim, "access"), .. roles.Select(r => new Claim(ClaimTypes.Role, r))]);

    public static string CreateRefreshToken(ApplicationUser user, IConfiguration config) =>
        Create(user, config, TimeSpan.FromDays(7), [new Claim(TokenUseClaim, "refresh")]);

    public static ClaimsPrincipal? ValidateRefreshToken(string token, IConfiguration config)
    {
        var principal = Validate(token, config);
        return principal?.FindFirstValue(TokenUseClaim) == "refresh" ? principal : null;
    }

    private static string Create(ApplicationUser user, IConfiguration config, TimeSpan lifetime, IEnumerable<Claim> extraClaims)
    {
        var key = config["Jwt:SigningKey"] ?? throw new InvalidOperationException("Missing Jwt:SigningKey.");
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.Email ?? user.UserName ?? user.Id),
        };
        claims.AddRange(extraClaims);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ClaimsPrincipal? Validate(string token, IConfiguration config)
    {
        var key = config["Jwt:SigningKey"] ?? throw new InvalidOperationException("Missing Jwt:SigningKey.");
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = config["Jwt:Issuer"],
            ValidAudience = config["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
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
