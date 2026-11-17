using Microsoft.AspNetCore.Identity;

namespace Atendee.Infrastructure.Identity;

/// <summary>
/// Global login identity for salon operators only. Platform administrators
/// deliberately use a separate identity model and authentication boundary.
/// </summary>
public sealed class OperationalIdentityUser : IdentityUser<Guid>
{
    public OperationalIdentityUser()
    {
        Id = Guid.NewGuid();
    }
}
