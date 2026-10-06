using System.Reflection;
using IGDash.Core.Infrastructure;
using IGDash.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace IGDash.Core.IntegrationTests;

public sealed class ConfigurationTests
{
    [Fact]
    public void MissingDatabaseConfiguration_FailsWithoutExposingSecrets()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddInfrastructure(new ConfigurationBuilder().Build()));
        Assert.Contains("Configure ConnectionStrings:DefaultConnection", error.Message);
    }

    [Fact]
    public void StructuredConfiguration_PreservesSpecialCharactersInPassword()
    {
        const string password = "not-a-real-secret;with=delimiters\"";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Postgres:Host"] = "postgres",
            ["Postgres:Port"] = "5432",
            ["Postgres:Database"] = "igdash",
            ["Postgres:Username"] = "igdash",
            ["Postgres:Password"] = password
        }).Build();
        using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.Equal(password, connection.Password);
        Assert.Equal("postgres", connection.Host);
    }

    [Fact]
    public void InnerLayers_DoNotDependOnPersistenceOrWebFrameworks()
    {
        foreach (var name in new[] { "IGDash.Core.Domain", "IGDash.Core.Application" })
        {
            var references = Assembly.Load(name).GetReferencedAssemblies();
            Assert.DoesNotContain(references, reference => reference.Name!.StartsWith("Microsoft.AspNetCore")
                || reference.Name.StartsWith("Microsoft.EntityFrameworkCore")
                || reference.Name.StartsWith("Npgsql")
                || reference.Name == "IGDash.Core.Infrastructure");
        }
    }
}
