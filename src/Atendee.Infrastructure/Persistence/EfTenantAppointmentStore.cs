using System.Security.Cryptography;
using System.Text;
using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Catalog;
using Atendee.Domain.Professionals;
using Atendee.Domain.Primitives;
using Atendee.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atendee.Infrastructure.Persistence;

internal sealed class EfTenantAppointmentStore : ITenantAppointmentStore
{
    private readonly TenantDbContext _db;
    private readonly Guid _tenantId;

    public EfTenantAppointmentStore(TenantDbContext db, ICurrentTenantContext tenantContext)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _tenantId = tenantContext.RequireTenantId().Value;
        if (_tenantId == Guid.Empty || _tenantId != db.BoundTenantId)
            throw new InvalidOperationException("Appointment store tenant does not match the DbContext tenant.");
    }

    public async Task<AppointmentCreateResult> CreateAsync(
        Guid customerId,
        Guid professionalId,
        Guid serviceId,
        DateTimeOffset startAtUtc,
        string timeZoneId,
        string idempotencyKey,
        string requestHash,
        Guid actorIdentityUserId,
        CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty || professionalId == Guid.Empty || serviceId == Guid.Empty)
            throw new ArgumentException("Customer, professional and service IDs are required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        ValidateIdempotencyKey(idempotencyKey);
        ValidateRequestHash(requestHash);
        if (startAtUtc.Offset != TimeSpan.Zero || startAtUtc.Second != 0 ||
            startAtUtc.Millisecond != 0 || startAtUtc.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("Appointment start must be a UTC whole-minute instant.", nameof(startAtUtc));
        if (actorIdentityUserId == Guid.Empty)
            throw new ArgumentException("Trusted actor is required.", nameof(actorIdentityUserId));

        // Lock the idempotency key before checking it. Same-key retries serialize,
        // while distinct keys proceed to the professional-level overlap lock.
        var idempotencyLock = $"{_tenantId:N}:appointment-idempotency:{idempotencyKey}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({idempotencyLock}, 0))",
            cancellationToken);

        var previous = await _db.TenantAppointments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKey == idempotencyKey, cancellationToken);
        if (previous is not null)
        {
            return string.Equals(previous.RequestHash, requestHash, StringComparison.Ordinal)
                ? new AppointmentCreateResult(AppointmentCreateOutcome.Replayed, ToModel(previous))
                : new AppointmentCreateResult(AppointmentCreateOutcome.IdempotencyConflict, null);
        }

        var professionalLock = $"{_tenantId:D}:{professionalId:D}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({professionalLock}, 0))",
            cancellationToken);

        var customerRow = await _db.TenantCustomers
            .SingleOrDefaultAsync(row => row.Id == customerId && !row.IsArchived, cancellationToken);
        if (customerRow is null)
            return new AppointmentCreateResult(AppointmentCreateOutcome.CustomerNotFound, null);

        var serviceRow = await _db.TenantServices
            .SingleOrDefaultAsync(row => row.Id == serviceId && row.IsActive, cancellationToken);
        if (serviceRow is null)
            return new AppointmentCreateResult(AppointmentCreateOutcome.ServiceNotFound, null);

        var professionalName = await _db.Database.SqlQuery<string>($"""
            SELECT membership.display_name AS "Value"
            FROM atendee.professionals AS professional
            INNER JOIN atendee.tenant_users AS membership
                ON membership.tenant_id = professional.tenant_id
               AND membership.identity_user_id = professional.identity_user_id
            WHERE professional.tenant_id = {_tenantId}
              AND professional.professional_id = {professionalId}
              AND professional.is_active = true
              AND membership.is_active = true
              AND membership.role = 4
            """).SingleOrDefaultAsync(cancellationToken);
        if (professionalName is null)
            return new AppointmentCreateResult(AppointmentCreateOutcome.ProfessionalNotFound, null);

        var assigned = await _db.TenantProfessionalServices.AnyAsync(
            row => row.ProfessionalId == professionalId && row.ServiceId == serviceId,
            cancellationToken);
        if (!assigned)
            return new AppointmentCreateResult(AppointmentCreateOutcome.ProfessionalNotFound, null);

        var service = serviceRow.ToDomain();
        var customer = customerRow.ToDomain();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var localStart = TimeZoneInfo.ConvertTime(startAtUtc, zone);
        var localDate = DateOnly.FromDateTime(localStart.DateTime);

        var businessHours = await _db.TenantBusinessHours.AsNoTracking()
            .Where(row => row.IsActive)
            .Select(row => new BusinessHoursInterval(row.IsoDayOfWeek, row.StartTime, row.EndTime))
            .ToArrayAsync(cancellationToken);
        var recurringAvailability = await _db.TenantProfessionalAvailability.AsNoTracking()
            .Where(row => row.ProfessionalId == professionalId && row.IsActive)
            .Select(row => new ProfessionalAvailabilityInterval(row.IsoDayOfWeek, row.StartTime, row.EndTime))
            .ToArrayAsync(cancellationToken);
        var timeOff = await _db.TenantProfessionalTimeOff.AsNoTracking()
            .Where(row => row.ProfessionalId == professionalId && row.IsActive && row.Date == localDate)
            .Select(row => new ProfessionalTimeOffInterval(row.Date, row.StartTime, row.EndTime))
            .ToArrayAsync(cancellationToken);

        var windows = ServiceAvailabilityCalculator.Calculate(
            localDate, localDate, timeZoneId, businessHours, recurringAvailability, timeOff, service);
        if (!windows.Any(window => startAtUtc >= window.EarliestStartUtc &&
                                   startAtUtc <= window.LatestStartUtc))
            return new AppointmentCreateResult(AppointmentCreateOutcome.OutsideAvailability, null);

        var nowUtc = DateTimeOffset.UtcNow;
        var appointment = Appointment.Create(
            Guid.NewGuid(), customer, service, professionalId, professionalName,
            startAtUtc, idempotencyKey, requestHash, actorIdentityUserId, nowUtc);
        var row = TenantAppointmentRow.FromDomain(_tenantId, appointment, timeZoneId);
        _db.TenantAppointments.Add(row);

        // The database trigger writes immutable event history, audit and outbox
        // rows in the same transaction as this appointment insert.
        return new AppointmentCreateResult(AppointmentCreateOutcome.Created, ToModel(appointment));
    }

    public async Task<AppointmentModel?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.TenantAppointments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return row is null ? null : ToModel(row);
    }
    public async Task<AppointmentModel?> TransitionAsync(
        Guid id,
        AppointmentStatus nextStatus,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty || nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Appointment ID and UTC transition time are required.");

        var row = await _db.TenantAppointments
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is null)
            return null;

        var transitioned = row.ToDomain().TransitionTo(nextStatus, nowUtc);
        row.ApplyTransition(transitioned);
        return ToModel(transitioned);
    }

    public async Task<AppointmentModel?> RescheduleAsync(
        Guid originalAppointmentId,
        DateTimeOffset newStartAtUtc,
        string newIdempotencyKey,
        string newRequestHash,
        Guid actorIdentityUserId,
        CancellationToken cancellationToken = default)
    {
        if (originalAppointmentId == Guid.Empty || newStartAtUtc.Offset != TimeSpan.Zero ||
            newStartAtUtc.Second != 0 || newStartAtUtc.Millisecond != 0 ||
            newStartAtUtc.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new ArgumentException("Appointment ID and valid UTC start time (whole minute) are required.");
        if (actorIdentityUserId == Guid.Empty)
            throw new ArgumentException("Trusted actor is required.");
        ValidateIdempotencyKey(newIdempotencyKey);
        ValidateRequestHash(newRequestHash);

        var originalRow = await _db.TenantAppointments
            .SingleOrDefaultAsync(item => item.Id == originalAppointmentId, cancellationToken);
        if (originalRow is null)
            return null;

        var original = originalRow.ToDomain();
        if (original.Status != AppointmentStatus.Confirmed && original.Status != AppointmentStatus.PendingConfirmation)
            return null; // Can only reschedule confirmed/pending

        // Lock idempotency key
        var idempotencyLock = $"{_tenantId:N}:appointment-idempotency:{newIdempotencyKey}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({idempotencyLock}, 0))",
            cancellationToken);

        var previous = await _db.TenantAppointments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.IdempotencyKey == newIdempotencyKey, cancellationToken);
        if (previous is not null)
        {
            if (string.Equals(previous.RequestHash, newRequestHash, StringComparison.Ordinal))
                return ToModel(previous);
            throw new InvalidOperationException("Idempotency key reused with different request.");
        }

        // Lock professional for overlap check
        var professionalLock = $"{_tenantId:D}:{original.ProfessionalId:D}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({professionalLock}, 0))",
            cancellationToken);

        // Check availability at new time
        var serviceRow = await _db.TenantServices
            .SingleOrDefaultAsync(row => row.Id == original.ServiceId && row.IsActive, cancellationToken);
        if (serviceRow is null)
            return null;

        var service = serviceRow.ToDomain();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(originalRow.TenantTimeZoneId);
        var localStart = TimeZoneInfo.ConvertTime(newStartAtUtc, zone);
        var localDate = DateOnly.FromDateTime(localStart.DateTime);

        var businessHours = await _db.TenantBusinessHours.AsNoTracking()
            .Where(row => row.IsActive)
            .Select(row => new BusinessHoursInterval(row.IsoDayOfWeek, row.StartTime, row.EndTime))
            .ToArrayAsync(cancellationToken);
        var recurringAvailability = await _db.TenantProfessionalAvailability.AsNoTracking()
            .Where(row => row.ProfessionalId == original.ProfessionalId && row.IsActive)
            .Select(row => new ProfessionalAvailabilityInterval(row.IsoDayOfWeek, row.StartTime, row.EndTime))
            .ToArrayAsync(cancellationToken);
        var timeOff = await _db.TenantProfessionalTimeOff.AsNoTracking()
            .Where(row => row.ProfessionalId == original.ProfessionalId && row.IsActive && row.Date == localDate)
            .Select(row => new ProfessionalTimeOffInterval(row.Date, row.StartTime, row.EndTime))
            .ToArrayAsync(cancellationToken);

        var windows = ServiceAvailabilityCalculator.Calculate(
            localDate, localDate, originalRow.TenantTimeZoneId, businessHours, recurringAvailability, timeOff, service);
        if (!windows.Any(window => newStartAtUtc >= window.EarliestStartUtc &&
                                   newStartAtUtc <= window.LatestStartUtc))
            return null; // Outside availability

        // Check overlap with existing appointments
        var occupancy = await ListBlockingOccupancyAsync(original.ProfessionalId, newStartAtUtc, newStartAtUtc.AddMinutes(service.DurationMinutes), cancellationToken);
        if (occupancy.Count > 0)
            throw new InvalidOperationException("professional_appointment_overlap");

        var nowUtc = DateTimeOffset.UtcNow;
        var rescheduled = original.Reschedule(newStartAtUtc, newIdempotencyKey, newRequestHash, actorIdentityUserId, nowUtc);

        // Update original row to Rescheduled status
        originalRow.ApplyTransition(rescheduled);
        originalRow.UpdateTimestamp(nowUtc);

        // Add new appointment row with tenant time zone
        var newRow = TenantAppointmentRow.FromDomain(_tenantId, rescheduled, originalRow.TenantTimeZoneId);
        _db.TenantAppointments.Add(newRow);

        return ToModel(rescheduled);
    }

    public async Task<AppointmentModel?> CancelAsync(
        Guid appointmentId,
        bool isCustomerCancellation,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (appointmentId == Guid.Empty || nowUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Appointment ID and UTC time are required.");

        var row = await _db.TenantAppointments
            .SingleOrDefaultAsync(item => item.Id == appointmentId, cancellationToken);
        if (row is null)
            return null;

        var appointment = row.ToDomain();
        if (appointment.Status != AppointmentStatus.Confirmed && appointment.Status != AppointmentStatus.PendingConfirmation)
            return null;

        if (isCustomerCancellation && appointment.StartAtUtc - nowUtc < TimeSpan.FromHours(24))
            throw new InvalidOperationException("Customer cancellation requires at least 24 hours notice.");

        var cancelled = appointment.Cancel(nowUtc, isCustomerCancellation);
        row.ApplyTransition(cancelled);
        row.UpdateTimestamp(nowUtc);

        return ToModel(cancelled);
    }

public async Task<IReadOnlyList<ProfessionalOccupancyInterval>> ListBlockingOccupancyAsync(
        Guid professionalId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (professionalId == Guid.Empty || fromUtc.Offset != TimeSpan.Zero ||
            toUtc.Offset != TimeSpan.Zero || fromUtc >= toUtc)
            throw new ArgumentException("Occupancy query range is invalid.");

        return await _db.TenantAppointments.AsNoTracking()
            .Where(row => row.ProfessionalId == professionalId &&
                          row.Status != AppointmentStatus.Cancelled &&
                          row.Status != AppointmentStatus.Completed &&
                          row.Status != AppointmentStatus.NoShow &&
                          row.OccupancyStartAtUtc < toUtc &&
                          row.OccupancyEndAtUtc > fromUtc)
            .OrderBy(row => row.OccupancyStartAtUtc)
            .Select(row => new ProfessionalOccupancyInterval(row.OccupancyStartAtUtc, row.OccupancyEndAtUtc))
            .ToListAsync(cancellationToken);
    }
    public async Task<AppointmentPageModel> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? professionalId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero ||
            fromUtc >= toUtc || toUtc - fromUtc > TimeSpan.FromDays(31) ||
            professionalId == Guid.Empty || page is < 1 or > 100_000 ||
            pageSize is < 1 or > 100)
            throw new ArgumentException("Appointment list range, pagination or professional filter is invalid.");

        var query = _db.TenantAppointments.AsNoTracking()
            .Where(row => row.StartAtUtc >= fromUtc && row.StartAtUtc < toUtc);
        if (professionalId.HasValue)
            query = query.Where(row => row.ProfessionalId == professionalId.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(row => row.StartAtUtc).ThenBy(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new AppointmentPageModel(items.Select(ToModel).ToArray(), page, pageSize, totalCount);
    }

    private static AppointmentModel ToModel(TenantAppointmentRow row)
        => new(
            row.Id, row.CustomerId, row.ProfessionalId, row.ServiceId,
            row.CustomerNameSnapshot, row.CustomerPhoneSnapshot,
            row.ProfessionalNameSnapshot, row.ServiceNameSnapshot,
            row.DurationMinutesSnapshot, row.PriceMinorUnitsSnapshot, row.CurrencySnapshot,
            row.BufferBeforeMinutesSnapshot, row.BufferAfterMinutesSnapshot,
            row.StartAtUtc, row.EndAtUtc, row.OccupancyStartAtUtc, row.OccupancyEndAtUtc,
            row.Status.ToString(), row.CreatedAtUtc, row.UpdatedAtUtc);

    private static AppointmentModel ToModel(Appointment appointment)
        => new(
            appointment.Id, appointment.CustomerId, appointment.ProfessionalId, appointment.ServiceId,
            appointment.CustomerNameSnapshot, appointment.CustomerPhoneSnapshot,
            appointment.ProfessionalNameSnapshot, appointment.ServiceNameSnapshot,
            appointment.DurationMinutesSnapshot, appointment.PriceMinorUnitsSnapshot, appointment.CurrencySnapshot,
            appointment.BufferBeforeMinutesSnapshot, appointment.BufferAfterMinutesSnapshot,
            appointment.StartAtUtc, appointment.EndAtUtc, appointment.OccupancyStartAtUtc,
            appointment.OccupancyEndAtUtc, appointment.Status.ToString(),
            appointment.CreatedAtUtc, appointment.UpdatedAtUtc);

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
            throw new ArgumentException("Request hash must be a lowercase SHA-256 digest.", nameof(value));
    }
}