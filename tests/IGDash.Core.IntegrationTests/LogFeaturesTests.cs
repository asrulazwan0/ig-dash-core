using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IGDash.Core.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IGDash.Core.IntegrationTests;

public sealed class LogFeaturesTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static object Entry(string message = "Plan release", string[]? tags = null, string level = "info", string occurredAt = "2026-10-06T12:00:00Z") => new { message, tags = tags ?? ["work"], level, occurredAt };
    private static async Task<HttpResponseMessage> Change(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    [Fact]
    public async Task EditsDeletesAndTags_AreOwnedAndRequireCsrf()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var a = host.Client(); using var b = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "owner@example.test"); await ApiTestHost.RegisterAndLoginAsync(b, "other@example.test");
        var created = await ApiTestHost.PostAsync(a, "/api/logs", Entry(tags: [" WORK ", "work", "learning"]));
        var log = await created.Content.ReadFromJsonAsync<JsonElement>(); var path = "/api/logs/" + log.GetProperty("id").GetString();
        Assert.Equal(["learning", "work"], log.GetProperty("tags").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Change(b, HttpMethod.Put, path, Entry("Stolen"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Change(b, HttpMethod.Delete, path)).StatusCode);
        Assert.Empty((await b.GetFromJsonAsync<string[]>("/api/logs/tags"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PutAsJsonAsync(path, Entry())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.DeleteAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(a, HttpMethod.Put, path, new { ownerId = Guid.NewGuid(), message = "spoof", level = "info", occurredAt = "2026-10-06T12:00:00Z" })).StatusCode);
        var edited = await Change(a, HttpMethod.Put, path, Entry("Updated", ["personal"], "warning"));
        edited.EnsureSuccessStatusCode(); var record = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated", record.GetProperty("message").GetString());
        Assert.Equal(log.GetProperty("createdAt").GetString(), record.GetProperty("createdAt").GetString());
        Assert.Equal(["personal"], (await a.GetFromJsonAsync<string[]>("/api/logs/tags"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await Change(a, HttpMethod.Delete, path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Change(a, HttpMethod.Delete, path)).StatusCode);
        Assert.Empty((await a.GetFromJsonAsync<string[]>("/api/logs/tags"))!);
    }
    [Fact]
    public async Task CombinedFilters_SummaryAndExportMatch_WithoutForeignData()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var a = host.Client(); using var b = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "filters@example.test"); await ApiTestHost.RegisterAndLoginAsync(b, "secret@example.test");
        for (var i = 0; i < 26; i++) (await ApiTestHost.PostAsync(a, "/api/logs", Entry("Release review " + i, level: "warning"))).EnsureSuccessStatusCode();
        (await ApiTestHost.PostAsync(a, "/api/logs", Entry("Release boundary", level: "warning", occurredAt: "2026-10-07T00:00:00Z"))).EnsureSuccessStatusCode();
        (await ApiTestHost.PostAsync(a, "/api/logs", Entry("Other", ["personal"], "error"))).EnsureSuccessStatusCode();
        (await ApiTestHost.PostAsync(b, "/api/logs", Entry("Release SECRET", level: "warning"))).EnsureSuccessStatusCode();
        const string query = "level=warning&search=RELEASE&tag=work&from=2026-10-06T00:00:00Z&to=2026-10-07T00:00:00Z";
        var list = await a.GetFromJsonAsync<JsonElement>("/api/logs?" + query);
        Assert.Equal(26, list.GetProperty("total").GetInt32()); Assert.Equal(25, list.GetProperty("items").GetArrayLength());
        var summary = await a.GetFromJsonAsync<JsonElement>("/api/logs/summary?" + query);
        Assert.Equal(26, summary.GetProperty("total").GetInt32()); Assert.Equal(26, summary.GetProperty("warning").GetInt32());
        Assert.Equal(0, summary.GetProperty("error").GetInt32()); Assert.Equal(5, summary.GetProperty("recent").GetArrayLength());
        Assert.Equal(26, summary.GetProperty("activity")[0].GetProperty("count").GetInt32());
        var csv = await a.GetStringAsync("/api/logs/export?" + query);
        Assert.Equal(27, csv.Trim().Split("\r\n").Length); Assert.DoesNotContain("SECRET", csv); Assert.DoesNotContain("boundary", csv);
        Assert.DoesNotContain("Other", csv);
    }
    [Theory]
    [InlineData("search=100%25", "100% complete")]
    [InlineData("search=a_b", "a_b literal")]
    public async Task SearchTreatsWildcardsLiterally(string query, string message)
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "literal@example.test");
        (await ApiTestHost.PostAsync(client, "/api/logs", Entry(message))).EnsureSuccessStatusCode();
        (await ApiTestHost.PostAsync(client, "/api/logs", Entry("1000 complete axb"))).EnsureSuccessStatusCode();
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/logs?" + query)).GetProperty("total").GetInt32());
    }
    [Theory]
    [InlineData("from=2026-10-07T00:00:00Z&to=2026-10-06T00:00:00Z")]
    [InlineData("from=2026-10-06T00:00:00%2B08:00")]
    [InlineData("tag=bad%20tag")]
    [InlineData("from=not-a-date")]
    public async Task InvalidFiltersReturn400AcrossReads(string query)
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "invalid-filter@example.test");
        foreach (var path in new[] { "/api/logs", "/api/logs/summary", "/api/logs/export" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "?" + query)).StatusCode);
    }
    [Theory]
    [InlineData("bad tag")]
    [InlineData("")]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345")]
    public async Task InvalidTagsRejected(string tag)
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "invalid-tag@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/logs", Entry(tags: [tag]))).StatusCode);
    }
    [Fact]
    public async Task OversizedSearchAndTagListsAreRejected()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "limits@example.test");
        foreach (var path in new[] { "/api/logs", "/api/logs/summary", "/api/logs/export" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "?search=" + new string('a', 201))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/logs", Entry(tags: Enumerable.Range(0, 11).Select(i => "tag" + i).ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/logs", Entry(tags: [null!]))).StatusCode);
    }
    [Fact]
    public async Task AnonymousMutationsAreDenied()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        var path = "/api/logs/" + Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Change(client, HttpMethod.Put, path, Entry())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Change(client, HttpMethod.Delete, path)).StatusCode);
    }
    [Fact]
    public async Task ExportEscapesQuotesNewlinesAndSpreadsheetFormulas()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "csv@example.test");
        (await ApiTestHost.PostAsync(client, "/api/logs", Entry("=HYPERLINK(\"bad\")\nsecond,line"))).EnsureSuccessStatusCode();
        var csv = await client.GetAsync("/api/logs/export");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", csv.Content.Headers.ContentDisposition?.ToString());
        Assert.Contains("\"'=HYPERLINK(\"\"bad\"\")\nsecond,line\"", await csv.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("/api/logs/summary")]
    [InlineData("/api/logs/tags")]
    [InlineData("/api/logs/export")]
    public async Task NewReadsRequireAuthentication(string path)
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task SummaryHandlesEmptyFutureAndMinimumDateRanges()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "empty-summary@example.test");
        foreach (var query in new[] { "", "from=2099-01-01T00:00:00Z", "to=0001-01-01T00:00:00Z" })
        {
            var response = await client.GetAsync("/api/logs/summary?" + query); response.EnsureSuccessStatusCode();
            Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetInt32());
        }
    }
    [Fact]
    public async Task SeederIsRepeatablePreservesEditsAndSeparatesAccounts()
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        Assert.Equal(new SeedResult(3, 180), await seeder.SeedAsync("Demo-Password-123!"));
        Assert.Equal(new SeedResult(0, 0), await seeder.SeedAsync("Another-Password-123!"));
        using var client = host.Client();
        (await ApiTestHost.PostAsync(client, "/api/auth/login", new { email = "demo.one@example.test", password = "Demo-Password-123!" })).EnsureSuccessStatusCode();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/logs"); Assert.Equal(84, list.GetProperty("total").GetInt32());
        Assert.All(list.GetProperty("items").EnumerateArray(), log => Assert.StartsWith("Alex:", log.GetProperty("message").GetString()));
        var path = "/api/logs/" + list.GetProperty("items")[0].GetProperty("id").GetString();
        (await Change(client, HttpMethod.Put, path, Entry("Kept edit", ["custom"]))).EnsureSuccessStatusCode();
        // Use a fresh scope, as the first run's tracked entities should not influence the preservation assertion.
        await using var fresh = host.Factory.Services.CreateAsyncScope();
        Assert.Equal(new SeedResult(0, 0), await fresh.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync("Demo-Password-123!"));
        Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/api/logs?search=Kept%20edit")).GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task SeederRejectsProductionMissingPasswordAndExistingEmail()
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        await using var scope = host.Factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync("weak"));
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var original = environment.EnvironmentName;
        try { environment.EnvironmentName = "Production"; await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync("Demo-Password-123!")); }
        finally { environment.EnvironmentName = original; }
        using var client = host.Client(); await ApiTestHost.RegisterAndLoginAsync(client, "demo.two@example.test");
        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync("Demo-Password-123!"));
        await using var verify = host.Factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(db.Users); Assert.Empty(db.LogEntries);
    }
}
