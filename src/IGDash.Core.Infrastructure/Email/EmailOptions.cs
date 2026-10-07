namespace IGDash.Core.Infrastructure.Email;

public sealed class EmailOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public string From { get; set; } = "noreply@igdash.test";
    public string PublicOrigin { get; set; } = "http://localhost:5173";
    public string Security { get; set; } = "None";
    public string? Username { get; set; }
    public string? Password { get; set; }
}
