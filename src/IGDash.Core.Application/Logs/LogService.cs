using IGDash.Core.Domain.Logs;

namespace IGDash.Core.Application.Logs;

public interface ICurrentUser { Guid Id { get; } }
public sealed record LogRecord(Guid Id, string Message, string Level, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt);
public sealed record LogPage(IReadOnlyList<LogRecord> Items, int Page, int PageSize, int Total);
public interface ILogRepository
{
    Task AddAsync(LogEntry log, CancellationToken cancellationToken);
    Task<LogPage> ListAsync(Guid ownerId, int page, int pageSize, string? level, CancellationToken cancellationToken);
}
public sealed class LogValidationException(string message) : Exception(message);
public sealed class LogService(ICurrentUser currentUser, ILogRepository logs)
{
    public async Task<LogRecord> CreateAsync(string? message, string? level, DateTimeOffset? occurredAt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > 2000)
            throw new LogValidationException("Message must contain 1–2000 characters.");
        if (level is not ("info" or "warning" or "error")) throw new LogValidationException("Choose info, warning, or error.");
        if (occurredAt is null || occurredAt.Value.Offset != TimeSpan.Zero)
            throw new LogValidationException("Provide a UTC occurredAt timestamp.");
        var log = LogEntry.Create(currentUser.Id, message, level, occurredAt.Value);
        await logs.AddAsync(log, cancellationToken);
        return new(log.Id, log.Message, log.Level, log.OccurredAt, log.CreatedAt);
    }

    public Task<LogPage> ListAsync(int page, int pageSize, string? level, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new LogValidationException("Use page >= 1 and pageSize between 1 and 100.");
        if (level is not (null or "info" or "warning" or "error"))
            throw new LogValidationException("Choose info, warning, or error.");
        return logs.ListAsync(currentUser.Id, page, pageSize, level, cancellationToken);
    }
}
