using System.Security.Cryptography;
using System.Text;
using IGDash.Core.Domain.Logs;
using IGDash.Core.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace IGDash.Core.Infrastructure.Persistence;

public sealed record SeedResult(int UsersCreated, int LogsCreated);
public sealed class DevelopmentSeeder(ApplicationDbContext db, UserManager<ApplicationUser> users, IHostEnvironment environment)
{
    public async Task<SeedResult> SeedAsync(string? password, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Demo seeding is available only in Development.");
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Set Seed:Password (Seed__Password) to a strong demo password before seeding.");
        if (password.Length < 12 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit) || password.All(char.IsLetterOrDigit))
            throw new InvalidOperationException("The demo password requires at least 12 characters, uppercase, lowercase, a number, and a symbol.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize seed runs so deterministic IDs cannot race. Nothing is deleted or overwritten.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73482019)", cancellationToken);
        var usersCreated = 0; var logsCreated = 0;
        string[] messages = ["Completed the morning planning session", "Followed up on a pending review", "Made progress on a personal goal", "Unexpected interruption during focused work", "Resolved a recurring issue", "Saved a useful idea for tomorrow", "A small milestone worth remembering", "Took a break and came back with a clearer plan"];
        string[] tags = ["work", "personal", "learning", "planning", "health"];
        for (var account = 0; account < 2; account++)
        {
            var email = account == 0 ? "demo.one@example.test" : "demo.two@example.test";
            var userId = StableId("demo-user-" + account);
            var user = await users.FindByEmailAsync(email);
            if (user is not null && user.Id != userId) throw new InvalidOperationException("A demo email belongs to an existing account. Seeding stopped without changing its data.");
            if (user is null)
            {
                user = new ApplicationUser { Id = userId, Email = email, UserName = email };
                var result = await users.CreateAsync(user, password);
                if (!result.Succeeded) throw new InvalidOperationException("Unable to create demo account: " + string.Join(" ", result.Errors.Select(error => error.Description)));
                usersCreated++;
            }
            var existing = (await db.LogEntries.Where(log => log.OwnerId == userId).Select(log => log.Id).ToListAsync(cancellationToken)).ToHashSet();
            for (var index = 0; index < 84; index++)
            {
                var id = StableId($"demo-log-{account}-{index}");
                if (existing.Contains(id)) continue;
                var message = $"{(account == 0 ? "Alex" : "Sam")}: {messages[index % messages.Length]} · day {index / 3 + 1}";
                if (index == 7) message += "\nNotes: kept the scope small, checked the result, and recorded the next steps. " + string.Concat(Enumerable.Repeat("The useful part was recording what changed, what I checked, and one concrete next step. That makes this entry easier to revisit when planning the following week. ", 3));
                if (index == 12) message += " — commas, \"quotes\", and Unicode ✓";
                var level = index % 7 == 0 ? "error" : index % 3 == 0 ? "warning" : "info";
                var occurredAt = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-(index / 3)).AddHours(9 + index % 3 * 3), TimeSpan.Zero);
                var log = LogEntry.Create(userId, message, level, occurredAt, [tags[(index + account) % tags.Length], "demo"]);
                db.Entry(log).Property(entry => entry.Id).CurrentValue = id;
                db.LogEntries.Add(log); logsCreated++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(usersCreated, logsCreated);
    }
    private static Guid StableId(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("igdash-development-v1:" + key)).AsSpan(0, 16));
}
