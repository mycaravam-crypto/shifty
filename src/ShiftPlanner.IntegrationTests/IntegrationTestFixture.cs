using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ShiftPlanner.Infrastructure.Persistence;
using Xunit;

namespace ShiftPlanner.IntegrationTests;

// The API serializes with the ASP.NET Core MVC default (camelCase, case-insensitive on the way
// in) — plain HttpClient.*AsJsonAsync/ReadFromJsonAsync default to JsonSerializerDefaults.General
// instead, which is case-sensitive and Pascal-cased, so every test in this project reads/writes
// JSON through this shared Web-flavored options instance to match what the real API speaks.
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

// One instance shared across every test class in [Collection(Name)] (see
// IntegrationTestCollection below) — starts exactly one Postgres container + host for the
// whole run and seeds the three Staff accounts every test needs, rather than each test class
// paying container-startup cost (~seconds) and re-hitting the "auth" login rate limiter.
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@integration.test";
    public const string ManagerEmail = "manager@integration.test";
    public const string EmployeeEmail = "employee@integration.test";
    public const string Password = "IntegrationTest123!";

    public ShiftPlannerApiFactory Factory { get; } = new();

    public string AdminAccessToken { get; private set; } = "";
    public string ManagerAccessToken { get; private set; } = "";
    public string EmployeeAccessToken { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await Factory.InitializeAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await CreateStaffUserAsync(userManager, AdminEmail, "Admin");
            await CreateStaffUserAsync(userManager, ManagerEmail, "Manager");
            await CreateStaffUserAsync(userManager, EmployeeEmail, "Employee");
        }

        // Real HTTP logins (not just UserManager) so the tokens below are exactly what
        // AuthController/JwtTokenFactory produce — three calls total against the "auth"
        // rate limiter (10/minute, shared across the whole run), leaving headroom for the
        // handful of additional login calls individual tests make.
        using var client = Factory.CreateClient();
        AdminAccessToken = await LoginAsync(client, AdminEmail);
        ManagerAccessToken = await LoginAsync(client, ManagerEmail);
        EmployeeAccessToken = await LoginAsync(client, EmployeeEmail);
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();

    private static async Task CreateStaffUserAsync(UserManager<ApplicationUser> userManager, string email, string role)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
            return;

        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await userManager.CreateAsync(user, Password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        await userManager.AddToRoleAsync(user, role);
    }

    public static async Task<string> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Email = email, Password = password }, TestJson.Options);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(TestJson.Options);
        return body!.AccessToken;
    }

    public HttpClient CreateClient() => Factory.CreateClient();

    public HttpClient CreateAuthenticatedClient(string accessToken)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public HttpClient CreateAdminClient() => CreateAuthenticatedClient(AdminAccessToken);
    public HttpClient CreateManagerClient() => CreateAuthenticatedClient(ManagerAccessToken);
    public HttpClient CreateEmployeeClient() => CreateAuthenticatedClient(EmployeeAccessToken);

    private record AccessTokenResponse(string AccessToken);
}

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    public const string Name = "Integration";
}
