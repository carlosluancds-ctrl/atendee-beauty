using Atendee.Application.Models;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Resolves the single active salon membership for an authenticated identity.
/// Implementations must query trusted server-side persistence only.
/// </summary>
public interface ITenantMembershipResolver
{
    Task<TenantMembershipSnapshot?> ResolveAsync(
        Guid identityUserId,
        CancellationToken cancellationToken = default);
}
