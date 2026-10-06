using IGDash.Core.Application.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IGDash.Core.Infrastructure.Identity;

internal sealed class AccountService(UserManager<ApplicationUser> users) : IAccountService
{
    private static readonly ApplicationUser DummyUser = new();
    private static readonly PasswordHasher<ApplicationUser> Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, Guid.NewGuid().ToString());

    public async Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new ApplicationUser { UserName = email.Trim(), Email = email.Trim() };
        try
        {
            var result = await users.CreateAsync(user, password);
            var conflict = result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName");
            return new(result.Succeeded, conflict, conflict ? [] : result.Errors.Select(e => e.Description).ToArray());
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new(false, true, []);
        }
    }

    public async Task<AccountSession?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null)
        {
            Hasher.VerifyHashedPassword(DummyUser, DummyHash, password);
            return null;
        }
        if (await users.IsLockedOutAsync(user)) return null;
        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return null;
        }
        await users.ResetAccessFailedCountAsync(user);
        return new(user.Id, user.Email!, user.SecurityStamp!);
    }

    public async Task<bool> ValidateSessionAsync(Guid id, string stamp, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id.ToString());
        return user is not null && string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)
            && !await users.IsLockedOutAsync(user);
    }

    public async Task RevokeSessionsAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByIdAsync(id.ToString());
        if (user is not null && !(await users.UpdateSecurityStampAsync(user)).Succeeded)
            throw new InvalidOperationException("Session revocation failed.");
    }
}
