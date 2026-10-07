using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using IGDash.Core.Application.Logs;
using IGDash.Core.Domain.Logs;
using Microsoft.EntityFrameworkCore;

namespace IGDash.Core.Infrastructure.Persistence;

internal sealed class LogRepository(ApplicationDbContext db) : ILogRepository
{
    private static readonly Expression<Func<LogEntry, LogRecord>> Projection = log =>
        new LogRecord(log.Id, log.Message, log.Level, log.OccurredAt, log.CreatedAt, log.Tags);
    public async Task AddAsync(LogEntry log, CancellationToken cancellationToken)
    { await db.LogEntries.AddAsync(log, cancellationToken); await db.SaveChangesAsync(cancellationToken); }
    public Task<LogEntry?> FindAsync(Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        db.LogEntries.SingleOrDefaultAsync(log => log.OwnerId == ownerId && log.Id == id, cancellationToken);
    public async Task SaveAsync(CancellationToken cancellationToken) => await db.SaveChangesAsync(cancellationToken);
    public async Task DeleteAsync(LogEntry log, CancellationToken cancellationToken)
    { db.LogEntries.Remove(log); await db.SaveChangesAsync(cancellationToken); }
    private IQueryable<LogEntry> Filter(Guid ownerId, LogFilter filter)
    {
        var query = db.LogEntries.AsNoTracking().Where(log => log.OwnerId == ownerId);
        if (filter.Level is not null) query = query.Where(log => log.Level == filter.Level);
        if (filter.Search is not null) query = query.Where(log => log.Message.ToLower().Contains(filter.Search.ToLower()));
        if (filter.From is not null) query = query.Where(log => log.OccurredAt >= filter.From);
        if (filter.To is not null) query = query.Where(log => log.OccurredAt < filter.To);
        if (filter.Tag is not null) query = query.Where(log => log.Tags.Contains(filter.Tag));
        return query;
    }
    private static IOrderedQueryable<LogEntry> Ordered(IQueryable<LogEntry> query) => query.OrderByDescending(log => log.OccurredAt).ThenByDescending(log => log.Id);
    public async Task<LogPage> ListAsync(Guid ownerId, int page, int pageSize, LogFilter filter, CancellationToken cancellationToken)
    {
        var query = Filter(ownerId, filter);
        var total = await query.CountAsync(cancellationToken);
        var items = await Ordered(query).Skip((page - 1) * pageSize).Take(pageSize).Select(Projection).ToListAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }
    public async Task<LogSummary> SummaryAsync(Guid ownerId, LogFilter filter, CancellationToken cancellationToken)
    {
        var query = Filter(ownerId, filter);
        var counts = await query.GroupBy(log => log.Level).Select(group => new { Level = group.Key, Count = group.Count() }).ToDictionaryAsync(x => x.Level, x => x.Count, cancellationToken);
        var chartEnd = filter.To is null ? DateTimeOffset.UtcNow : filter.To.Value == DateTimeOffset.MinValue ? DateTimeOffset.MinValue : filter.To.Value.AddTicks(-1);
        var chartTo = DateOnly.FromDateTime(chartEnd.UtcDateTime);
        if (filter.To is null && filter.From is not null && DateOnly.FromDateTime(filter.From.Value.UtcDateTime) > chartTo)
            chartTo = DateOnly.FromDateTime(filter.From.Value.UtcDateTime);
        var chartFrom = chartTo.DayNumber >= 29 ? chartTo.AddDays(-29) : DateOnly.MinValue;
        if (filter.From is not null && DateOnly.FromDateTime(filter.From.Value.UtcDateTime) > chartFrom)
            chartFrom = DateOnly.FromDateTime(filter.From.Value.UtcDateTime);
        var start = new DateTimeOffset(chartFrom.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        // An exclusive end is unnecessary for the max supported date; the filter already limits the query.
        var chartQuery = query.Where(log => log.OccurredAt >= start);
        if (chartTo != DateOnly.MaxValue)
        {
            var end = new DateTimeOffset(chartTo.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            chartQuery = chartQuery.Where(log => log.OccurredAt < end);
        }
        var daily = await chartQuery.GroupBy(log => log.OccurredAt.DateTime.Date)
            .Select(group => new { Date = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
        var activity = Enumerable.Range(0, chartTo.DayNumber - chartFrom.DayNumber + 1)
            .Select(offset => chartFrom.AddDays(offset)).Select(date => new ActivityDay(date, daily.Where(day => DateOnly.FromDateTime(day.Date) == date).Sum(day => day.Count))).ToArray();
        var recent = await Ordered(query).Take(5).Select(Projection).ToListAsync(cancellationToken);
        return new(counts.Values.Sum(), counts.GetValueOrDefault("info"), counts.GetValueOrDefault("warning"), counts.GetValueOrDefault("error"), chartFrom, chartTo, activity, recent);
    }
    public async Task<string[]> TagsAsync(Guid ownerId, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<string>($"SELECT DISTINCT unnest(\"Tags\") AS \"Value\" FROM \"LogEntries\" WHERE \"OwnerId\" = {ownerId}")
            .OrderBy(tag => tag).ToArrayAsync(cancellationToken);
    public async IAsyncEnumerable<LogRecord> ExportAsync(Guid ownerId, LogFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var log in Ordered(Filter(ownerId, filter)).Select(Projection).AsAsyncEnumerable().WithCancellation(cancellationToken)) yield return log;
    }
}
