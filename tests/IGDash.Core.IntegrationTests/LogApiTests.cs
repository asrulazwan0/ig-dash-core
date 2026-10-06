using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IGDash.Core.IntegrationTests;

public sealed class LogApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task TwoUsers_SeeOnlyTheirOwnLogs_InCountsFiltersAndPages()
    {
        using var host = await ApiTestHost.CreateAsync(postgres);
        using var a = host.Client(); using var b = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(a, "a@example.test");
        await ApiTestHost.RegisterAndLoginAsync(b, "b@example.test");
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await ApiTestHost.PostAsync(a, "/api/logs", new { message = "A " + i, level = "info", occurredAt = "2026-10-06T12:00:00Z" })).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await ApiTestHost.PostAsync(b, "/api/logs", new { message = "B " + i, level = "error", occurredAt = "2026-10-06T12:00:00Z" })).StatusCode);
        }
        var first = await a.GetFromJsonAsync<JsonElement>("/api/logs?page=1&pageSize=2");
        var second = await a.GetFromJsonAsync<JsonElement>("/api/logs?page=2&pageSize=2");
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        var ids = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(x => x.GetProperty("id").GetString()).ToArray();
        Assert.Equal(3, ids.Distinct().Count());
        Assert.All(first.GetProperty("items").EnumerateArray(), x =>
        {
            Assert.StartsWith("A ", x.GetProperty("message").GetString());
            Assert.False(x.TryGetProperty("ownerId", out _));
        });
        Assert.Equal(0, (await a.GetFromJsonAsync<JsonElement>("/api/logs?level=error")).GetProperty("total").GetInt32());
        var other = await b.GetFromJsonAsync<JsonElement>("/api/logs");
        Assert.All(other.GetProperty("items").EnumerateArray(), x => Assert.StartsWith("B ", x.GetProperty("message").GetString()));
        var repeat = await a.GetFromJsonAsync<JsonElement>("/api/logs?page=1&pageSize=2");
        Assert.Equal(first.GetProperty("items").ToString(), repeat.GetProperty("items").ToString());
    }
    [Theory]
    [InlineData("", "info", "2026-10-06T12:00:00Z")]
    [InlineData("message", "invalid", "2026-10-06T12:00:00Z")]
    [InlineData("message", "info", "2026-10-06T12:00:00+08:00")]
    public async Task InvalidLogs_Return400(string message, string level, string occurredAt)
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "invalid-log@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/logs", new { message, level, occurredAt })).StatusCode);
    }
    [Fact]
    public async Task ClientOwnership_IsRejected_AndCsrfIsRequired()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "ownership@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/logs", new { ownerId = Guid.NewGuid(), message = "spoofed", level = "info", occurredAt = "2026-10-06T12:00:00Z" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/logs", new { message = "no csrf", level = "info", occurredAt = "2026-10-06T12:00:00Z" })).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/logs")).GetProperty("total").GetInt32());
    }
    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("level=other")]
    [InlineData("page=abc")]
    public async Task InvalidQuery_Return400(string query)
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        await ApiTestHost.RegisterAndLoginAsync(client, "query@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/logs?" + query)).StatusCode);
    }
    [Fact]
    public async Task OversizedMessage_IsRejected_AndAnonymousCreationIsDenied()
    {
        using var host = await ApiTestHost.CreateAsync(postgres); using var client = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ApiTestHost.PostAsync(client, "/api/logs", new { message = "anonymous", level = "info", occurredAt = "2026-10-06T12:00:00Z" })).StatusCode);
        await ApiTestHost.RegisterAndLoginAsync(client, "size@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiTestHost.PostAsync(client, "/api/logs", new { message = new string('x', 2001), level = "info", occurredAt = "2026-10-06T12:00:00Z" })).StatusCode);
    }
}
