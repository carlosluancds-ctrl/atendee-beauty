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
}
