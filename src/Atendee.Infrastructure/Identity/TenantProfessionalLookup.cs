namespace Atendee.Infrastructure.Identity;

public sealed class TenantProfessionalLookup
{
    public Guid Id { get; set; }
    public Guid IdentityUserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class TenantProfessionalMembershipLookup
{
    public int Role { get; set; }
    public bool IsActive { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
