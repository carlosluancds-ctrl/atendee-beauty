using System.Data;
using System.Data.Common;
using Atendee.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atendee.Infrastructure.Persistence;

/// <summary>
/// Mandatory transaction boundary for tenant-scoped database work.
/// It resets session state on checkout, assumes the restricted runtime role,
/// and sets tenant and authenticated actor using bound parameters and transaction-local GUC state.
/// </summary>
public sealed class NpgsqlTenantTransactionRunner : ITenantTransactionRunner
{
    private const string RuntimeRole = "atendee_runtime"; // compile-time constant; never caller input
    private readonly TenantDbContext _db;
    private readonly ICurrentTenantContext _currentTenant;
    private readonly ICurrentActorContext _currentActor;

    public NpgsqlTenantTransactionRunner(
        TenantDbContext db,
        ICurrentTenantContext currentTenant,
        ICurrentActorContext currentActor)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _currentTenant = currentTenant ?? throw new ArgumentNullException(nameof(currentTenant));
        _currentActor = currentActor ?? throw new ArgumentNullException(nameof(currentActor));
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = _currentTenant.RequireTenantId();
        if (!tenantId.IsValid || tenantId.Value != _db.BoundTenantId)
            throw new InvalidOperationException("Tenant context changed or does not match this DbContext scope.");

        var actorId = _currentActor.ActorId;
        if (actorId is null || actorId == Guid.Empty)
            throw new InvalidOperationException("A trusted authenticated actor is required for tenant transactions.");

        if (_db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Nested tenant transactions are not supported.");

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Closed)
            throw new InvalidOperationException("Tenant transaction runner requires a closed DbContext connection at entry.");

        await _db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // Fail closed if a previous or out-of-band session-level setting exists
            // on a reused physical connection. Reset ROLE to the restricted login role.
            await ExecuteConnectionCommandAsync(connection, null, "RESET ROLE;", null, null, cancellationToken);
            await ExecuteConnectionCommandAsync(
                connection,
                null,
                "SELECT set_config('app.tenant_id', '', false);",
                null,
                null,
                cancellationToken);
            await ExecuteConnectionCommandAsync(
                connection,
                null,
                "SELECT set_config('app.actor_id', '', false);",
                null,
                null,
                cancellationToken);

            await using var transaction = await _db.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                var dbTransaction = transaction.GetDbTransaction();
                await ExecuteConnectionCommandAsync(
                    connection,
                    dbTransaction,
                    "SET LOCAL ROLE " + RuntimeRole + ";",
                    null,
                    null,
                    cancellationToken);

                await ExecuteConnectionCommandAsync(
                    connection,
                    dbTransaction,
                    "SELECT set_config('app.tenant_id', @tenant_id, true);",
                    "tenant_id",
                    tenantId.ToString(),
                    cancellationToken);

                await ExecuteConnectionCommandAsync(
                    connection,
                    dbTransaction,
                    "SELECT set_config('app.actor_id', @actor_id, true);",
                    "actor_id",
                    actorId.Value.ToString(),
                    cancellationToken);

                var result = await operation(cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await ExecuteAsync(async token =>
        {
            await operation(token);
            return true;
        }, cancellationToken);
    }

    private static async Task ExecuteConnectionCommandAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string commandText,
        string? parameterName,
        string? parameterValue,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = transaction;

        if (parameterName is not null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.DbType = DbType.String;
            parameter.Value = parameterValue
                ?? throw new ArgumentNullException(nameof(parameterValue));
            command.Parameters.Add(parameter);
        }

        if (commandText.StartsWith("SELECT", StringComparison.Ordinal))
            await command.ExecuteScalarAsync(cancellationToken);
        else
            await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
