using IGDash.Core.Domain.Logs;

namespace IGDash.Core.Application.Logs;

public interface ICurrentUser { Guid Id { get; } }
public sealed record LogRecord(Guid Id, string Message, string Level, DateTimeOffset OccurredAt, DateTimeOffset CreatedAt, string[] Tags);
public sealed record LogPage(IReadOnlyList<LogRecord> Items, int Page, int PageSize, int Total);
public sealed record LogFilter(string? Level = null, string? Search = null, DateTimeOffset? From = null, DateTimeOffset? To = null, string? Tag = null);
public sealed record ActivityDay(DateOnly Date, int Count);
public sealed record LogSummary(int Total, int Info, int Warning, int Error, DateOnly ChartFrom, DateOnly ChartTo,
    IReadOnlyList<ActivityDay> Activity, IReadOnlyList<LogRecord> Recent);
public interface ILogRepository
{
    Task AddAsync(LogEntry log, CancellationToken cancellationToken);
    Task<LogEntry?> FindAsync(Guid ownerId, Guid id, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task DeleteAsync(LogEntry log, CancellationToken cancellationToken);
    Task<LogPage> ListAsync(Guid ownerId, int page, int pageSize, LogFilter filter, CancellationToken cancellationToken);
    Task<LogSummary> SummaryAsync(Guid ownerId, LogFilter filter, CancellationToken cancellationToken);
    Task<string[]> TagsAsync(Guid ownerId, CancellationToken cancellationToken);
    IAsyncEnumerable<LogRecord> ExportAsync(Guid ownerId, LogFilter filter, CancellationToken cancellationToken);
}
public sealed class LogValidationException(string message) : Exception(message);
public sealed class LogService(ICurrentUser currentUser, ILogRepository logs)
{
    private static LogEntry Validate(Guid owner, string? message, string? level, DateTimeOffset? occurredAt, string[]? tags)
    {
        if (occurredAt is null) throw new LogValidationException("Provide a UTC occurredAt timestamp.");
        try { return LogEntry.Create(owner, message!, level!, occurredAt.Value, tags); }
        catch (ArgumentException error) { throw new LogValidationException(error.Message); }
    }
    public async Task<LogRecord> CreateAsync(string? message, string? level, DateTimeOffset? occurredAt, CancellationToken cancellationToken, string[]? tags = null)
    {
        var log = Validate(currentUser.Id, message, level, occurredAt, tags);
        await logs.AddAsync(log, cancellationToken);
        return Record(log);
    }
    public async Task<LogRecord?> UpdateAsync(Guid id, string? message, string? level, DateTimeOffset? occurredAt, string[]? tags, CancellationToken cancellationToken)
    {
        var existing = await logs.FindAsync(currentUser.Id, id, cancellationToken);
        if (existing is null) return null;
        var valid = Validate(currentUser.Id, message, level, occurredAt, tags);
        existing.Update(valid.Message, valid.Level, valid.OccurredAt, valid.Tags);
        await logs.SaveAsync(cancellationToken);
        return Record(existing);
    }
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var log = await logs.FindAsync(currentUser.Id, id, cancellationToken);
        if (log is null) return false;
        await logs.DeleteAsync(log, cancellationToken); return true;
    }
    private static LogRecord Record(LogEntry log) => new(log.Id, log.Message, log.Level, log.OccurredAt, log.CreatedAt, log.Tags);
    public static LogFilter ValidateFilter(LogFilter filter)
    {
        if (filter.Level is not (null or "info" or "warning" or "error")) throw new LogValidationException("Choose info, warning, or error.");
        if (filter.Search?.Length > 200) throw new LogValidationException("Search must contain at most 200 characters.");
        if ((filter.From is not null && filter.From.Value.Offset != TimeSpan.Zero)
            || (filter.To is not null && filter.To.Value.Offset != TimeSpan.Zero))
            throw new LogValidationException("Use UTC timestamps for date filters.");
        if (filter.From >= filter.To) throw new LogValidationException("From must be earlier than to (the end is exclusive).");
        string? tag = null;
        if (filter.Tag is not null)
        {
            try { tag = LogEntry.NormalizeTags([filter.Tag])[0]; }
            catch (ArgumentException error) { throw new LogValidationException(error.Message); }
        }
        return filter with { Search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim(), Tag = tag };
    }
    public Task<LogPage> ListAsync(int page, int pageSize, string? level, CancellationToken cancellationToken) => ListAsync(page, pageSize, new LogFilter(level), cancellationToken);
    public Task<LogPage> ListAsync(int page, int pageSize, LogFilter filter, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new LogValidationException("Use page >= 1 and pageSize between 1 and 100.");
        return logs.ListAsync(currentUser.Id, page, pageSize, ValidateFilter(filter), cancellationToken);
    }
    public Task<LogSummary> SummaryAsync(LogFilter filter, CancellationToken cancellationToken) => logs.SummaryAsync(currentUser.Id, ValidateFilter(filter), cancellationToken);
    public Task<string[]> TagsAsync(CancellationToken cancellationToken) => logs.TagsAsync(currentUser.Id, cancellationToken);
    public IAsyncEnumerable<LogRecord> ExportAsync(LogFilter filter, CancellationToken cancellationToken) => logs.ExportAsync(currentUser.Id, ValidateFilter(filter), cancellationToken);
}
