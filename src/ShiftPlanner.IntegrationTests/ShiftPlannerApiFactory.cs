using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftPlanner.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace ShiftPlanner.IntegrationTests;

// Boots the real ASP.NET Core pipeline (Program.cs, unmodified — no test-only wiring in the
// app itself) against a real, throwaway Postgres container, and applies the actual EF Core
// migrations rather than EnsureCreated(), so a broken migration is itself something these
// tests would catch. One container/factory is shared across the whole test run (see
// IntegrationTestCollection) rather than one per test class, both for speed and to keep the
// "auth" fixed-window rate limiter (Program.cs, 10 logins/minute, unpartitioned/global) from
// getting exhausted by every test class re-authenticating from scratch.
public sealed class ShiftPlannerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("shiftplanner_test")
        .WithUsername("shiftplanner")
        .WithPassword("integration-test-password")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        // Same dev-only style signing key as appsettings.Development.json — irrelevant which
        // exact value, only that it's stable for the lifetime of one factory/container.
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-at-least-32-bytes-long");
        builder.UseSetting("Jwt:Issuer", "ShiftPlanner");
        builder.UseSetting("Jwt:Audience", "ShiftPlanner");
        builder.UseSetting("Cors:FrontendOrigin", "http://localhost:5173");
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Force the host to build now (rather than lazily on the first CreateClient/Server
        // access) so migrations run before any test tries to use the database.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        // Mirrors Program.cs's own --migrate role-seeding step (roles aren't part of the EF
        // migrations themselves, ASP.NET Identity seeds them at runtime instead).
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Manager", "Employee" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    public new async Task DisposeAsync()
    {
        await _postgres.StopAsync();
        Dispose();
    }
}
