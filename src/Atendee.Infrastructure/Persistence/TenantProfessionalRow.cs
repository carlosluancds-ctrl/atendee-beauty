using Atendee.Domain.Professionals;

namespace Atendee.Infrastructure.Persistence;

internal sealed class TenantProfessionalRow
{
    private TenantProfessionalRow() { }

    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public Guid IdentityUserId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TenantProfessionalRow FromDomain(Guid tenantId, ProfessionalProfile profile)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        return new TenantProfessionalRow
        {
            TenantId = tenantId,
            Id = profile.Id,
            IdentityUserId = profile.IdentityUserId,
            IsActive = profile.IsActive,
            CreatedAtUtc = profile.CreatedAtUtc,
            UpdatedAtUtc = profile.UpdatedAtUtc
        };
    }
}
