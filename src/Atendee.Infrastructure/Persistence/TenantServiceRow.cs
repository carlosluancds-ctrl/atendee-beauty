using Atendee.Domain.Catalog;

namespace Atendee.Infrastructure.Persistence;

internal sealed class TenantServiceRow
{
    private TenantServiceRow() { } // EF Core

    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int DurationMinutes { get; private set; }
    public long PriceMinorUnits { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public int BufferBeforeMinutes { get; private set; }
    public int BufferAfterMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static TenantServiceRow FromDomain(Guid tenantId, SalonService service)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));

        return new TenantServiceRow
        {
            TenantId = tenantId,
            Id = service.Id,
            Name = service.Name,
            DurationMinutes = service.DurationMinutes,
            PriceMinorUnits = service.PriceMinorUnits,
            Currency = service.Currency,
            BufferBeforeMinutes = service.BufferBeforeMinutes,
            BufferAfterMinutes = service.BufferAfterMinutes,
            IsActive = service.IsActive,
            CreatedAtUtc = service.CreatedAtUtc,
            UpdatedAtUtc = service.UpdatedAtUtc
        };
    }

    public void Apply(SalonService service)
    {
        if (service.Id != Id)
            throw new InvalidOperationException("Cannot change the identity of a persisted service.");

        Name = service.Name;
        DurationMinutes = service.DurationMinutes;
        PriceMinorUnits = service.PriceMinorUnits;
        Currency = service.Currency;
        BufferBeforeMinutes = service.BufferBeforeMinutes;
        BufferAfterMinutes = service.BufferAfterMinutes;
        IsActive = service.IsActive;
        UpdatedAtUtc = service.UpdatedAtUtc;
    }

    public SalonService ToDomain() => new(
        Id, Name, DurationMinutes, PriceMinorUnits, Currency,
        BufferBeforeMinutes, BufferAfterMinutes, IsActive,
        CreatedAtUtc, UpdatedAtUtc);
}
