using Atendee.Application.Models;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Tenant-scoped sample repository. The caller cannot supply or change tenant_id.
/// </summary>
public interface ITenantNoteStore
{
    Task AddAsync(Guid id, string body, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantNote>> ListAsync(CancellationToken cancellationToken = default);
    Task<TenantNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
