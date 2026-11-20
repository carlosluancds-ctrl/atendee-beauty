using Atendee.Domain.Catalog;
using Atendee.Domain.Customers;

namespace Atendee.Domain.Scheduling;

public enum AppointmentStatus
{
    PendingConfirmation = 1,
    Confirmed = 2,
    CheckedIn = 3,
    InService = 4,
    Completed = 5,
    Cancelled = 6,
    NoShow = 7,
    Rescheduled = 8
}

/// <summary>
/// Immutable booking facts are snapshotted at creation so later catalogue,
/// customer or professional changes cannot rewrite the historical agreement.
/// Times are UTC instants; occupancy includes the service's buffers.
/// </summary>
public sealed record Appointment(
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
    AppointmentStatus Status,
    string IdempotencyKey,
    string RequestHash,
    Guid CreatedByIdentityUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CancelledAtUtc)
{
    public static Appointment Create(
        Guid id,
        Customer customer,
        SalonService service,
        Guid professionalId,
        string professionalDisplayName,
        DateTimeOffset startAtUtc,
        string idempotencyKey,
        string requestHash,
        Guid createdByIdentityUserId,
        DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Appointment ID is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(service);
        if (customer.Id == Guid.Empty || customer.IsArchived)
            throw new ArgumentException("An active customer is required.", nameof(customer));
        if (!service.IsActive)
            throw new ArgumentException("An active service is required.", nameof(service));
        if (professionalId == Guid.Empty)
            throw new ArgumentException("Professional ID is required.", nameof(professionalId));
        if (createdByIdentityUserId == Guid.Empty)
            throw new ArgumentException("A trusted creator identity is required.", nameof(createdByIdentityUserId));

        ValidateUtc(nowUtc, nameof(nowUtc));
        ValidateUtc(startAtUtc, nameof(startAtUtc));
        if (startAtUtc.Second != 0 || startAtUtc.Millisecond != 0 ||
            startAtUtc.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("Appointment start must be aligned to a whole UTC minute.", nameof(startAtUtc));
        if (startAtUtc <= nowUtc)
            throw new ArgumentOutOfRangeException(nameof(startAtUtc), "Appointment start must be in the future.");

        var professionalName = NormalizeSnapshot(professionalDisplayName, nameof(professionalDisplayName));
        ValidateIdempotencyKey(idempotencyKey);
        ValidateRequestHash(requestHash);

        var endAtUtc = startAtUtc.AddMinutes(service.DurationMinutes);
        var occupancyStartAtUtc = startAtUtc.AddMinutes(-service.BufferBeforeMinutes);
        var occupancyEndAtUtc = endAtUtc.AddMinutes(service.BufferAfterMinutes);

        return new Appointment(
            id,
            customer.Id,
            professionalId,
            service.Id,
            customer.FullName,
            customer.NormalizedPhoneNumber,
            professionalName,
            service.Name,
            service.DurationMinutes,
            service.PriceMinorUnits,
            service.Currency,
            service.BufferBeforeMinutes,
            service.BufferAfterMinutes,
            startAtUtc,
            endAtUtc,
            occupancyStartAtUtc,
            occupancyEndAtUtc,
            AppointmentStatus.Confirmed,
            idempotencyKey,
            requestHash,
            createdByIdentityUserId,
            nowUtc,
            nowUtc,
            null);
    }

    public Appointment TransitionTo(AppointmentStatus nextStatus, DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (nowUtc < CreatedAtUtc || nowUtc < UpdatedAtUtc)
            throw new ArgumentException("Appointment update timestamp cannot precede its history.", nameof(nowUtc));
        if (!Enum.IsDefined(nextStatus))
            throw new ArgumentOutOfRangeException(nameof(nextStatus));
        if (!IsAllowedTransition(Status, nextStatus))
            throw new InvalidOperationException($"Appointment cannot transition from {Status} to {nextStatus}.");

        return this with
        {
            Status = nextStatus,
            UpdatedAtUtc = nowUtc,
            CancelledAtUtc = nextStatus == AppointmentStatus.Cancelled ? nowUtc : CancelledAtUtc
        };
    }

    /// <summary>
    /// Reschedules the appointment to a new time, preserving history via Rescheduled status.
    /// Returns a new Appointment with the new time, linked to this one via the original's ID.
    /// </summary>
    public Appointment Reschedule(
        DateTimeOffset newStartAtUtc,
        string newIdempotencyKey,
        string newRequestHash,
        Guid createdByIdentityUserId,
        DateTimeOffset nowUtc)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        ValidateUtc(newStartAtUtc, nameof(newStartAtUtc));
        if (newStartAtUtc <= nowUtc)
            throw new ArgumentOutOfRangeException(nameof(newStartAtUtc), "New appointment start must be in the future.");
        if (newStartAtUtc.Second != 0 || newStartAtUtc.Millisecond != 0 ||
            newStartAtUtc.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("Appointment start must be aligned to a whole UTC minute.", nameof(newStartAtUtc));
        if (Status != AppointmentStatus.Confirmed && Status != AppointmentStatus.PendingConfirmation)
            throw new InvalidOperationException($"Cannot reschedule appointment in status {Status}.");
        ValidateIdempotencyKey(newIdempotencyKey);
        ValidateRequestHash(newRequestHash);
        if (createdByIdentityUserId == Guid.Empty)
            throw new ArgumentException("A trusted creator identity is required.", nameof(createdByIdentityUserId));

        var newEndAtUtc = newStartAtUtc.AddMinutes(DurationMinutesSnapshot);
        var newOccupancyStartAtUtc = newStartAtUtc.AddMinutes(-BufferBeforeMinutesSnapshot);
        var newOccupancyEndAtUtc = newEndAtUtc.AddMinutes(BufferAfterMinutesSnapshot);

        var rescheduled = new Appointment(
            Guid.NewGuid(),
            CustomerId,
            ProfessionalId,
            ServiceId,
            CustomerNameSnapshot,
            CustomerPhoneSnapshot,
            ProfessionalNameSnapshot,
            ServiceNameSnapshot,
            DurationMinutesSnapshot,
            PriceMinorUnitsSnapshot,
            CurrencySnapshot,
            BufferBeforeMinutesSnapshot,
            BufferAfterMinutesSnapshot,
            newStartAtUtc,
            newEndAtUtc,
            newOccupancyStartAtUtc,
            newOccupancyEndAtUtc,
            AppointmentStatus.Confirmed,
            newIdempotencyKey,
            newRequestHash,
            createdByIdentityUserId,
            nowUtc,
            nowUtc,
            null);

        // Mark original as rescheduled
        return this with
        {
            Status = AppointmentStatus.Rescheduled,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Cancels the appointment (customer cancellation with 24h policy enforced at API layer).
    /// </summary>
    public Appointment Cancel(DateTimeOffset nowUtc, bool isCustomerCancellation = false)
    {
        ValidateUtc(nowUtc, nameof(nowUtc));
        if (Status != AppointmentStatus.Confirmed && Status != AppointmentStatus.PendingConfirmation)
            throw new InvalidOperationException($"Cannot cancel appointment in status {Status}.");
        if (isCustomerCancellation && StartAtUtc - nowUtc < TimeSpan.FromHours(24))
            throw new InvalidOperationException("Customer cancellation requires at least 24 hours notice.");

        return this with
        {
            Status = AppointmentStatus.Cancelled,
            UpdatedAtUtc = nowUtc,
            CancelledAtUtc = nowUtc
        };
    }

    public bool BlocksProfessionalTime => Status is AppointmentStatus.PendingConfirmation or AppointmentStatus.Confirmed or AppointmentStatus.CheckedIn or AppointmentStatus.InService;

    private static bool IsAllowedTransition(AppointmentStatus current, AppointmentStatus next)
        => current switch
        {
            AppointmentStatus.PendingConfirmation =>
                next is AppointmentStatus.Confirmed or AppointmentStatus.Cancelled,
            AppointmentStatus.Confirmed =>
                next is AppointmentStatus.CheckedIn or AppointmentStatus.Cancelled or AppointmentStatus.NoShow,
            AppointmentStatus.CheckedIn =>
                next == AppointmentStatus.InService,
            AppointmentStatus.InService =>
                next == AppointmentStatus.Completed,
            _ => false
        };

    private static string NormalizeSnapshot(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > 120 || normalized.Any(char.IsControl))
            throw new ArgumentException("Snapshot text is invalid or exceeds 120 characters.", parameterName);
        return normalized;
    }

    private static void ValidateIdempotencyKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length is < 16 or > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                                   character is not ('.' or '_' or ':' or '-')))
            throw new ArgumentException("Idempotency key must contain 16-128 safe ASCII characters.", nameof(value));
    }

    private static void ValidateRequestHash(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length != 64 || value.Any(character =>
                !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Request hash must be a lowercase SHA-256 hexadecimal digest.", nameof(value));
    }

    private static void ValidateUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}