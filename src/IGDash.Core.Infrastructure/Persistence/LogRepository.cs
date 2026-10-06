using IGDash.Core.Application.Logs;
using IGDash.Core.Domain.Logs;
using Microsoft.EntityFrameworkCore;

namespace IGDash.Core.Infrastructure.Persistence;

internal sealed class LogRepository(ApplicationDbContext db) : ILogRepository
{
    public async Task AddAsync(LogEntry log, CancellationToken cancellationToken)
    {
        await db.LogEntries.AddAsync(log, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<LogPage> ListAsync(Guid ownerId, int page, int pageSize, string? level, CancellationToken cancellationToken)
    {
        var query = db.LogEntries.AsNoTracking().Where(log => log.OwnerId == ownerId);
        if (level is not null) query = query.Where(log => log.Level == level);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(log => log.OccurredAt).ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(log => new LogRecord(log.Id, log.Message, log.Level, log.OccurredAt, log.CreatedAt))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }
}
