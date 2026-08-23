using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftPlanner.Infrastructure.Persistence;

namespace ShiftPlanner.Api.Middleware;

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApplicationDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var provided) || string.IsNullOrWhiteSpace(provided))
            return AuthenticateResult.NoResult();

        var hashed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provided.ToString())));

        var apiKey = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.HashedKey == hashed && k.RevokedAt == null);

        if (apiKey is null)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, apiKey.Name),
            new Claim("ApiKeyScope", apiKey.Scope.ToString())
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
