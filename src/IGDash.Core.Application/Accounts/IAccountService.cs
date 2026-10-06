namespace IGDash.Core.Application.Accounts;

public sealed record AccountSession(Guid Id, string Email, string SecurityStamp);
public sealed record RegistrationResult(bool Succeeded, bool Conflict, string[] Errors);
public interface IAccountService
{
    Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken cancellationToken);
    Task<AccountSession?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken);
    Task<bool> ValidateSessionAsync(Guid id, string stamp, CancellationToken cancellationToken);
    Task RevokeSessionsAsync(Guid id, CancellationToken cancellationToken);
}
