using Atendee.Application.Abstractions;
using Atendee.Application.Models;
using Atendee.Domain.Primitives;
using Atendee.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Atendee.Infrastructure.Identity;

/// <summary>
/// Resolves membership through a SECURITY DEFINER function. The auth database
/// role has no direct SELECT privilege on tenant_users or tenants. Identity is
/// placed in a transaction-local server-side context before calling the function.
/// </summary>
public sealed class PostgresTenantMembershipResolver(
    AtendeeIdentityDbContext identityDb) : ITenantMembershipResolver
{
    private const string Query = """
        SELECT
            identity_user_id AS "IdentityUserId",
            tenant_id AS "TenantId",
            display_name AS "DisplayName",
            role AS "Role",
            is_active AS "IsActive",
            tenant_status AS "TenantStatus",
            tenant_name AS "TenantName",
            tenant_slug AS "TenantSlug",
            tenant_time_zone_id AS "TenantTimeZoneId"
        FROM atendee.resolve_tenant_membership()
        """;

    public async Task<TenantMembershipSnapshot?> ResolveAsync(
        Guid identityUserId,
        CancellationToken cancellationToken = default)
    {
        if (identityUserId == Guid.Empty)
            return null;

        // SET LOCAL is scoped to this transaction. PostgreSQL resets the
        // identity context on commit/rollback before a pooled connection is reused.
        await using var transaction =
            await identityDb.Database.BeginTransactionAsync(cancellationToken);

        var trustedIdentity = identityUserId.ToString("D");
        await identityDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.identity_user_id', {trustedIdentity}, true)",
            cancellationToken);

        var result = await identityDb.Database
            .SqlQueryRaw<TenantMembershipLookup>(Query)
            .SingleOrDefaultAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (result is null)
            return null;

        if (!Enum.IsDefined(typeof(TenantRole), result.Role) ||
            !Enum.IsDefined(typeof(TenantStatus), result.TenantStatus))
        {
            throw new InvalidOperationException(
                "The stored tenant membership contains an unsupported role or status.");
        }

        if (result.IdentityUserId != identityUserId ||
            result.TenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(result.DisplayName) ||
            string.IsNullOrWhiteSpace(result.TenantName) ||
            string.IsNullOrWhiteSpace(result.TenantSlug) ||
            string.IsNullOrWhiteSpace(result.TenantTimeZoneId))
        {
            throw new InvalidOperationException(
                "The stored tenant membership or tenant profile is incomplete or inconsistent.");
        }

        return new TenantMembershipSnapshot(
            result.IdentityUserId,
            new TenantId(result.TenantId),
            result.DisplayName,
            (TenantRole)result.Role,
            result.IsActive,
            (TenantStatus)result.TenantStatus,
            result.TenantName,
            result.TenantSlug,
            result.TenantTimeZoneId);
    }
}
