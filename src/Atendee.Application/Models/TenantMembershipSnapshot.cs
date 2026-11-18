using Atendee.Domain.Primitives;
using Atendee.Domain.Tenants;

namespace Atendee.Application.Models;

/// <summary>
/// Server-resolved membership and minimal tenant profile for one operational
/// identity. This value must never be constructed from tenant or role values
/// supplied by an HTTP client.
/// </summary>
public sealed record TenantMembershipSnapshot(
    Guid IdentityUserId,
    TenantId TenantId,
    string DisplayName,
    TenantRole Role,
    bool IsActive,
    TenantStatus TenantStatus,
    string TenantName,
    string TenantSlug,
    string TenantTimeZoneId);
