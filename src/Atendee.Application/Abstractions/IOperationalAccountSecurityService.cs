using Atendee.Domain.Tenants;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Performs security-sensitive operational account actions. Callers must
/// enforce the appropriate administrative authorization and audit the action.
/// </summary>
public interface IOperationalAccountSecurityService
{
    /// <summary>
    /// Disables an operational identity and invalidates every existing cookie
    /// by rotating its security stamp. Returns false when the identity is absent.
    /// </summary>
    Task<bool> DisableAsync(
        Guid identityUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates every existing application session by rotating the security stamp.
    /// Returns false when the identity is absent.
    /// </summary>
    Task<bool> RevokeSessionsAsync(
        Guid identityUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a tenant member's role. Returns false when the identity is absent
    /// or the actor lacks authorization for the target role change.
    /// </summary>
    Task<bool> ChangeRoleAsync(
        Guid actorIdentityUserId,
        Guid targetIdentityUserId,
        TenantRole newRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates a tenant membership (soft disable within the tenant).
    /// Returns false when the membership is absent.
    /// </summary>
    Task<bool> DeactivateMembershipAsync(
        Guid actorIdentityUserId,
        Guid targetIdentityUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reactivates a previously deactivated tenant membership.
    /// Returns false when the membership is absent.
    /// </summary>
    Task<bool> ReactivateMembershipAsync(
        Guid actorIdentityUserId,
        Guid targetIdentityUserId,
        CancellationToken cancellationToken = default);
}
