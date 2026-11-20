using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Atendee.Infrastructure.Web;

/// <summary>
/// Periodically removes expired IP-digest buckets. The request path also performs
/// bounded opportunistic cleanup so retention does not depend on this worker's tick.
/// </summary>
public sealed class DistributedAuthenticationRateLimitCleanupService(
    NpgsqlDataSource dataSource,
    ILogger<DistributedAuthenticationRateLimitCleanupService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
                await using var command = new NpgsqlCommand(
                    """
                    DELETE FROM identity."AuthenticationRateLimitBuckets"
                     WHERE ctid IN
                     (
                         SELECT ctid
                           FROM identity."AuthenticationRateLimitBuckets"
                          WHERE "ExpiresAtUtc" <= @now
                          ORDER BY "ExpiresAtUtc"
                          LIMIT 256
                     )
                    """,
                    connection)
                {
                    CommandTimeout = 3
                };
                command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
                var deleted = await command.ExecuteNonQueryAsync(stoppingToken);
                if (deleted > 0)
                    logger.LogDebug(
                        "Removed {DeletedCount} expired distributed authentication rate-limit buckets.",
                        deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Expired distributed authentication rate-limit buckets could not be cleaned; the worker will retry.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}