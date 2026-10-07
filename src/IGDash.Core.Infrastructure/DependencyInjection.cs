using IGDash.Core.Application.Accounts;
using IGDash.Core.Application.Logs;
using IGDash.Core.Infrastructure.Identity;
using IGDash.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using IGDash.Core.Infrastructure.Email;

namespace IGDash.Core.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var postgres = configuration.GetSection("Postgres").Get<PostgresOptions>();
            if (postgres is null || string.IsNullOrWhiteSpace(postgres.Host)
                || string.IsNullOrWhiteSpace(postgres.Database)
                || string.IsNullOrWhiteSpace(postgres.Username)
                || string.IsNullOrEmpty(postgres.Password)
                || postgres.Port is < 1 or > 65535)
            {
                throw new InvalidOperationException(
                    "Configure ConnectionStrings:DefaultConnection or the Postgres Host, Port, Database, Username, and Password settings.");
            }

            connectionString = new NpgsqlConnectionStringBuilder
            {
                Host = postgres.Host,
                Port = postgres.Port,
                Database = postgres.Database,
                Username = postgres.Username,
                Password = postgres.Password
            }.ConnectionString;
        }

        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 12;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        })
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.Configure<EmailOptions>(configuration.GetSection("Email"));
        services.AddScoped<IAccountEmailSender, SmtpAccountEmailSender>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ILogRepository, LogRepository>();
        return services;
    }
}
