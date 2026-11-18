using Atendee.Application.Models;

namespace Atendee.Application.Abstractions;

public interface ITenantProfessionalStore
{
    Task<IReadOnlyList<ProfessionalProfileModel>> ListAsync(CancellationToken cancellationToken = default);
    Task<ProfessionalProfileModel?> GetOwnAsync(Guid identityUserId, CancellationToken cancellationToken = default);
    Task<ProfessionalProfileModel> CreateAsync(Guid identityUserId, CancellationToken cancellationToken = default);
    Task<ProfessionalProfileModel?> SetServicesAsync(
        Guid professionalId,
        IReadOnlyCollection<Guid> serviceIds,
        CancellationToken cancellationToken = default);
}
