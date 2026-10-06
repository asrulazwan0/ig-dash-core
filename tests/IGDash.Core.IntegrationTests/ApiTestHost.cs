using System.Net.Http.Json;
using System.Text.Json;
using IGDash.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IGDash.Core.IntegrationTests;

internal sealed class ApiTestHost : IDisposable
{
    public WebApplicationFactory<Program> Factory { get; }
    private ApiTestHost(string connectionString, int rateLimit)
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("RateLimits:AccountsPerMinute", rateLimit.ToString());
        });
    }
    public static async Task<ApiTestHost> CreateAsync(PostgresFixture postgres, int rateLimit = 20)
    {
        var host = new ApiTestHost(await postgres.CreateDatabaseAsync(), rateLimit);
        await using var scope = host.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        return host;
    }
    public HttpClient Client(bool cookies = true) => Factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), HandleCookies = cookies, AllowAutoRedirect = false });
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    public static async Task RegisterAndLoginAsync(HttpClient client, string email)
    {
        using var register = await PostAsync(client, "/api/auth/register", new { email, password = "Test-password-123!" });
        register.EnsureSuccessStatusCode();
        using var login = await PostAsync(client, "/api/auth/login", new { email, password = "Test-password-123!" });
        login.EnsureSuccessStatusCode();
    }
    public void Dispose() => Factory.Dispose();
}
