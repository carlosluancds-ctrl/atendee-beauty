using Atendee.Application.Models;
using Atendee.Domain.Professionals;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Tenant- and professional-scoped recurring availability persistence.
/// </summary>
public interface ITenantProfessionalAvailabilityStore
{
    Task<IReadOnlyList<ProfessionalAvailabilityIntervalModel>> ListAsync(
        Guid professionalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfessionalAvailabilityIntervalModel>> ReplaceAsync(
        Guid professionalId,
        ProfessionalAvailabilitySchedule schedule,
        CancellationToken cancellationToken = default);
}
