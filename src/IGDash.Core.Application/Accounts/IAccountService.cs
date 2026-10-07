namespace IGDash.Core.Application.Accounts;

public sealed record AccountSession(Guid Id, string Email, string SecurityStamp, Guid SessionId);
public sealed record LoginResult(AccountSession? Session, bool NeedsVerification = false);
public sealed record RegistrationResult(bool Succeeded, bool Conflict, string[] Errors);
public sealed record AccountResult(bool Succeeded, string[] Errors);
public sealed record AccountProfile(Guid Id, string Email, bool EmailConfirmed, string? PendingEmail);
public sealed record SessionRecord(Guid Id, string Device, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Current);
public interface IAccountEmailSender
{
    Task SendAsync(string recipient, string subject, string text, CancellationToken cancellationToken);
}
public interface IAccountService
{
    Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken cancellationToken);
    Task<LoginResult> AuthenticateAsync(string email, string password, CancellationToken cancellationToken, string device = "Browser");
    Task<bool> ValidateSessionAsync(Guid id, string stamp, Guid sessionId, CancellationToken cancellationToken);
    Task RevokeSessionsAsync(Guid id, CancellationToken cancellationToken);
    Task RequestVerificationAsync(string email, CancellationToken cancellationToken);
    Task<bool> ConfirmEmailAsync(Guid id, string token, CancellationToken cancellationToken);
    Task RequestResetAsync(string email, CancellationToken cancellationToken);
    Task<AccountResult> ResetPasswordAsync(Guid id, string token, string password, CancellationToken cancellationToken);
    Task<AccountProfile> ProfileAsync(Guid id, CancellationToken cancellationToken);
    Task<AccountResult> ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken cancellationToken);
    Task<AccountResult> RequestEmailChangeAsync(Guid id, string email, string password, CancellationToken cancellationToken);
    Task<bool> ConfirmEmailChangeAsync(Guid id, string email, string token, CancellationToken cancellationToken);
    Task<IReadOnlyList<SessionRecord>> SessionsAsync(Guid id, Guid currentSession, CancellationToken cancellationToken);
    Task<bool> RevokeSessionAsync(Guid id, Guid sessionId, CancellationToken cancellationToken);
}

public sealed class AccountEmailException : Exception { }
