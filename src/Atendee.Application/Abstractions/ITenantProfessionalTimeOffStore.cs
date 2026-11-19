using Atendee.Application.Models;
using Atendee.Domain.Professionals;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Tenant- and professional-scoped date-specific time-off persistence.
/// </summary>
public interface ITenantProfessionalTimeOffStore
{
    Task<IReadOnlyList<ProfessionalTimeOffModel>> ListAsync(
        Guid professionalId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<ProfessionalTimeOffModel?> CreateAsync(
        Guid professionalId,
        ProfessionalTimeOffInterval interval,
        CancellationToken cancellationToken = default);

    Task<ProfessionalTimeOffModel?> ArchiveAsync(
        Guid professionalId,
        Guid timeOffId,
        CancellationToken cancellationToken = default);
}
