using System.Net.Http.Json;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using IGDash.Core.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using System.Text.Json;
using IGDash.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IGDash.Core.IntegrationTests;

internal sealed class ApiTestHost : IDisposable
{
    private static readonly ConditionalWeakTable<HttpClient, CapturedMail> ClientMail = new();
    public CapturedMail Emails { get; } = new();
    public WebApplicationFactory<Program> Factory { get; }
    private ApiTestHost(string connectionString, int rateLimit)
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("RateLimits:AccountsPerMinute", rateLimit.ToString());
            builder.ConfigureServices(services => services.AddSingleton<IAccountEmailSender>(Emails));
        });
    }
    public static async Task<ApiTestHost> CreateAsync(PostgresFixture postgres, int rateLimit = 20)
    {
        var host = new ApiTestHost(await postgres.CreateDatabaseAsync(), rateLimit);
        await using var scope = host.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        return host;
    }
    public HttpClient Client(bool cookies = true)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = cookies, AllowAutoRedirect = false });
        ClientMail.Add(client, Emails); return client;
    }
    public static async Task ConfirmAsync(HttpClient client, string email)
    {
        var link = ClientMail.GetValue(client, _ => throw new InvalidOperationException()).Link(email, "Verify your IGDash email");
        using var confirm = await PostAsync(client, "/api/auth/verification/confirm", new { userId = link["user"], token = link["token"] });
        confirm.EnsureSuccessStatusCode();
    }
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
        await ConfirmAsync(client, email);
        using var login = await PostAsync(client, "/api/auth/login", new { email, password = "Test-password-123!" });
        login.EnsureSuccessStatusCode();
    }
    public void Dispose() => Factory.Dispose();
}

internal sealed record CapturedMessage(string Recipient, string Subject, string Text);
internal sealed class CapturedMail : IAccountEmailSender
{
    public ConcurrentQueue<CapturedMessage> Messages { get; } = new();
    public bool Fail { get; set; }
    public Task SendAsync(string recipient, string subject, string text, CancellationToken cancellationToken)
    {
        if (Fail) throw new AccountEmailException();
        Messages.Enqueue(new(recipient, subject, text)); return Task.CompletedTask;
    }
    public Dictionary<string, string> Link(string recipient, string subject)
    {
        var message = Messages.Last(message => message.Recipient.Equals(recipient, StringComparison.OrdinalIgnoreCase) && message.Subject == subject);
        var uri = new Uri(message.Text.Split('\n').First(line => line.StartsWith("http", StringComparison.Ordinal)));
        return uri.Fragment.TrimStart('#').Split('&').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
    }
}
