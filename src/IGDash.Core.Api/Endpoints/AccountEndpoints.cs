using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using IGDash.Core.Api.Security;
using IGDash.Core.Application.Accounts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace IGDash.Core.Api.Endpoints;

internal static class AccountEndpoints
{
    internal sealed record Credentials(string? Email, string? Password);
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Accounts");
        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        group.MapPost("/register", async (Credentials request, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            if (!Valid(request)) return Results.Problem(statusCode: 400, title: "Provide a valid email and a password of 12–128 characters.");
            var result = await accounts.RegisterAsync(request.Email!, request.Password!, cancellationToken);
            if (result.Conflict) return Results.Problem(statusCode: 409, title: "Unable to register with these details.");
            return result.Succeeded ? Results.Created("/api/auth/me", (object?)null)
                : Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = result.Errors });
        }).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("accounts");
        group.MapPost("/login", async (Credentials request, HttpContext context, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            if (!Valid(request)) return Results.Problem(statusCode: 400, title: "Provide a valid email and a password of 12–128 characters.");
            var login = await accounts.AuthenticateAsync(request.Email!, request.Password!, cancellationToken, context.Request.Headers.UserAgent.ToString());
            if (login.NeedsVerification) return Results.Problem(statusCode: 403, title: "Verify your email before signing in. You can request a new verification email.");
            var account = login.Session;
            if (account is null) return Results.Problem(statusCode: 401, title: "Invalid email or password.");
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.Email, account.Email), new Claim("igdash:stamp", account.SecurityStamp), new Claim("igdash:session", account.SessionId.ToString()) };
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            return Results.NoContent();
        }).AddEndpointFilter<CsrfFilter>().RequireRateLimiting("accounts");
        group.MapPost("/logout", async (HttpContext context, IAccountService accounts, CancellationToken cancellationToken) =>
        {
            await accounts.RevokeSessionsAsync(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken);
            await context.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization().AddEndpointFilter<CsrfFilter>();
        group.MapGet("/me", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { id = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), email = context.User.FindFirstValue(ClaimTypes.Email) });
        }).RequireAuthorization();
    }
    private static bool Valid(Credentials request) => request.Email is { Length: > 0 and <= 254 }
        && !request.Email.Contains('\r') && !request.Email.Contains('\n')
        && new EmailAddressAttribute().IsValid(request.Email.Trim())
        && request.Password is { Length: >= 12 and <= 128 };
}
