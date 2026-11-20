namespace Atendee.Application.Models;

public sealed record AppointmentModel(
    Guid Id,
    Guid CustomerId,
    Guid ProfessionalId,
    Guid ServiceId,
    string CustomerNameSnapshot,
    string? CustomerPhoneSnapshot,
    string ProfessionalNameSnapshot,
    string ServiceNameSnapshot,
    int DurationMinutesSnapshot,
    long PriceMinorUnitsSnapshot,
    string CurrencySnapshot,
    int BufferBeforeMinutesSnapshot,
    int BufferAfterMinutesSnapshot,
    DateTimeOffset StartAtUtc,
    DateTimeOffset EndAtUtc,
    DateTimeOffset OccupancyStartAtUtc,
    DateTimeOffset OccupancyEndAtUtc,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);