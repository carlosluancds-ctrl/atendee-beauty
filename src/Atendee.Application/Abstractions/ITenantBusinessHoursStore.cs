using Atendee.Application.Models;
using Atendee.Domain.Scheduling;

namespace Atendee.Application.Abstractions;

/// <summary>
/// Reads and replaces a tenant's weekly opening intervals inside the caller's
/// tenant-scoped transaction.
/// </summary>
public interface ITenantBusinessHoursStore
{
    Task<IReadOnlyList<BusinessHoursIntervalModel>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessHoursIntervalModel>> ReplaceAsync(
        BusinessHoursSchedule schedule,
        CancellationToken cancellationToken = default);
}
