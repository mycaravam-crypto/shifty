using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShiftPlanner.Api.Authorization;
using ShiftPlanner.Api.Middleware;
using ShiftPlanner.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default (set via env var).");
var jwtKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Missing Jwt:SigningKey (set via env var).");

builder.Services.AddHttpContextAccessor();
// AuditSaveChangesInterceptor is stateless over the request-scoped IHttpContextAccessor (itself
// a singleton backed by AsyncLocal), so it's safe to share as a singleton across DbContext
// instances rather than constructing one per request.
builder.Services.AddSingleton<AuditSaveChangesInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
    options.UseNpgsql(connectionString)
        .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

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
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
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
