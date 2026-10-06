namespace IGDash.Core.Infrastructure.Persistence;

internal sealed class PostgresOptions
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 5432;
    public string Database { get; init; } = "";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
}
