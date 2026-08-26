using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ShiftPlanner.IntegrationTests;

// Login/refresh/authorization through the real HTTP pipeline — a real Postgres-backed
// ApplicationUser/Identity round trip, not the pure-JWT unit tests ShiftPlanner.Tests already
// has for JwtTokenFactory in isolation.
[Collection(IntegrationTestCollection.Name)]
public class AuthenticationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessToken()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { Email = IntegrationTestFixture.ManagerEmail, Password = IntegrationTestFixture.Password }, TestJson.Options);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("refreshToken=")));
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { Email = IntegrationTestFixture.ManagerEmail, Password = "definitely-wrong-password" }, TestJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutAnyToken_ReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync("/api/employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_RotatesAndIssuesNewAccessToken()
    {
        using var loginClient = fixture.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/v1/auth/login",
            new { Email = IntegrationTestFixture.EmployeeEmail, Password = IntegrationTestFixture.Password }, TestJson.Options);
        loginResponse.EnsureSuccessStatusCode();
        var refreshCookie = ExtractCookie(loginResponse, "refreshToken");

        // The refresh cookie is Secure+SameSite=Strict — HttpClient's automatic CookieContainer
        // won't re-attach a Secure cookie over the TestServer's plain-http base address, so it's
        // forwarded manually as a raw Cookie header instead (exactly what a browser's cookie jar
        // would do for a real https origin).
        using var refreshClient = fixture.CreateClient();
        refreshClient.DefaultRequestHeaders.Add("Cookie", $"refreshToken={refreshCookie}");
        var refreshResponse = await refreshClient.PostAsync("/api/v1/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var body = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
        var newAccessToken = body.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(newAccessToken));

        // issue #55: rotation-on-use — the just-presented refresh token's DB row is revoked as
        // part of that call, so presenting the SAME cookie again must now fail.
        using var reuseClient = fixture.CreateClient();
        reuseClient.DefaultRequestHeaders.Add("Cookie", $"refreshToken={refreshCookie}");
        var reuseResponse = await reuseClient.PostAsync("/api/v1/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        // The newly issued access token must actually work as a Bearer token.
        using var apiClient = fixture.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccessToken);
        var apiResponse = await apiClient.GetAsync("/api/employees");
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
    }

    // issue #71: a refresh token is a self-contained JWT too (same signature/issuer/audience),
    // differing from an access token only by its "token_use" claim — Program.cs's
    // OnTokenValidated handler must reject it as a Bearer token even though it would otherwise
    // pass every other JWT bearer check.
    [Fact]
    public async Task RefreshToken_UsedAsBearerToken_IsRejected()
    {
        using var loginClient = fixture.CreateClient();
        var loginResponse = await loginClient.PostAsJsonAsync("/api/v1/auth/login",
            new { Email = IntegrationTestFixture.ManagerEmail, Password = IntegrationTestFixture.Password }, TestJson.Options);
        loginResponse.EnsureSuccessStatusCode();
        var refreshToken = ExtractCookie(loginResponse, "refreshToken");

        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshToken);
        var response = await client.GetAsync("/api/employees");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string ExtractCookie(HttpResponseMessage response, string name)
    {
        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith($"{name}="));
        var valueWithAttributes = setCookie[(name.Length + 1)..];
        return valueWithAttributes[..valueWithAttributes.IndexOf(';')];
    }
}
