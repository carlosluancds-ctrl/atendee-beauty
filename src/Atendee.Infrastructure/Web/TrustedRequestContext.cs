using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Primitives;
using Atendee.Domain.Tenants;

namespace Atendee.Infrastructure.Web;

/// <summary>
/// Per-request tenant and actor context populated only by
/// TenantContextMiddleware after resolving the authenticated user in PostgreSQL.
/// There is intentionally no setter bound to HTTP input.
/// </summary>
public sealed class TrustedRequestContext : ICurrentTenantContext, ICurrentActorContext
{
    private TenantMembershipSnapshot? _membership;

    public Guid? ActorId => _membership?.IdentityUserId;

    // Platform administrators have a separate identity boundary and are never
    // inferred from an operational identity or role claim.
    public bool IsPlatformAdministrator => false;

    public TenantRole? Role => _membership?.Role;

    public TenantStatus? TenantStatus => _membership?.TenantStatus;

    public string? DisplayName => _membership?.DisplayName;

    public TenantId RequireTenantId()
        => _membership?.TenantId
            ?? throw new InvalidOperationException(
                "A trusted authenticated tenant context is not available for this request.");

    public void Establish(TenantMembershipSnapshot membership)
    {
        ArgumentNullException.ThrowIfNull(membership);

        if (_membership is not null)
            throw new InvalidOperationException("The tenant context has already been established.");

        if (!membership.IsActive)
            throw new InvalidOperationException("An inactive tenant membership cannot establish request context.");

        _membership = membership;
    }
}
