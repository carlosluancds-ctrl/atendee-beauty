namespace Atendee.Infrastructure.Persistence;

internal sealed class TenantProfessionalServiceRow
{
    private TenantProfessionalServiceRow() { }

    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ServiceId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static TenantProfessionalServiceRow Create(
        Guid tenantId, Guid professionalId, Guid serviceId, DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty || professionalId == Guid.Empty || serviceId == Guid.Empty)
            throw new ArgumentException("Tenant, professional and service IDs are required.");
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));
        return new TenantProfessionalServiceRow
        {
            TenantId = tenantId,
            ProfessionalId = professionalId,
            ServiceId = serviceId,
            CreatedAtUtc = nowUtc
        };
    }
}
