using Microsoft.Extensions.Options;

namespace ShiftPlanner.Api.Authentication;

// Binds the "Jwt" config section (SigningKey/Issuer/Audience) — see issue #111. Before this,
// all three keys were read via the IConfiguration indexer independently in Program.cs and
// JwtTokenFactory, so a typo in any one (e.g. "Jwt:SigninKey") wouldn't be caught at compile
// time and would silently read null instead. Centralizing the key NAMES here means a typo is
// instead caught at startup via JwtOptionsValidator + ValidateOnStart() (see Program.cs).
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
}

// A manual IValidateOptions<T> implementation rather than DataAnnotations attributes +
// AddOptions<T>().ValidateDataAnnotations() — IValidateOptions/ValidateOnStart already ship as
// part of the ASP.NET Core shared framework this Sdk.Web project references, so this avoids
// adding a Microsoft.Extensions.Options.DataAnnotations package dependency just for three
// required-string checks. Also reused directly from Program.cs's early-bootstrap binding (see
// below) so a missing/blank key fails exactly as loudly there as it does via
// IOptions<JwtOptions> elsewhere.
public class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.SigningKey)) missing.Add(nameof(options.SigningKey));
        if (string.IsNullOrWhiteSpace(options.Issuer)) missing.Add(nameof(options.Issuer));
        if (string.IsNullOrWhiteSpace(options.Audience)) missing.Add(nameof(options.Audience));

        return missing.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Missing required configuration: {string.Join(", ", missing.Select(m => $"{JwtOptions.SectionName}:{m}"))} (set via env var).");
    }
}
