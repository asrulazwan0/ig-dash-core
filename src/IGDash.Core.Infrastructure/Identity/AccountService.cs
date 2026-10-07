using System.Text;
using IGDash.Core.Application.Accounts;
using IGDash.Core.Infrastructure.Email;
using IGDash.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IGDash.Core.Infrastructure.Identity;

internal sealed class AccountService(UserManager<ApplicationUser> users, ApplicationDbContext db,
    IAccountEmailSender mail, IOptions<EmailOptions> options, ILogger<AccountService> logger) : IAccountService
{
    private static readonly ApplicationUser DummyUser = new();
    private static readonly PasswordHasher<ApplicationUser> Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, Guid.NewGuid().ToString());
    private static readonly AccountResult InvalidLink = new(false, ["This link is invalid or has expired. Request a new one."]);

    public async Task<RegistrationResult> RegisterAsync(string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = new ApplicationUser { UserName = email.Trim(), Email = email.Trim() };
        try
        {
            var result = await users.CreateAsync(user, password);
            var conflict = result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName");
            if (result.Succeeded) await VerificationMailAsync(user, cancellationToken);
            return new(result.Succeeded, conflict, conflict ? [] : result.Errors.Select(e => e.Description).ToArray());
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return new(false, true, []); }
    }
    public async Task<LoginResult> AuthenticateAsync(string email, string password, CancellationToken cancellationToken, string device = "Browser")
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null) { Hasher.VerifyHashedPassword(DummyUser, DummyHash, password); return new(null); }
        if (await users.IsLockedOutAsync(user)) return new(null);
        if (!await users.CheckPasswordAsync(user, password)) { await users.AccessFailedAsync(user); return new(null); }
        await users.ResetAccessFailedCountAsync(user);
        if (!user.EmailConfirmed) return new(null, true);
        // Remove expired/revoked rows and bound active sessions to keep the account list manageable.
        var now = DateTimeOffset.UtcNow;
        await db.BrowserSessions.Where(session => session.UserId == user.Id && (session.ExpiresAt <= now || session.RevokedAt != null)).ExecuteDeleteAsync(cancellationToken);
        var active = await db.BrowserSessions.Where(session => session.UserId == user.Id).OrderByDescending(session => session.CreatedAt).Skip(19).ToListAsync(cancellationToken);
        db.BrowserSessions.RemoveRange(active);
        var session = new BrowserSession { UserId = user.Id, Device = DeviceName(device), CreatedAt = now, ExpiresAt = now.AddHours(8) };
        db.BrowserSessions.Add(session); await db.SaveChangesAsync(cancellationToken);
        return new(new(user.Id, user.Email!, user.SecurityStamp!, session.Id));
    }
    private static string DeviceName(string agent) => agent.Contains("Edg/", StringComparison.Ordinal) ? "Microsoft Edge"
        : agent.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox"
        : agent.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome"
        : agent.Contains("Safari/", StringComparison.Ordinal) ? "Safari" : "Browser";
    public async Task<bool> ValidateSessionAsync(Guid id, string stamp, Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        return user is { EmailConfirmed: true } && string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal)
            && !await users.IsLockedOutAsync(user)
            && await db.BrowserSessions.AnyAsync(session => session.Id == sessionId && session.UserId == id && session.RevokedAt == null && session.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);
    }
    public async Task RevokeSessionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is not null && !(await users.UpdateSecurityStampAsync(user)).Succeeded) throw new InvalidOperationException("Session revocation failed.");
        await db.BrowserSessions.Where(session => session.UserId == id).ExecuteDeleteAsync(cancellationToken);
    }
    private string Link(string action, Guid id, string token, string? email = null)
    {
        var fragment = $"account-action={action}&user={id}&token={WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))}";
        if (email is not null) fragment += "&email=" + Uri.EscapeDataString(email);
        return options.Value.PublicOrigin.TrimEnd('/') + "/#" + fragment;
    }
    private static string? Decode(string encoded)
    {
        if (encoded.Length is < 1 or > 4096) return null;
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded)); }
        catch (FormatException) { return null; }
    }
    private async Task<bool> ReserveMailAsync(Guid id, string purpose, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow; var cutoff = now.AddMinutes(-1);
        var query = db.Users.Where(user => user.Id == id);
        var changed = purpose switch
        {
            "verify" => await query.Where(user => user.LastVerificationMailAt == null || user.LastVerificationMailAt < cutoff)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastVerificationMailAt, now), cancellationToken),
            "reset" => await query.Where(user => user.LastResetMailAt == null || user.LastResetMailAt < cutoff)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastResetMailAt, now), cancellationToken),
            "change" => await query.Where(user => user.LastEmailChangeMailAt == null || user.LastEmailChangeMailAt < cutoff)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.LastEmailChangeMailAt, now), cancellationToken),
            _ => throw new InvalidOperationException("Unknown account email purpose.")
        };
        return changed == 1;
    }
    private async Task SendAsync(string recipient, string subject, string text, CancellationToken cancellationToken)
    {
        try { await mail.SendAsync(recipient, subject, text, cancellationToken); }
        catch (AccountEmailException) { logger.LogWarning("Account email delivery failed; a new link can be requested after the cooldown."); }
    }
    private async Task VerificationMailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (!await ReserveMailAsync(user.Id, "verify", cancellationToken)) return;
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        await SendAsync(user.Email!, "Verify your IGDash email", "Verify your email using this link (expires in one hour):\n\n" + Link("verify", user.Id, token), cancellationToken);
    }
    public async Task RequestVerificationAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is { EmailConfirmed: false }) await VerificationMailAsync(user, cancellationToken);
    }
    public async Task<bool> ConfirmEmailAsync(Guid id, string token, CancellationToken cancellationToken)
    {
        var decoded = Decode(token); if (decoded is null) return false;
        var user = await users.FindByIdAsync(id.ToString());
        return user is { EmailConfirmed: false } && (await users.ConfirmEmailAsync(user, decoded)).Succeeded;
    }
    public async Task RequestResetAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is not { EmailConfirmed: true } || !await ReserveMailAsync(user.Id, "reset", cancellationToken)) return;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        await SendAsync(user.Email!, "Reset your IGDash password", "Reset your password using this link (expires in one hour):\n\n" + Link("reset", user.Id, token) + "\n\nIf you did not request this, ignore this email.", cancellationToken);
    }
    public async Task<AccountResult> ResetPasswordAsync(Guid id, string token, string password, CancellationToken cancellationToken)
    {
        var decoded = Decode(token); if (decoded is null) return InvalidLink;
        var user = await users.FindByIdAsync(id.ToString()); if (user is not { EmailConfirmed: true }) return InvalidLink;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        user.PendingEmail = null;
        var result = await users.ResetPasswordAsync(user, decoded, password);
        if (!result.Succeeded) return result.Errors.Any(e => e.Code == "InvalidToken") ? InvalidLink : Result(result);
        await db.BrowserSessions.Where(session => session.UserId == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, []);
    }
    public async Task<AccountProfile> ProfileAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new InvalidOperationException("Account unavailable.");
        return new(user.Id, user.Email!, user.EmailConfirmed, user.PendingEmail);
    }
    public async Task<AccountResult> ChangePasswordAsync(Guid id, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new InvalidOperationException("Account unavailable.");
        if (!await ReauthenticateAsync(user, currentPassword)) return new(false, ["Current password is incorrect."]);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        user.PendingEmail = null;
        var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!result.Succeeded) return Result(result);
        await db.BrowserSessions.Where(session => session.UserId == id).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, []);
    }
    public async Task<AccountResult> RequestEmailChangeAsync(Guid id, string email, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new InvalidOperationException("Account unavailable.");
        if (!await ReauthenticateAsync(user, password)) return new(false, ["Current password is incorrect."]);
        email = email.Trim();
        if (string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)) return new(false, ["Choose a different email address."]);
        if (!await ReserveMailAsync(id, "change", cancellationToken)) return new(false, ["Please wait one minute before requesting another account email."]);
        user.PendingEmail = email;
        // ExecuteUpdate bypassed tracking; refresh the cooldown on the tracked user before saving.
        user.LastEmailChangeMailAt = DateTimeOffset.UtcNow;
        var updated = await users.UpdateAsync(user); if (!updated.Succeeded) return Result(updated);
        var token = await users.GenerateChangeEmailTokenAsync(user, email);
        await SendAsync(email, "Confirm your IGDash email change", "Confirm your new email using this link (expires in one hour):\n\n" + Link("email-change", id, token, email), cancellationToken);
        await SendAsync(user.Email!, "IGDash email change requested", "A change to your account email was requested. Your current email remains active until the new address is verified. If this was not you, change your password to cancel it and revoke your sessions.", cancellationToken);
        return new(true, []);
    }
    public async Task<bool> ConfirmEmailChangeAsync(Guid id, string email, string token, CancellationToken cancellationToken)
    {
        var decoded = Decode(token); if (decoded is null) return false;
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || !string.Equals(user.PendingEmail, email, StringComparison.Ordinal)) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!(await users.ChangeEmailAsync(user, email, decoded)).Succeeded) return false;
            if (!(await users.SetUserNameAsync(user, email)).Succeeded) return false;
            user.PendingEmail = null;
            if (!(await users.UpdateSecurityStampAsync(user)).Succeeded) return false;
            await db.BrowserSessions.Where(session => session.UserId == id).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken); return true;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return false; }
    }
    public async Task<IReadOnlyList<SessionRecord>> SessionsAsync(Guid id, Guid currentSession, CancellationToken cancellationToken) =>
        await db.BrowserSessions.AsNoTracking().Where(session => session.UserId == id && session.RevokedAt == null && session.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(session => session.CreatedAt).Select(session => new SessionRecord(session.Id, session.Device, session.CreatedAt, session.ExpiresAt, session.Id == currentSession)).ToListAsync(cancellationToken);
    public async Task<bool> RevokeSessionAsync(Guid id, Guid sessionId, CancellationToken cancellationToken) =>
        await db.BrowserSessions.Where(session => session.UserId == id && session.Id == sessionId && session.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.RevokedAt, DateTimeOffset.UtcNow), cancellationToken) == 1;
    private async Task<bool> ReauthenticateAsync(ApplicationUser user, string password)
    {
        if (await users.IsLockedOutAsync(user)) return false;
        if (!await users.CheckPasswordAsync(user, password)) { await users.AccessFailedAsync(user); return false; }
        await users.ResetAccessFailedCountAsync(user); return true;
    }
    private static AccountResult Result(IdentityResult result) => new(result.Succeeded, result.Errors.Select(error => error.Description).ToArray());
}
