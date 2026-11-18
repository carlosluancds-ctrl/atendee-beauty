using Atendee.Application.Models;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Tenant-scoped salon service persistence. Tenant identity is always taken
/// from the trusted request context and cannot be supplied by the caller.
/// </summary>
public interface ITenantServiceStore
{
    Task<IReadOnlyList<SalonServiceModel>> ListAsync(CancellationToken cancellationToken = default);
    Task<SalonServiceModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SalonServiceModel> CreateAsync(
        string name,
        int durationMinutes,
        long priceMinorUnits,
        string currency,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        bool isActive,
        CancellationToken cancellationToken = default);
    Task<SalonServiceModel?> UpdateAsync(
        Guid id,
        string name,
        int durationMinutes,
        long priceMinorUnits,
        string currency,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        bool isActive,
        CancellationToken cancellationToken = default);
}
