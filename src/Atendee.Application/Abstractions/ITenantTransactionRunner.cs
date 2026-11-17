namespace Atendee.Application.Abstractions;

/// <summary>
/// Executes all tenant-scoped persistence work inside one transaction whose
/// PostgreSQL role and transaction-local tenant context are set by infrastructure.
/// The callback must not start/commit transactions or call SaveChanges directly.
/// </summary>
public interface ITenantTransactionRunner
{
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}
