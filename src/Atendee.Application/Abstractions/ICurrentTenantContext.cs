using Atendee.Domain.Primitives;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Exposes only the tenant context resolved by trusted server-side identity.
/// Implementations must never trust a tenant ID supplied by the browser.
/// </summary>
public interface ICurrentTenantContext
{
    TenantId RequireTenantId();
}
