using Atendee.Application.Models;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Tenant-scoped customer persistence. Tenant identity is resolved from the
/// trusted request context and is never accepted from API input.
/// </summary>
public interface ITenantCustomerStore
{
    Task<CustomerPageModel> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CustomerModel?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CustomerModel> CreateAsync(
        string fullName,
        string? phoneNumber,
        string? emailAddress,
        CancellationToken cancellationToken = default);

    Task<CustomerModel?> UpdateAsync(
        Guid id,
        string fullName,
        string? phoneNumber,
        string? emailAddress,
        CancellationToken cancellationToken = default);

    Task<CustomerModel?> ArchiveAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
