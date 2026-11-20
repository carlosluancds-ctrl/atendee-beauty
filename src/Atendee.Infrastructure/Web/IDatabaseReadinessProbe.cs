namespace Atendee.Infrastructure.Web;

/// <summary>
/// Reports whether the API can reach the PostgreSQL databases it depends on.
/// Implementations must never throw for dependency failures and must never
/// expose connection strings or exception details to callers.
/// </summary>
public interface IDatabaseReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
