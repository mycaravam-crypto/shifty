using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShiftPlanner.Api.Authentication;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Controllers;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public record AccessTokenResponse(string AccessToken);

// Admin/Manager login only — readme.md §23 explicitly excludes Employee self-service in v1.
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public class AuthController(UserManager<ApplicationUser> userManager, IConfiguration config) : ControllerBase
{
    private const string RefreshCookieName = "refreshToken";

    [HttpPost("login")]
    public async Task<ActionResult<AccessTokenResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized();

        var roles = await userManager.GetRolesAsync(user);
        SetRefreshCookie(JwtTokenFactory.CreateRefreshToken(user, config));
        return Ok(new AccessTokenResponse(JwtTokenFactory.CreateAccessToken(user, roles, config)));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AccessTokenResponse>> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken))
            return Unauthorized();

        var principal = JwtTokenFactory.ValidateRefreshToken(refreshToken, config);
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return Unauthorized();

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Unauthorized();

        var roles = await userManager.GetRolesAsync(user);
        SetRefreshCookie(JwtTokenFactory.CreateRefreshToken(user, config));
        return Ok(new AccessTokenResponse(JwtTokenFactory.CreateAccessToken(user, roles, config)));
    }

    private void SetRefreshCookie(string token) =>
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
        });
}
