using Atendee.Application.Models;
using Atendee.Domain.Scheduling;

namespace Atendee.Application.Abstractions;

public interface ITenantAppointmentStore
{
    Task<AppointmentCreateResult> CreateAsync(
        Guid customerId,
        Guid professionalId,
        Guid serviceId,
        DateTimeOffset startAtUtc,
        string timeZoneId,
        string idempotencyKey,
        string requestHash,
        Guid actorIdentityUserId,
        CancellationToken cancellationToken = default);

    Task<AppointmentModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AppointmentModel?> TransitionAsync(Guid id, AppointmentStatus nextStatus, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfessionalOccupancyInterval>> ListBlockingOccupancyAsync(
        Guid professionalId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    Task<AppointmentPageModel> ListAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Guid? professionalId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public enum AppointmentCreateOutcome
{
    Created,
    Replayed,
    IdempotencyConflict,
    CustomerNotFound,
    ServiceNotFound,
    ProfessionalNotFound,
    OutsideAvailability
}

public sealed record AppointmentCreateResult(
    AppointmentCreateOutcome Outcome,
    AppointmentModel? Appointment);