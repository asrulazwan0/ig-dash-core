namespace IGDash.Core.Domain.Logs;

public sealed class LogEntry
{
    private LogEntry() { }
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Message { get; private set; } = "";
    public string Level { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
    public string[] Tags { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    public static LogEntry Create(Guid ownerId, string message, string level, DateTimeOffset occurredAt, string[]? tags = null)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner is required.");
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length > 2000)
            throw new ArgumentException("Message must contain 1–2000 characters.");
        if (level is not ("info" or "warning" or "error")) throw new ArgumentException("Unsupported level.");
        if (occurredAt.Offset != TimeSpan.Zero) throw new ArgumentException("A UTC timestamp is required.");
        return new() { Id = Guid.NewGuid(), OwnerId = ownerId, Message = message.Trim(), Level = level,
            OccurredAt = occurredAt.AddTicks(-(occurredAt.Ticks % 10)), Tags = NormalizeTags(tags),
            CreatedAt = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero) };
    }
    public void Update(string message, string level, DateTimeOffset occurredAt, string[]? tags)
    {
        var validated = Create(OwnerId, message, level, occurredAt, tags);
        Message = validated.Message; Level = validated.Level; OccurredAt = validated.OccurredAt; Tags = validated.Tags;
    }
    public static string[] NormalizeTags(string[]? tags)
    {
        if (tags is null) return [];
        if (tags.Length > 10) throw new ArgumentException("Use at most 10 tags.");
        var normalized = tags.Select(tag => tag?.Trim().ToLowerInvariant() ?? "").ToArray();
        if (normalized.Any(tag => tag.Length is < 1 or > 30 || tag.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_'))))
            throw new ArgumentException("Tags must contain 1–30 lowercase letters, numbers, hyphens, or underscores.");
        return normalized.Distinct().Order().ToArray();
    }
}
