using IGDash.Core.Api.Security;
using IGDash.Core.Application.Logs;

namespace IGDash.Core.Api.Endpoints;

internal static class LogEndpoints
{
    internal sealed record CreateLogRequest(string? Message, string? Level, DateTimeOffset? OccurredAt);
    public static void MapLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/logs").WithTags("Logs").RequireAuthorization().RequireRateLimiting("logs");
        group.MapPost("", async (CreateLogRequest request, LogService logs, CancellationToken cancellationToken) =>
            Results.Created("/api/logs", await logs.CreateAsync(request.Message, request.Level, request.OccurredAt, cancellationToken)))
            .AddEndpointFilter<CsrfFilter>();
        group.MapGet("", async (LogService logs, CancellationToken cancellationToken, int page = 1, int pageSize = 25, string? level = null) =>
            Results.Ok(await logs.ListAsync(page, pageSize, level, cancellationToken)));
    }
}
