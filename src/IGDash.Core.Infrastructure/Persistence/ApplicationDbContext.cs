using IGDash.Core.Infrastructure.Identity;
using IGDash.Core.Domain.Logs;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IGDash.Core.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<BrowserSession> BrowserSessions => Set<BrowserSession>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.Entity<ApplicationUser>().Property(user => user.PendingEmail).HasMaxLength(254);
        var sessions = builder.Entity<BrowserSession>();
        sessions.Property(session => session.Device).HasMaxLength(120);
        sessions.HasIndex(session => new { session.UserId, session.ExpiresAt });
        sessions.HasOne<ApplicationUser>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        var logs = builder.Entity<LogEntry>();
        logs.Property(log => log.Message).HasMaxLength(2000);
        logs.Property(log => log.Level).HasMaxLength(10);
        logs.Property(log => log.Tags).HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
        logs.HasIndex(log => log.Tags).HasMethod("gin");
        logs.HasIndex(log => new { log.OwnerId, log.OccurredAt, log.Id });
        logs.HasOne<ApplicationUser>().WithMany().HasForeignKey(log => log.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
