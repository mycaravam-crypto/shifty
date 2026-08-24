using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ShiftPlanner.Api.Authentication;
using ShiftPlanner.Api.Authorization;
using ShiftPlanner.Api.Middleware;
using ShiftPlanner.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default (set via env var).");

// issue #111: the "Jwt" config section is bound once into a strongly-typed JwtOptions instead of
// three independent IConfiguration["Jwt:..."] indexer reads scattered across this file and
// JwtTokenFactory — a typo in a key name now fails to compile against JwtOptions' properties
// rather than silently reading null at runtime.
//
// AddJwtBearer's TokenValidationParameters below are needed while *building* the DI container
// (before builder.Build() runs, so before any IOptions<T> can be resolved) — so this reads
// directly off builder.Configuration here, using the same "Jwt" section/key names JwtOptions
// itself binds from via services.AddOptions<JwtOptions>() further down, and the same
// JwtOptionsValidator so a missing/blank key fails exactly as loudly here as it would via
// IOptions<JwtOptions> elsewhere (e.g. in JwtTokenFactory via AuthController).
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var jwtValidationResult = new JwtOptionsValidator().Validate(null, jwtOptions);
if (jwtValidationResult.Failed)
    throw new InvalidOperationException(string.Join(" ", jwtValidationResult.Failures));

builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 12;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };
        // Access and refresh tokens are both self-contained JWTs (JwtTokenFactory) distinguished
        // only by a "token_use" claim — without this check, a refresh token satisfies the same
        // signature/issuer/audience/lifetime checks as an access token and would work as a Bearer
        // token against every protected endpoint (issue #71).
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var tokenUse = context.Principal?.FindFirstValue("token_use");
                if (tokenUse != "access")
                    context.Fail("Token is not an access token.");
                return Task.CompletedTask;
            },
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

// The DI-bound counterpart of the early jwtOptions binding above — same "Jwt" section/key
// names, resolved as IOptions<JwtOptions> by AuthController/JwtTokenFactory instead of reading
// IConfiguration directly. ValidateOnStart() re-runs JwtOptionsValidator (registered as
// IValidateOptions<JwtOptions> so both binding paths share the same check) against this
// binding too, once the host starts — belt-and-suspenders with the throw above, which already
// covers the earliest possible failure point.
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateOnStart();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Staff", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());

    options.AddPolicy("ExternalApi", policy => policy
        .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser());

    options.AddPolicy("ApiRead", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser());

    // readme.md §23: Admin covers Stammdaten (Team/ShiftType) + Benutzer/API-Keys; Manager
    // covers Planung/Mitarbeiter (Employee/Contract) — and, since roles aren't hierarchical
    // data but Admin is still "manages everything", Admin can do Manager's writes too.
    options.AddPolicy("AdminWrite", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .AddRequirements(new ApiWriteRequirement("Admin")));

    options.AddPolicy("ManagerWrite", policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .AddRequirements(new ApiWriteRequirement("Admin", "Manager")));
});
builder.Services.AddSingleton<IAuthorizationHandler, ApiWriteRequirementHandler>();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", limiterOptions =>
    {
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.PermitLimit = 10;
    });
});

var frontendOrigin = builder.Configuration["Cors:FrontendOrigin"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (!string.IsNullOrWhiteSpace(frontendOrigin))
            policy.WithOrigins(frontendOrigin).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.Migrate();

    // Three fixed roles, readme.md §23 — seeded here since this is already the
    // "prepare the database" step every deploy runs.
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "Manager", "Employee" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
    return;
}

if (args.Contains("--seed-user"))
{
    // Bootstraps a Staff account (Admin or Manager) from env vars — there's no Benutzer
    // management endpoint yet (readme.md §23 gives that job to Admin, but the very first
    // account has no Admin to be created by, and nothing else creates a Manager either).
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var email = builder.Configuration["SeedUser:Email"]
        ?? throw new InvalidOperationException("Missing SeedUser:Email (set via env var).");
    var password = builder.Configuration["SeedUser:Password"]
        ?? throw new InvalidOperationException("Missing SeedUser:Password (set via env var).");
    var role = builder.Configuration["SeedUser:Role"]
        ?? throw new InvalidOperationException("Missing SeedUser:Role (Admin or Manager, set via env var).");

    if (await userManager.FindByEmailAsync(email) is null)
    {
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, role);
    }
    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHsts();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
