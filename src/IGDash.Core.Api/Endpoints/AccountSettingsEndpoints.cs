using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using IGDash.Core.Api.Security;
using IGDash.Core.Application.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace IGDash.Core.Api.Endpoints;

internal static class AccountSettingsEndpoints
{
    internal sealed record EmailRequest(string? Email);
    internal sealed record TokenRequest(Guid UserId, string? Token);
    internal sealed record ResetRequest(Guid UserId, string? Token, string? Password);
    internal sealed record PasswordChange(string? CurrentPassword, string? NewPassword);
    internal sealed record EmailChange(string? Email, string? CurrentPassword);
    internal sealed record EmailConfirmation(Guid UserId, string? Email, string? Token);
    public static void MapAccountSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Account settings");
        group.MapPost("/verification/request", async (EmailRequest request, IAccountService accounts, CancellationToken token) =>
        {
            if (!ValidEmail(request.Email)) return Invalid("Provide a valid email address.");
            await accounts.RequestVerificationAsync(request.Email!, token); return Results.Accepted();
        }).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("accounts");
        group.MapPost("/verification/confirm", async (TokenRequest request, IAccountService accounts, CancellationToken token) =>
            ValidToken(request.Token) && await accounts.ConfirmEmailAsync(request.UserId, request.Token!, token)
                ? Results.NoContent() : InvalidLink()).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("accounts");
        group.MapPost("/password/forgot", async (EmailRequest request, IAccountService accounts, CancellationToken token) =>
        {
            if (!ValidEmail(request.Email)) return Invalid("Provide a valid email address.");
            await accounts.RequestResetAsync(request.Email!, token); return Results.Accepted();
        }).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("accounts");
        group.MapPost("/password/reset", async (ResetRequest request, HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            if (!ValidToken(request.Token) || !ValidPassword(request.Password)) return Invalid("Provide a valid link and a password of 12–128 characters.");
            var result = await accounts.ResetPasswordAsync(request.UserId, request.Token!, request.Password!, token);
            if (!result.Succeeded) return Errors(result);
            // Only clear this browser's cookie if it belongs to the account being recovered.
            if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) == request.UserId.ToString()) await context.SignOutAsync();
            return Results.NoContent();
        }).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("accounts");
        group.MapGet("/profile", async (HttpContext context, IAccountService accounts, CancellationToken token) => Results.Ok(await accounts.ProfileAsync(UserId(context), token))).RequireAuthorization().RequireRateLimiting("settings");
        group.MapPost("/password/change", async (PasswordChange request, HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            if (!ValidPassword(request.CurrentPassword) || !ValidPassword(request.NewPassword)) return Invalid("Passwords must contain 12–128 characters.");
            var result = await accounts.ChangePasswordAsync(UserId(context), request.CurrentPassword!, request.NewPassword!, token);
            if (!result.Succeeded) return Errors(result);
            await context.SignOutAsync(); return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<CsrfFilter>().RequireRateLimiting("settings");
        group.MapPost("/email/change", async (EmailChange request, HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            if (!ValidEmail(request.Email) || !ValidPassword(request.CurrentPassword)) return Invalid("Provide a valid email address and current password.");
            var result = await accounts.RequestEmailChangeAsync(UserId(context), request.Email!, request.CurrentPassword!, token);
            return result.Succeeded ? Results.Accepted() : Errors(result);
        }).RequireAuthorization().AddEndpointFilter<CsrfFilter>().RequireRateLimiting("settings");
        group.MapPost("/email/confirm", async (EmailConfirmation request, HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            if (!ValidEmail(request.Email) || !ValidToken(request.Token) || !await accounts.ConfirmEmailChangeAsync(request.UserId, request.Email!, request.Token!, token)) return InvalidLink();
            if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) == request.UserId.ToString()) await context.SignOutAsync();
            return Results.NoContent();
        }).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("accounts");
        group.MapGet("/sessions", async (HttpContext context, IAccountService accounts, CancellationToken token) => Results.Ok(await accounts.SessionsAsync(UserId(context), SessionId(context), token))).RequireAuthorization().RequireRateLimiting("settings");
        group.MapPost("/sessions/{id:guid}/revoke", async (Guid id, HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            if (!await accounts.RevokeSessionAsync(UserId(context), id, token)) return Results.NotFound();
            if (id == SessionId(context)) await context.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<CsrfFilter>().RequireRateLimiting("settings");
        group.MapPost("/sessions/revoke-all", async (HttpContext context, IAccountService accounts, CancellationToken token) =>
        {
            await accounts.RevokeSessionsAsync(UserId(context), token); await context.SignOutAsync(); return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<CsrfFilter>().RequireRateLimiting("settings");
    }
    private static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static Guid SessionId(HttpContext context) => Guid.Parse(context.User.FindFirstValue("igdash:session")!);
    private static bool ValidEmail(string? email) => email is { Length: > 0 and <= 254 } && !email.Contains('\r') && !email.Contains('\n') && new EmailAddressAttribute().IsValid(email.Trim());
    private static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 128 };
    private static bool ValidToken(string? token) => token is { Length: > 0 and <= 4096 };
    private static IResult Invalid(string message) => Results.Problem(statusCode: 400, title: message);
    private static IResult InvalidLink() => Invalid("This link is invalid or has expired. Request a new one.");
    private static IResult Errors(AccountResult result) => Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = result.Errors });
}
