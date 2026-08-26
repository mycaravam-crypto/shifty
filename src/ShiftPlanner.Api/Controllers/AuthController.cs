using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShiftPlanner.Api.Authentication;
using ShiftPlanner.Domain.Common;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public record AccessTokenResponse(string AccessToken);

// Admin/Manager login only — readme.md §23 explicitly excludes Employee self-service in v1.
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public class AuthController(UserManager<ApplicationUser> userManager, ApplicationDbContext db, IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    private const string RefreshCookieName = "refreshToken";

    [HttpPost("login")]
    public async Task<ActionResult<AccessTokenResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized();

        var roles = await userManager.GetRolesAsync(user);
        var refreshToken = JwtTokenFactory.CreateRefreshToken(user, jwtOptions.Value);
        db.RefreshTokens.Add(JwtTokenFactory.CreateRefreshTokenRecord(refreshToken, user.Id));
        await db.SaveChangesAsync();

        SetRefreshCookie(refreshToken);
        return Ok(new AccessTokenResponse(JwtTokenFactory.CreateAccessToken(user, roles, jwtOptions.Value)));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AccessTokenResponse>> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken))
            return Unauthorized();

        // Both the JWT itself (signature/expiry/token_use claim) and its DB-backed row (exists,
        // not revoked, not expired) must check out — see JwtTokenFactory's own comment for why.
        var validated = await JwtTokenFactory.ValidateRefreshTokenAsync(refreshToken, jwtOptions.Value, db);
        if (validated is null)
            return Unauthorized();

        var user = await userManager.FindByIdAsync(validated.UserId);
        if (user is null)
            return Unauthorized();

        // Rotate on use: refresh tokens are single-use. The presented token's DB row is revoked
        // here and a brand-new token/row takes its place, rather than letting the same refresh
        // token stay valid to reuse for its whole 7-day lifetime. This caps the blast radius of
        // a stolen refresh token to one silent exchange — after that, both the legitimate client
        // and the attacker are holding a now-revoked token, and the next refresh attempt with it
        // fails loudly instead of quietly keeping a stolen session alive for a week.
        validated.Record.RevokedAt = DateTimeOffset.UtcNow;

        var roles = await userManager.GetRolesAsync(user);
        var newRefreshToken = JwtTokenFactory.CreateRefreshToken(user, jwtOptions.Value);
        db.RefreshTokens.Add(JwtTokenFactory.CreateRefreshTokenRecord(newRefreshToken, user.Id));
        await db.SaveChangesAsync();

        SetRefreshCookie(newRefreshToken);
        return Ok(new AccessTokenResponse(JwtTokenFactory.CreateAccessToken(user, roles, jwtOptions.Value)));
    }

    // Revokes the DB-backed row for *this* refresh token only ("log out this device"). Requires
    // a valid access token so the caller is known — the row looked up must belong to that same
    // user, so presenting someone else's refresh-token cookie value (which would require having
    // stolen it in the first place, since only its SHA-256 hash is ever stored) still can't be
    // used to revoke a different user's session under your own login.
    [HttpPost("logout")]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> Logout()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return Unauthorized();

        if (Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken))
        {
            var hash = RefreshToken.Hash(refreshToken);
            var record = await db.RefreshTokens
                .FirstOrDefaultAsync(r => r.TokenHash == hash && r.UserId == userId && r.RevokedAt == null);
            if (record is not null)
            {
                record.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        ClearRefreshCookie();
        return NoContent();
    }

    // "Log out other sessions": revokes every non-revoked RefreshToken row for the current user,
    // including the one behind this very request's cookie — there's no way to distinguish "this
    // session" from "other sessions" at the DB level beyond that, so logging out everywhere is
    // the simplest correct behavior for a compromised-device/employee-departure scenario.
    [HttpPost("logout-all")]
    [Authorize(Policy = "Staff")]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return Unauthorized();

        var records = await db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        foreach (var record in records)
            record.RevokedAt = now;

        await db.SaveChangesAsync();

        ClearRefreshCookie();
        return NoContent();
    }

    private void SetRefreshCookie(string token) =>
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
        });

    private void ClearRefreshCookie() =>
        Response.Cookies.Append(RefreshCookieName, "", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UnixEpoch,
        });
}
