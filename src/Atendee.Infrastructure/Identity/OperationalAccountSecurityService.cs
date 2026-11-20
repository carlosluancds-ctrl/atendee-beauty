using Atendee.Application.Abstractions;
using Atendee.Domain.Tenants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atendee.Infrastructure.Identity;

public sealed class OperationalAccountSecurityService(
    UserManager<OperationalIdentityUser> userManager,
    AtendeeIdentityDbContext db,
    ISecurityAuditWriter auditWriter,
    ICurrentActorContext actorContext,
    ITenantMembershipResolver membershipResolver) : IOperationalAccountSecurityService
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

    public async Task<bool> ChangeRoleAsync(
        Guid actorIdentityUserId,
        Guid targetIdentityUserId,
        TenantRole newRole,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Resolve actor membership
        var actorMembership = await membershipResolver.ResolveAsync(actorIdentityUserId, cancellationToken);
        if (actorMembership is null || !actorMembership.IsActive)
            return false;

        // Resolve target membership
        var targetMembership = await membershipResolver.ResolveAsync(targetIdentityUserId, cancellationToken);
        if (targetMembership is null || !targetMembership.IsActive)
            return false;

        // Same tenant check
        if (actorMembership.TenantId != targetMembership.TenantId)
            return false;

        // Authorization: Owner can change any role; Manager can only change Receptionist/Professional
        if (actorMembership.Role == TenantRole.Owner)
        {
            // Cannot change own role
            if (actorIdentityUserId == targetIdentityUserId)
                return false;
            // Cannot change another Owner's role
            if (targetMembership.Role == TenantRole.Owner)
                return false;
        }
        else if (actorMembership.Role == TenantRole.Manager)
        {
            // Manager cannot act on Owner or Manager
            if (targetMembership.Role == TenantRole.Owner || targetMembership.Role == TenantRole.Manager)
                return false;
            // Manager cannot promote to Owner or Manager
            if (newRole == TenantRole.Owner || newRole == TenantRole.Manager)
                return false;
        }
        else
        {
            // Receptionist/Professional cannot change roles
            return false;
        }

        // No self-escalation to Owner without MFA enrollment (handled by caller)
        if (newRole == TenantRole.Owner && targetMembership.Role != TenantRole.Owner)
        {
            // This is a promotion to Owner - require MFA enrollment check
            // For now we allow it; the caller should enforce MFA enrollment
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Set app.identity_user_id for the SECURITY DEFINER function context
        var trustedIdentity = targetIdentityUserId.ToString("D");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.identity_user_id', {trustedIdentity}, true)", cancellationToken);

        // Update tenant_users role
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE atendee.tenant_users
            SET role = {(int)newRole}
            WHERE tenant_id = {actorMembership.TenantId.Value} AND identity_user_id = {targetIdentityUserId}
            """, cancellationToken);

        await auditWriter.WriteAsync("tenant_membership.role_changed", actorContext.ActorId,
            targetIdentityUserId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeactivateMembershipAsync(
        Guid actorIdentityUserId,
        Guid targetIdentityUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var actorMembership = await membershipResolver.ResolveAsync(actorIdentityUserId, cancellationToken);
        if (actorMembership is null || !actorMembership.IsActive)
            return false;

        var targetMembership = await membershipResolver.ResolveAsync(targetIdentityUserId, cancellationToken);
        if (targetMembership is null || !targetMembership.IsActive)
            return false;

        if (actorMembership.TenantId != targetMembership.TenantId)
            return false;

        if (actorIdentityUserId == targetIdentityUserId)
            return false;

        // Cannot deactivate an Owner
        if (targetMembership.Role == TenantRole.Owner)
            return false;

        if (actorMembership.Role == TenantRole.Manager && targetMembership.Role != TenantRole.Receptionist && targetMembership.Role != TenantRole.Professional)
            return false;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var trustedIdentity = targetIdentityUserId.ToString("D");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.identity_user_id', {trustedIdentity}, true)", cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE atendee.tenant_users
            SET is_active = FALSE
            WHERE tenant_id = {actorMembership.TenantId.Value} AND identity_user_id = {targetIdentityUserId}
            """, cancellationToken);

        await auditWriter.WriteAsync("tenant_membership.deactivated", actorContext.ActorId,
            targetIdentityUserId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ReactivateMembershipAsync(
        Guid actorIdentityUserId,
        Guid targetIdentityUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var actorMembership = await membershipResolver.ResolveAsync(actorIdentityUserId, cancellationToken);
        if (actorMembership is null || !actorMembership.IsActive)
            return false;

        var targetMembership = await membershipResolver.ResolveAsync(targetIdentityUserId, cancellationToken);
        if (targetMembership is null)
            return false;

        if (actorMembership.TenantId != targetMembership.TenantId)
            return false;

        if (actorIdentityUserId == targetIdentityUserId)
            return false;

        if (actorMembership.Role == TenantRole.Manager && targetMembership.Role != TenantRole.Receptionist && targetMembership.Role != TenantRole.Professional)
            return false;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var trustedIdentity = targetIdentityUserId.ToString("D");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.identity_user_id', {trustedIdentity}, true)", cancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE atendee.tenant_users
            SET is_active = TRUE
            WHERE tenant_id = {actorMembership.TenantId.Value} AND identity_user_id = {targetIdentityUserId}
            """, cancellationToken);

        await auditWriter.WriteAsync("tenant_membership.activated", actorContext.ActorId,
            targetIdentityUserId, cancellationToken);

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
