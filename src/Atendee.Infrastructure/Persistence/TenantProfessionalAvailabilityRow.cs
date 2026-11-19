using Atendee.Application.Models;
using Atendee.Domain.Professionals;

namespace Atendee.Infrastructure.Persistence;

internal sealed class TenantProfessionalAvailabilityRow
{
    private TenantProfessionalAvailabilityRow() { } // EF Core

    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public int IsoDayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TenantProfessionalAvailabilityRow FromDomain(
        Guid tenantId,
        Guid professionalId,
        ProfessionalAvailabilityInterval interval,
        DateTimeOffset nowUtc)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        if (professionalId == Guid.Empty)
            throw new ArgumentException("Professional ID is required.", nameof(professionalId));
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));

        return new TenantProfessionalAvailabilityRow
        {
            TenantId = tenantId,
            Id = Guid.NewGuid(),
            ProfessionalId = professionalId,
            IsoDayOfWeek = interval.IsoDayOfWeek,
            StartTime = interval.StartTime,
            EndTime = interval.EndTime,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));
        if (!IsActive)
            return;

        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    public ProfessionalAvailabilityIntervalModel ToModel() => new(
        Id, ProfessionalId, IsoDayOfWeek, StartTime, EndTime);
}
