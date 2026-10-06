namespace IGDash.Core.Domain.Logs;

public sealed class LogEntry
{
    private LogEntry() { }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Message { get; private set; } = "";
    public string Level { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static LogEntry Create(Guid ownerId, string message, string level, DateTimeOffset occurredAt)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.");
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > 2000)
            throw new ArgumentException("Message must contain 1–2000 characters.");
        if (level is not ("info" or "warning" or "error")) throw new ArgumentException("Unsupported level.");
        if (occurredAt.Offset != TimeSpan.Zero) throw new ArgumentException("A UTC timestamp is required.");
        return new() { Id = Guid.NewGuid(), OwnerId = ownerId, Message = message.Trim(), Level = level,
            OccurredAt = occurredAt, CreatedAt = DateTimeOffset.UtcNow };
    }
}
