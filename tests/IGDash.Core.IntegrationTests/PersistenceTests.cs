using IGDash.Core.Infrastructure;
using IGDash.Core.Infrastructure.Identity;
using IGDash.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using Xunit;

namespace IGDash.Core.IntegrationTests;

public sealed class PersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Migrations_CreateAccountSchema_AndPreserveAccountsWhenAppliedAgain()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var services = BuildServices(connectionString);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await AccountTablesAsync(db));
            Assert.Equal(3, (await db.Database.GetPendingMigrationsAsync()).Count());
            await db.Database.MigrateAsync();
            Assert.Equal(["AspNetUserClaims", "AspNetUserLogins", "AspNetUserTokens", "AspNetUsers"], await AccountTablesAsync(db));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "migration@example.test", Email = "migration@example.test" };
            var result = await users.CreateAsync(user, "Test-password-123!");
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Code)));
        }

        await using var secondScope = services.CreateAsyncScope();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await secondDb.Database.MigrateAsync();
        Assert.Empty(await secondDb.Database.GetPendingMigrationsAsync());
        Assert.Equal(3, (await secondDb.Database.GetAppliedMigrationsAsync()).Count());
        var secondUsers = secondScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var stored = await secondUsers.FindByEmailAsync("migration@example.test");
        Assert.NotNull(stored);
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.NotEqual("Test-password-123!", stored.PasswordHash);
        Assert.True(await secondUsers.CheckPasswordAsync(stored, "Test-password-123!"));
    }

    [Fact]
    public async Task InitialMigration_CanBeRevertedAndReapplied_OnAnEmptyTestDatabase()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var services = BuildServices(connectionString);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        await db.GetService<IMigrator>().MigrateAsync("0");
        Assert.Empty(await AccountTablesAsync(db));
        Assert.Empty(await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(4, (await AccountTablesAsync(db)).Length);
        Assert.Equal(3, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task ApiStartup_RegistersPersistence_WithoutApplyingMigrations()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Database.CanConnectAsync());
        Assert.Empty(await AccountTablesAsync(db));
        Assert.Equal(3, (await db.Database.GetPendingMigrationsAsync()).Count());
    }

    [Fact]
    public async Task TagsMigration_PreservesExistingLogsAndDefaultsToEmptyTags()
    {
        using var services = BuildServices(await postgres.CreateDatabaseAsync());
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.GetService<IMigrator>().MigrateAsync("20261006160031_PrivateLogs");
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "upgrade@example.test", Email = "upgrade@example.test" };
        Assert.True((await users.CreateAsync(user, "Test-password-123!")).Succeeded);
        var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var message = "legacy"; var level = "info";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"LogEntries\" (\"Id\", \"OwnerId\", \"Message\", \"Level\", \"OccurredAt\", \"CreatedAt\") VALUES ({id}, {user.Id}, {message}, {level}, {now}, {now})");
        await db.Database.MigrateAsync();
        var log = await db.LogEntries.SingleAsync();
        Assert.Equal(id, log.Id); Assert.Equal("legacy", log.Message); Assert.Empty(log.Tags);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static ServiceProvider BuildServices(string connectionString)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString
        }).Build();
        return new ServiceCollection().AddLogging().AddInfrastructure(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static async Task<string[]> AccountTablesAsync(ApplicationDbContext db)
    {
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_name LIKE 'AspNet%' ORDER BY table_name", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        return tables.ToArray();
    }
}
