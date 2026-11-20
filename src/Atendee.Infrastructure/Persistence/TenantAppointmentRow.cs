using Atendee.Domain.Scheduling;

namespace Atendee.Infrastructure.Persistence;

internal sealed class TenantAppointmentRow
{
    private TenantAppointmentRow() { }

    public Guid TenantId { get; private set; }
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string CustomerNameSnapshot { get; private set; } = string.Empty;
    public string? CustomerPhoneSnapshot { get; private set; }
    public string ProfessionalNameSnapshot { get; private set; } = string.Empty;
    public string ServiceNameSnapshot { get; private set; } = string.Empty;
    public int DurationMinutesSnapshot { get; private set; }
    public long PriceMinorUnitsSnapshot { get; private set; }
    public string CurrencySnapshot { get; private set; } = string.Empty;
    public int BufferBeforeMinutesSnapshot { get; private set; }
    public int BufferAfterMinutesSnapshot { get; private set; }
    public DateTimeOffset StartAtUtc { get; private set; }
    public DateTimeOffset EndAtUtc { get; private set; }
    public DateTimeOffset OccupancyStartAtUtc { get; private set; }
    public DateTimeOffset OccupancyEndAtUtc { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public Guid CreatedByIdentityUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public static TenantAppointmentRow FromDomain(Guid tenantId, Appointment appointment)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant ID is required.", nameof(tenantId));

        return new TenantAppointmentRow
        {
            TenantId = tenantId,
            Id = appointment.Id,
            CustomerId = appointment.CustomerId,
            ProfessionalId = appointment.ProfessionalId,
            ServiceId = appointment.ServiceId,
            CustomerNameSnapshot = appointment.CustomerNameSnapshot,
            CustomerPhoneSnapshot = appointment.CustomerPhoneSnapshot,
            ProfessionalNameSnapshot = appointment.ProfessionalNameSnapshot,
            ServiceNameSnapshot = appointment.ServiceNameSnapshot,
            DurationMinutesSnapshot = appointment.DurationMinutesSnapshot,
            PriceMinorUnitsSnapshot = appointment.PriceMinorUnitsSnapshot,
            CurrencySnapshot = appointment.CurrencySnapshot,
            BufferBeforeMinutesSnapshot = appointment.BufferBeforeMinutesSnapshot,
            BufferAfterMinutesSnapshot = appointment.BufferAfterMinutesSnapshot,
            StartAtUtc = appointment.StartAtUtc,
            EndAtUtc = appointment.EndAtUtc,
            OccupancyStartAtUtc = appointment.OccupancyStartAtUtc,
            OccupancyEndAtUtc = appointment.OccupancyEndAtUtc,
            Status = appointment.Status,
            IdempotencyKey = appointment.IdempotencyKey,
            RequestHash = appointment.RequestHash,
            CreatedByIdentityUserId = appointment.CreatedByIdentityUserId,
            CreatedAtUtc = appointment.CreatedAtUtc,
            UpdatedAtUtc = appointment.UpdatedAtUtc,
            CancelledAtUtc = appointment.CancelledAtUtc
        };
    }
    public void ApplyTransition(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (appointment.Id != Id ||
            appointment.CustomerId != CustomerId ||
            appointment.ProfessionalId != ProfessionalId ||
            appointment.ServiceId != ServiceId ||
            appointment.IdempotencyKey != IdempotencyKey ||
            appointment.RequestHash != RequestHash)
            throw new InvalidOperationException("Appointment identity and idempotency facts are immutable.");

        Status = appointment.Status;
        UpdatedAtUtc = appointment.UpdatedAtUtc;
        CancelledAtUtc = appointment.CancelledAtUtc;
    }

public Appointment ToDomain() => new(
        Id, CustomerId, ProfessionalId, ServiceId,
        CustomerNameSnapshot, CustomerPhoneSnapshot, ProfessionalNameSnapshot, ServiceNameSnapshot,
        DurationMinutesSnapshot, PriceMinorUnitsSnapshot, CurrencySnapshot,
        BufferBeforeMinutesSnapshot, BufferAfterMinutesSnapshot,
        StartAtUtc, EndAtUtc, OccupancyStartAtUtc, OccupancyEndAtUtc,
        Status, IdempotencyKey, RequestHash, CreatedByIdentityUserId,
        CreatedAtUtc, UpdatedAtUtc, CancelledAtUtc);
}