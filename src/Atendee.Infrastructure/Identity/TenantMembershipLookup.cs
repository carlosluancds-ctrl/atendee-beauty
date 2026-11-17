namespace Atendee.Infrastructure.Identity;

/// <summary>
/// Projection returned by the narrowly scoped PostgreSQL membership resolver.
/// This is not an EF entity and is never exposed directly to an HTTP client.
/// </summary>
public sealed class TenantMembershipLookup
{
    public Guid IdentityUserId { get; set; }
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Role { get; set; }
    public bool IsActive { get; set; }
    public int TenantStatus { get; set; }
}
