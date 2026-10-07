using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IGDash.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IGDash.Core.IntegrationTests;

public sealed class AccountReadinessTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Password = "Test-password-123!";
    private const string NewPassword = "New-password-456!";
    private static async Task Register(HttpClient client, string email) => (await ApiTestHost.PostAsync(client, "/api/auth/register", new { email, password = Password })).EnsureSuccessStatusCode();
    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password = Password) => ApiTestHost.PostAsync(client, "/api/auth/login", new { email, password });
    [Fact]
    public async Task RegistrationRequiresProof_ConfirmationIsSingleUse_AndResendIsGenericAndLimited()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await Register(client, "verify@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(client, "verify@example.test")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, "verify@example.test", NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await ApiTestHost.PostAsync(client, "/api/auth/verification/request", new { email = "verify@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await ApiTestHost.PostAsync(client, "/api/auth/verification/request", new { email = "missing@example.test" })).StatusCode);
        Assert.Single(host.Emails.Messages);
        var link = host.Emails.Link("verify@example.test", "Verify your IGDash email");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/verification/confirm", new { userId = link["user"], token = link["token"] })).StatusCode);
        await ApiTestHost.ConfirmAsync(client, "verify@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/auth/verification/confirm", new { userId = link["user"], token = link["token"] })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(client, "verify@example.test")).StatusCode);
        var profile = await client.GetFromJsonAsync<JsonElement>("/api/auth/profile"); Assert.True(profile.GetProperty("emailConfirmed").GetBoolean());
    }
    [Fact]
    public async Task ResetIsGenericSingleUse_AndInvalidatesEveryCookie()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var a = host.Client(); using var b = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "reset@example.test"); (await Login(b, "reset@example.test")).EnsureSuccessStatusCode();
        var known = await ApiTestHost.PostAsync(a, "/api/auth/password/forgot", new { email = "reset@example.test" });
        var missing = await ApiTestHost.PostAsync(a, "/api/auth/password/forgot", new { email = "missing@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode); Assert.Equal(known.StatusCode, missing.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await missing.Content.ReadAsStringAsync());
        var link = host.Emails.Link("reset@example.test", "Reset your IGDash password");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/password/reset", new { userId = link["user"], token = "bad", password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ApiTestHost.PostAsync(a, "/api/auth/password/reset", new { userId = link["user"], token = link["token"], password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/auth/me")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/password/reset", new { userId = link["user"], token = link["token"], password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(a, "reset@example.test")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(a, "reset@example.test", NewPassword)).StatusCode);
    }
    [Fact]
    public async Task ExpiredLinksAreRejected()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await Register(client, "expiry@example.test");
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value;
        var lifespan = options.TokenLifespan;
        try
        {
            options.TokenLifespan = TimeSpan.FromSeconds(-1);
            var link = host.Emails.Link("expiry@example.test", "Verify your IGDash email");
            Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/auth/verification/confirm", new { userId = link["user"], token = link["token"] })).StatusCode);
        }
        finally { options.TokenLifespan = lifespan; }
        await ApiTestHost.ConfirmAsync(client, "expiry@example.test");
        (await ApiTestHost.PostAsync(client, "/api/auth/password/forgot", new { email = "expiry@example.test" })).EnsureSuccessStatusCode();
        var reset = host.Emails.Link("expiry@example.test", "Reset your IGDash password");
        try
        {
            options.TokenLifespan = TimeSpan.FromSeconds(-1);
            Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/auth/password/reset", new { userId = reset["user"], token = reset["token"], password = NewPassword })).StatusCode);
        }
        finally { options.TokenLifespan = lifespan; }
    }
    [Fact]
    public async Task PasswordChangeRequiresCurrentPasswordCsrf_AndRevokesOtherSessions()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var a = host.Client(); using var b = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "change-password@example.test"); (await Login(b, "change-password@example.test")).EnsureSuccessStatusCode();
        var payload = new { currentPassword = Password, newPassword = NewPassword };
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync("/api/auth/password/change", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/password/change", new { currentPassword = NewPassword, newPassword = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ApiTestHost.PostAsync(a, "/api/auth/password/change", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(a, "change-password@example.test")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(a, "change-password@example.test", NewPassword)).StatusCode);
    }
    [Fact]
    public async Task EmailChangeRequiresTargetProof_PreservesData_AndRejectsReplay()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var a = host.Client(); using var b = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "old@example.test"); (await Login(b, "old@example.test")).EnsureSuccessStatusCode();
        var before = await a.GetFromJsonAsync<JsonElement>("/api/auth/me");
        (await ApiTestHost.PostAsync(a, "/api/logs", new { message = "Keep me", level = "info", occurredAt = "2026-10-07T12:00:00Z" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/email/change", new { email = "new@example.test", currentPassword = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await ApiTestHost.PostAsync(a, "/api/auth/email/change", new { email = "new@example.test", currentPassword = Password })).StatusCode);
        Assert.Equal("old@example.test", (await a.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("email").GetString());
        Assert.Contains(host.Emails.Messages, email => email.Recipient == "old@example.test" && email.Subject == "IGDash email change requested");
        var link = host.Emails.Link("new@example.test", "Confirm your IGDash email change");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/email/confirm", new { userId = link["user"], email = "wrong@example.test", token = link["token"] })).StatusCode);
        var confirm = new { userId = link["user"], email = "new@example.test", token = link["token"] };
        Assert.Equal(HttpStatusCode.NoContent, (await ApiTestHost.PostAsync(a, "/api/auth/email/confirm", confirm)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/email/confirm", confirm)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(a, "old@example.test")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(a, "new@example.test")).StatusCode);
        Assert.Equal(before.GetProperty("id").GetString(), (await a.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetString());
        Assert.Equal(1, (await a.GetFromJsonAsync<JsonElement>("/api/logs")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task ConflictingEmailConfirmationRollsBackWithoutChangingEitherAccount()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var a = host.Client(); using var b = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "first@example.test"); await ApiTestHost.RegisterAndLoginAsync(b, "taken@example.test");
        (await ApiTestHost.PostAsync(a, "/api/auth/email/change", new { email = "taken@example.test", currentPassword = Password })).EnsureSuccessStatusCode();
        var link = host.Emails.Link("taken@example.test", "Confirm your IGDash email change");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/email/confirm", new { userId = link["user"], email = "taken@example.test", token = link["token"] })).StatusCode);
        Assert.Equal("first@example.test", (await a.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("email").GetString());
        Assert.Equal("taken@example.test", (await b.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("email").GetString());
    }
    [Fact]
    public async Task SessionsAreOwned_IndividualRevocationRejectsCookieReplay_AllRevocationSignsOut()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var a = host.Client(); using var other = host.Client(); using var foreign = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "sessions@example.test");
        var login = await Login(other, "sessions@example.test"); login.EnsureSuccessStatusCode();
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("igdash.session=")).Split(';')[0];
        await ApiTestHost.RegisterAndLoginAsync(foreign, "foreign@example.test");
        var sessions = await a.GetFromJsonAsync<JsonElement>("/api/auth/sessions"); Assert.Equal(2, sessions.GetArrayLength());
        var remote = sessions.EnumerateArray().Single(session => !session.GetProperty("current").GetBoolean()).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await ApiTestHost.PostAsync(foreign, $"/api/auth/sessions/{remote}/revoke", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync($"/api/auth/sessions/{remote}/revoke", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ApiTestHost.PostAsync(a, $"/api/auth/sessions/{remote}/revoke", new { })).StatusCode);
        using var replay = host.Client(false); replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ApiTestHost.PostAsync(a, "/api/auth/sessions/revoke-all", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await foreign.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task RecoveryTokensAreBoundToOwnerAndPurpose_AndCancelPendingEmailChanges()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var a = host.Client(); using var b = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "purpose@example.test");
        await ApiTestHost.RegisterAndLoginAsync(b, "other-owner@example.test");
        var other = await b.GetFromJsonAsync<JsonElement>("/api/auth/me");
        (await ApiTestHost.PostAsync(a, "/api/auth/email/change", new { email = "pending@example.test", currentPassword = Password })).EnsureSuccessStatusCode();
        var change = host.Emails.Link("pending@example.test", "Confirm your IGDash email change");
        (await ApiTestHost.PostAsync(a, "/api/auth/password/forgot", new { email = "purpose@example.test" })).EnsureSuccessStatusCode();
        var reset = host.Emails.Link("purpose@example.test", "Reset your IGDash password");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/password/reset", new { userId = other.GetProperty("id").GetString(), token = reset["token"], password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/password/reset", new { userId = reset["user"], token = change["token"], password = NewPassword })).StatusCode);
        (await ApiTestHost.PostAsync(a, "/api/auth/password/reset", new { userId = reset["user"], token = reset["token"], password = NewPassword })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(a, "/api/auth/email/confirm", new { userId = change["user"], email = "pending@example.test", token = change["token"] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/auth/me")).StatusCode);
        (await Login(a, "purpose@example.test", NewPassword)).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await a.GetFromJsonAsync<JsonElement>("/api/auth/profile")).GetProperty("pendingEmail").ValueKind);
    }
    [Fact]
    public async Task ExpiredServerSessionRejectsAnOtherwiseValidCookie()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "session-expiry@example.test");
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.BrowserSessions.ExecuteUpdateAsync(setters => setters.SetProperty(session => session.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task DeliveryFailureDoesNotRevealEligibleAccounts_OrBypassVerification()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client(); host.Emails.Fail = true;
        await Register(client, "outage@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await Login(client, "outage@example.test")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await ApiTestHost.PostAsync(client, "/api/auth/verification/request", new { email = "outage@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await ApiTestHost.PostAsync(client, "/api/auth/verification/request", new { email = "missing@example.test" })).StatusCode);
        Assert.Empty(host.Emails.Messages);
    }
    [Theory]
    [InlineData("/api/auth/profile")]
    [InlineData("/api/auth/sessions")]
    public async Task PrivateSettingsRequireAuthentication(string path)
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task SeedScenariosPreserveUpdatesAndKeepUnverifiedUserOutUntilConfirmation()
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        await using (var scope = host.Factory.Services.CreateAsyncScope()) Assert.Equal(new SeedResult(3, 180), await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(Password));
        using var client = host.Client(); Assert.Equal(HttpStatusCode.Forbidden, (await Login(client, "demo.unverified@example.test")).StatusCode);
        (await ApiTestHost.PostAsync(client, "/api/auth/verification/request", new { email = "demo.unverified@example.test" })).EnsureSuccessStatusCode();
        await ApiTestHost.ConfirmAsync(client, "demo.unverified@example.test"); (await Login(client, "demo.unverified@example.test")).EnsureSuccessStatusCode();
        Assert.Equal(12, (await client.GetFromJsonAsync<JsonElement>("/api/logs")).GetProperty("total").GetInt32());
        (await ApiTestHost.PostAsync(client, "/api/auth/email/change", new { email = "changed-demo@example.test", currentPassword = Password })).EnsureSuccessStatusCode();
        var link = host.Emails.Link("changed-demo@example.test", "Confirm your IGDash email change");
        (await ApiTestHost.PostAsync(client, "/api/auth/email/confirm", new { userId = link["user"], email = "changed-demo@example.test", token = link["token"] })).EnsureSuccessStatusCode();
        await using (var second = host.Factory.Services.CreateAsyncScope()) Assert.Equal(new SeedResult(0, 0), await second.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(Password));
        (await Login(client, "changed-demo@example.test")).EnsureSuccessStatusCode();
        Assert.Equal(12, (await client.GetFromJsonAsync<JsonElement>("/api/logs")).GetProperty("total").GetInt32());
    }
}
