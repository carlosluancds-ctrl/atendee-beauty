using Atendee.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atendee.Infrastructure.Identity;

public sealed class OperationalAccountSecurityService(
    UserManager<OperationalIdentityUser> userManager,
    AtendeeIdentityDbContext db,
    ISecurityAuditWriter auditWriter,
    ICurrentActorContext actorContext) : IOperationalAccountSecurityService
{
    public async Task<bool> DisableAsync(Guid identityUserId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(identityUserId.ToString());
        if (user is null)
            return false;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockAndReloadAsync(user, cancellationToken))
            return false;

        if (!user.LockoutEnabled)
        {
            var lockoutEnabled = await userManager.SetLockoutEnabledAsync(user, true);
            EnsureSucceeded(lockoutEnabled, "Could not enable account lockout.");
        }

        var locked = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        EnsureSucceeded(locked, "Could not disable the operational identity.");

        var stamp = await userManager.UpdateSecurityStampAsync(user);
        EnsureSucceeded(stamp, "Could not revoke existing operational sessions.");

        await auditWriter.WriteAsync("operational_account.disabled", actorContext.ActorId,
            identityUserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RevokeSessionsAsync(Guid identityUserId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await userManager.FindByIdAsync(identityUserId.ToString());
        if (user is null)
            return false;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await LockAndReloadAsync(user, cancellationToken))
            return false;

        var stamp = await userManager.UpdateSecurityStampAsync(user);
        EnsureSucceeded(stamp, "Could not revoke existing operational sessions.");

        await auditWriter.WriteAsync("operational_account.sessions_revoked", actorContext.ActorId,
            identityUserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> LockAndReloadAsync(
        OperationalIdentityUser user,
        CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("An identity transaction is required before locking a user row.");
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            SELECT "Id"
              FROM identity."AspNetUsers"
             WHERE "Id" = @user_id
             FOR UPDATE
            """;
        var userIdParameter = command.CreateParameter();
        userIdParameter.ParameterName = "user_id";
        userIdParameter.Value = user.Id;
        command.Parameters.Add(userIdParameter);

        if (await command.ExecuteScalarAsync(cancellationToken) is not Guid)
            return false;

        await db.Entry(user).ReloadAsync(cancellationToken);
        return true;
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(message + " " +
                string.Join("; ", result.Errors.Select(error => error.Code)));
    }
}
