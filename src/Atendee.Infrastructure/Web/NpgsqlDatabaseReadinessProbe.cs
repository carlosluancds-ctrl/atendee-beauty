using Microsoft.Extensions.Logging;
using Npgsql;

namespace Atendee.Infrastructure.Web;

public sealed class NpgsqlDatabaseReadinessProbe : IDatabaseReadinessProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly NpgsqlDataSource _identityDataSource;
    private readonly string _tenantConnectionString;
    private readonly ILogger<NpgsqlDatabaseReadinessProbe> _logger;

    public NpgsqlDatabaseReadinessProbe(
        NpgsqlDataSource identityDataSource,
        string tenantConnectionString,
        ILogger<NpgsqlDatabaseReadinessProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(identityDataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantConnectionString);
        ArgumentNullException.ThrowIfNull(logger);

        _identityDataSource = identityDataSource;
        _tenantConnectionString = tenantConnectionString;
        _logger = logger;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            await using (var identityCommand = _identityDataSource.CreateCommand("SELECT 1"))
            {
                await identityCommand.ExecuteScalarAsync(timeout.Token);
            }

            await using var tenantConnection = new NpgsqlConnection(_tenantConnectionString);
            await tenantConnection.OpenAsync(timeout.Token);
            await using var tenantCommand = new NpgsqlCommand("SELECT 1", tenantConnection);
            await tenantCommand.ExecuteScalarAsync(timeout.Token);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up (for example the client disconnected); do not report a dependency failure.
            throw;
        }
        catch (Exception exception)
        {
            // Log only the exception type: messages can contain host names or connection details.
            _logger.LogWarning(
                "Database readiness probe failed. ExceptionType={ExceptionType}",
                exception.GetType().Name);
            return false;
        }
    }
}
