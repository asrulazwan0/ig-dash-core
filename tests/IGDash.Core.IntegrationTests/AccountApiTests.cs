using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IGDash.Core.IntegrationTests;

public sealed class AccountApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/logs")]
    public async Task AnonymousAccess_Returns401ProblemDetails(string path)
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        using var client = host.Client();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("invalid-token")]
    public async Task UnsafeAnonymousRequest_RequiresCsrf(string? token)
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        using var client = host.Client();
        if (token is not null) client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        using var response = await client.PostAsJsonAsync("/api/auth/register", new { email = "csrf@example.test", password = "Test-password-123!" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
    [Fact]
    public async Task RegistrationLoginLogout_RestoresSession_AndRejectsReplayedCookie()
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        using var client = host.Client();
        using var register = await ApiTestHost.PostAsync(client, "/api/auth/register", new { email = "session@example.test", password = "Test-password-123!" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        using var login = await ApiTestHost.PostAsync(client, "/api/auth/login", new { email = "session@example.test", password = "Test-password-123!" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("igdash.session="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=lax", cookie.ToLowerInvariant());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("session@example.test", me.GetProperty("email").GetString());
        Assert.Equal(2, me.EnumerateObject().Count());
        using var replay = host.Client(false);
        replay.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ApiTestHost.PostAsync(client, "/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task LoginErrors_AreGeneric_AndRepeatedFailuresLockTheAccount()
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "locked@example.test");
        using var other = host.Client();
        using var unknown = await ApiTestHost.PostAsync(other, "/api/auth/login", new { email = "missing@example.test", password = "Wrong-password-456!" });
        using var wrong = await ApiTestHost.PostAsync(other, "/api/auth/login", new { email = "locked@example.test", password = "Wrong-password-456!" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString(),
            (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await ApiTestHost.PostAsync(other, "/api/auth/login", new { email = "locked@example.test", password = "Wrong-password-456!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ApiTestHost.PostAsync(other, "/api/auth/login", new { email = "locked@example.test", password = "Test-password-123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task AccountRequests_AreRateLimited()
    {
        using var host = await ApiTestHost.CreateAsync(postgres, 1);
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.Created, (await ApiTestHost.PostAsync(client, "/api/auth/register", new { email = "rate@example.test", password = "Test-password-123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ApiTestHost.PostAsync(client, "/api/auth/login", new { email = "rate@example.test", password = "Test-password-123!" })).StatusCode);
    }
    [Theory]
    [InlineData("invalid", "Test-password-123!")]
    [InlineData("valid@example.test", "short")]
    [InlineData("valid@example.test", "alllowercaseonly")]
    public async Task InvalidRegistration_ReturnsValidationFailure(string email, string password)
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        using var client = host.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/auth/register", new { email, password })).StatusCode);
    }
    [Fact]
    public async Task ConcurrentRegistration_CannotCreateDuplicateAccounts()
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        using var a = host.Client(); using var b = host.Client();
        var results = await Task.WhenAll(ApiTestHost.PostAsync(a, "/api/auth/register", new { email = "race@example.test", password = "Test-password-123!" }),
            ApiTestHost.PostAsync(b, "/api/auth/register", new { email = "RACE@example.test", password = "Test-password-123!" }));
        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in results) response.Dispose();
    }
    [Fact]
    public async Task Logout_RequiresValidCsrf_AndDoesNotInvalidateSessionOnFailure()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "logout@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
