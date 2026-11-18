using Atendee.Domain.Tenants;
using Npgsql;

namespace Atendee.Infrastructure.Identity;

public sealed record FirstOwnerProvisioningRequest(
    Guid IdentityUserId,
    string TenantName,
    string TenantSlug,
    string TimeZoneId,
    string OwnerDisplayName);

public sealed record FirstOwnerProvisioningResult(
    Guid TenantId,
    Guid OwnerIdentityUserId,
    string TenantSlug);

/// <summary>
/// Trusted, offline first-Owner provisioning. This service is deliberately not
/// registered in the API container and requires the dedicated migrator login.
/// </summary>
public sealed class FirstOwnerProvisioningService
{
    private readonly string _connectionString;

    public FirstOwnerProvisioningService(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }
    public async Task<FirstOwnerProvisioningResult> ProvisionAsync(
        FirstOwnerProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IdentityUserId == Guid.Empty)
            throw new ArgumentException("A valid pre-provisioned identity ID is required.", nameof(request));

        var now = DateTimeOffset.UtcNow;
        var tenant = Tenant.Create(
            request.TenantName,
            request.TenantSlug,
            request.TimeZoneId,
            now);
        var owner = TenantUser.Create(
            tenant.Id,
            request.OwnerDisplayName,
            TenantRole.Owner,
            now);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The operator tool must never run with a superuser or application login.
        await using (var preflight = new NpgsqlCommand(
            "SELECT session_user = 'atendee_migrator'", connection, transaction))
        {
            if (!Equals(await preflight.ExecuteScalarAsync(cancellationToken), true))
                throw new InvalidOperationException(
                    "Provisioning requires the dedicated atendee_migrator database login.");
        }

        // Serialize attempts for this identity even if two operators run the tool
        // at the same time. The unique membership constraint remains the final guard.
        await using (var identityLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@identity_id::text, 9041127))",
            connection, transaction))
        {
            identityLock.Parameters.AddWithValue("identity_id", request.IdentityUserId);
            await identityLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var role = new NpgsqlCommand(
            "SET LOCAL ROLE atendee_identity_owner", connection, transaction))
            await role.ExecuteNonQueryAsync(cancellationToken);

        await using (var identity = new NpgsqlCommand(
            """
            SELECT "TwoFactorEnabled", "EmailConfirmed", "LockoutEnd"
              FROM identity."AspNetUsers"
             WHERE "Id" = @identity_id
            """, connection, transaction))
        {
            identity.Parameters.AddWithValue("identity_id", request.IdentityUserId);
            await using var reader = await identity.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "The identity does not exist. Provisioning never creates login identities.");

            var mfaEnabled = reader.GetBoolean(0);
            var emailConfirmed = reader.GetBoolean(1);
            var lockoutEnd = reader.IsDBNull(2) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(2);

            if (!emailConfirmed)
                throw new InvalidOperationException("The identity email must be confirmed before provisioning.");
            if (!mfaEnabled)
                throw new InvalidOperationException("The identity must complete MFA enrollment before Owner membership.");
            if (lockoutEnd is not null && lockoutEnd > now)
                throw new InvalidOperationException("The identity is currently locked out.");
        }

        await using (var membership = new NpgsqlCommand(
            """
            SELECT EXISTS
            (
                SELECT 1
                  FROM atendee.tenant_users
                 WHERE identity_user_id = @identity_id
            )
            """, connection, transaction))
        {
            membership.Parameters.AddWithValue("identity_id", request.IdentityUserId);
            if (Equals(await membership.ExecuteScalarAsync(cancellationToken), true))
                throw new InvalidOperationException(
                    "The identity already has a salon membership; first-Owner provisioning is single-use.");
        }

        await using (var role = new NpgsqlCommand(
            "SET LOCAL ROLE atendee_owner", connection, transaction))
            await role.ExecuteNonQueryAsync(cancellationToken);

        await using (var createTenant = new NpgsqlCommand(
            """
            INSERT INTO atendee.tenants
                (id, name, slug, time_zone_id, status, created_at_utc)
            VALUES
                (@tenant_id, @name, @slug, @time_zone_id, @status, @created_at_utc)
            """, connection, transaction))
        {
            createTenant.Parameters.AddWithValue("tenant_id", tenant.Id.Value);
            createTenant.Parameters.AddWithValue("name", tenant.Name);
            createTenant.Parameters.AddWithValue("slug", tenant.Slug);
            createTenant.Parameters.AddWithValue("time_zone_id", tenant.TimeZoneId);
            createTenant.Parameters.AddWithValue("status", (short)tenant.Status);
            createTenant.Parameters.AddWithValue("created_at_utc", tenant.CreatedAtUtc);
            await createTenant.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var createOwner = new NpgsqlCommand(
            """
            INSERT INTO atendee.tenant_users
                (tenant_id, identity_user_id, display_name, role, is_active, created_at_utc)
            VALUES
                (@tenant_id, @identity_id, @display_name, @role, true, @created_at_utc)
            """, connection, transaction))
        {
            createOwner.Parameters.AddWithValue("tenant_id", tenant.Id.Value);
            createOwner.Parameters.AddWithValue("identity_id", request.IdentityUserId);
            createOwner.Parameters.AddWithValue("display_name", owner.DisplayName);
            createOwner.Parameters.AddWithValue("role", (short)TenantRole.Owner);
            createOwner.Parameters.AddWithValue("created_at_utc", now);
            await createOwner.ExecuteNonQueryAsync(cancellationToken);
        }

        // Migration 010's deferred constraint trigger validates the invariant
        // at commit, after both rows have been inserted in this transaction.
        await transaction.CommitAsync(cancellationToken);

        return new FirstOwnerProvisioningResult(
            tenant.Id.Value,
            request.IdentityUserId,
            tenant.Slug);
    }
}
