using Atendee.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
        var stamp = await userManager.UpdateSecurityStampAsync(user);
        EnsureSucceeded(stamp, "Could not revoke existing operational sessions.");

        await auditWriter.WriteAsync("operational_account.sessions_revoked", actorContext.ActorId,
            identityUserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(message + " " +
                string.Join("; ", result.Errors.Select(error => error.Code)));
    }
}
