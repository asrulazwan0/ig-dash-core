using System.Globalization;
using System.Text;
using IGDash.Core.Api.Security;
using IGDash.Core.Application.Logs;

namespace IGDash.Core.Api.Endpoints;

internal static class LogEndpoints
{
    internal sealed record CreateLogRequest(string? Message, string? Level, DateTimeOffset? OccurredAt, string[]? Tags = null);
    public static void MapLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/logs").WithTags("Logs").RequireAuthorization().RequireRateLimiting("logs");
        group.MapPost("", async (CreateLogRequest request, LogService logs, CancellationToken cancellationToken) =>
            Results.Created("/api/logs", await logs.CreateAsync(request.Message, request.Level, request.OccurredAt, cancellationToken, request.Tags)))
            .AddEndpointFilter<CsrfFilter>();
        group.MapPut("/{id:guid}", async (Guid id, CreateLogRequest request, LogService logs, CancellationToken cancellationToken) =>
        {
            var record = await logs.UpdateAsync(id, request.Message, request.Level, request.OccurredAt, request.Tags, cancellationToken);
            return record is null ? Results.NotFound() : Results.Ok(record);
        }).AddEndpointFilter<CsrfFilter>();
        group.MapDelete("/{id:guid}", async (Guid id, LogService logs, CancellationToken cancellationToken) =>
            await logs.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound()).AddEndpointFilter<CsrfFilter>();
        group.MapGet("", async (LogService logs, CancellationToken cancellationToken, int page = 1, int pageSize = 25,
            string? level = null, string? search = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? tag = null) =>
            Results.Ok(await logs.ListAsync(page, pageSize, new LogFilter(level, search, from, to, tag), cancellationToken)));
        group.MapGet("/summary", async (LogService logs, CancellationToken cancellationToken,
            string? level = null, string? search = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? tag = null) =>
            Results.Ok(await logs.SummaryAsync(new LogFilter(level, search, from, to, tag), cancellationToken)));
        group.MapGet("/tags", async (LogService logs, CancellationToken cancellationToken) => Results.Ok(await logs.TagsAsync(cancellationToken)));
        group.MapGet("/export", async (HttpContext context, LogService logs, CancellationToken cancellationToken,
            string? level = null, string? search = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? tag = null) =>
        {
            // Validate before sending headers; stream all matching records instead of only the visible page.
            var records = logs.ExportAsync(new LogFilter(level, search, from, to, tag), cancellationToken);
            context.Response.ContentType = "text/csv; charset=utf-8";
            context.Response.Headers.ContentDisposition = "attachment; filename=igdash-logs.csv";
            await context.Response.WriteAsync("\uFEFFId,Message,Level,OccurredAt,CreatedAt,Tags\r\n", cancellationToken);
            await foreach (var log in records)
            {
                var row = string.Join(",", new[] { log.Id.ToString(), log.Message, log.Level,
                    log.OccurredAt.ToString("O", CultureInfo.InvariantCulture), log.CreatedAt.ToString("O", CultureInfo.InvariantCulture), string.Join(";", log.Tags) }.Select(CsvCell));
                await context.Response.WriteAsync(row + "\r\n", Encoding.UTF8, cancellationToken);
            }
        });
    }
    private static string CsvCell(string value)
    {
        // Prefix potentially executable spreadsheet formulas, including leading whitespace/control characters.
        var probe = value.TrimStart();
        if (probe.Length > 0 && (probe[0] is '=' or '+' or '-' or '@' || value[0] is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
