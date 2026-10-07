using Microsoft.AspNetCore.Identity;

namespace IGDash.Core.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string? PendingEmail { get; set; }
    public DateTimeOffset? LastVerificationMailAt { get; set; }
    public DateTimeOffset? LastResetMailAt { get; set; }
    public DateTimeOffset? LastEmailChangeMailAt { get; set; }
    public ApplicationUser() => Id = Guid.NewGuid();
}
