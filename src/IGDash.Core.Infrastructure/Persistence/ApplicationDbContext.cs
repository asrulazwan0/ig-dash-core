using IGDash.Core.Infrastructure.Identity;
using IGDash.Core.Domain.Logs;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IGDash.Core.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().HasIndex(user => user.NormalizedEmail).IsUnique();
        var logs = builder.Entity<LogEntry>();
        logs.Property(log => log.Message).HasMaxLength(2000);
        logs.Property(log => log.Level).HasMaxLength(10);
        logs.HasIndex(log => new { log.OwnerId, log.OccurredAt, log.Id });
        logs.HasOne<ApplicationUser>().WithMany().HasForeignKey(log => log.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
