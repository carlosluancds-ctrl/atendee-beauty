using Atendee.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atendee.Infrastructure.Identity;

public sealed record OperationalIdentityActivationIssue(
    Guid IdentityUserId,
    string Email,
    string Token,
    DateTimeOffset ExpiresAtUtc,
    bool IsReissue);

/// <summary>
/// Trusted offline creation/re-issue of pending operational identities.
/// Requires the dedicated atendee_identity_provisioner database login.
/// </summary>
public sealed class OperationalIdentityProvisioningService(
    UserManager<OperationalIdentityUser> userManager,
    AtendeeIdentityDbContext db,
    ISecurityAuditWriter auditWriter)
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public Task<OperationalIdentityActivationIssue> CreatePendingAsync(
        string email,
        CancellationToken cancellationToken = default)
        => IssueAsync(email, reissue: false, cancellationToken);

    public Task<OperationalIdentityActivationIssue> ReissuePendingAsync(
        string email,
        CancellationToken cancellationToken = default)
        => IssueAsync(email, reissue: true, cancellationToken);

    private async Task<OperationalIdentityActivationIssue> IssueAsync(
        string email,
        bool reissue,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalizedInput = email.Trim();
        if (normalizedInput.Length > 256 ||
            !System.Net.Mail.MailAddress.TryCreate(normalizedInput, out var address) ||
            !string.Equals(address.Address, normalizedInput, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A valid email address is required.", nameof(email));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();

        await using (var preflight = connection.CreateCommand())
        {
            preflight.Transaction = transaction.GetDbTransaction();
            preflight.CommandText = "SELECT session_user = 'atendee_identity_provisioner'";
            if (!Equals(await preflight.ExecuteScalarAsync(cancellationToken), true))
                throw new InvalidOperationException(
                    "Provisioning requires the dedicated atendee_identity_provisioner database login.");
        }

        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL ROLE atendee_identity_owner",
            cancellationToken);

        var user = await userManager.FindByEmailAsync(normalizedInput);
        if (!reissue)
        {
            if (user is not null)
                throw new InvalidOperationException(
                    "An operational identity already exists for this normalized email address.");

            user = new OperationalIdentityUser
            {
                UserName = normalizedInput,
                Email = normalizedInput,
                EmailConfirmed = false,
                PasswordHash = null,
                TwoFactorEnabled = false,
                LockoutEnabled = true
            };

            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
                throw new InvalidOperationException(
                    "Could not create the pending operational identity: " +
                    string.Join("; ", created.Errors.Select(error => error.Code)));
        }
        else
        {
            if (user is null)
                throw new InvalidOperationException(
                    "Only an existing, unlocked, unactivated identity can receive a replacement link.");

            // Serialize re-issue with activation. Both operations lock the
            // AspNetUsers row before locking/updating the activation-token row.
            await using (var identityLock = connection.CreateCommand())
            {
                identityLock.Transaction = transaction.GetDbTransaction();
                identityLock.CommandText = """
                    SELECT "Id"
                      FROM identity."AspNetUsers"
                     WHERE "Id" = @user_id
                     FOR UPDATE
                    """;
                AddParameter(identityLock, "user_id", user.Id);
                if (await identityLock.ExecuteScalarAsync(cancellationToken) is not Guid)
                    throw new InvalidOperationException("The pending identity no longer exists.");
            }

            user = await userManager.FindByIdAsync(user.Id.ToString());
            if (user is null ||
                user.EmailConfirmed ||
                user.PasswordHash is not null ||
                user.TwoFactorEnabled ||
                (user.LockoutEnd is not null && user.LockoutEnd > DateTimeOffset.UtcNow))
                throw new InvalidOperationException(
                    "Only an existing, unlocked, unactivated identity can receive a replacement link.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(TokenLifetime);
        var token = ActivationTokenCodec.CreateToken();
        var tokenHash = ActivationTokenCodec.HashToken(token);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity."OperationalIdentityActivationTokens"
                ("UserId", "TokenHash", "CreatedAtUtc", "ExpiresAtUtc", "ConsumedAtUtc")
            VALUES
                ({user.Id}, {tokenHash}, {now}, {expiresAt}, NULL)
            ON CONFLICT ("UserId") DO UPDATE
               SET "TokenHash" = EXCLUDED."TokenHash",
                   "CreatedAtUtc" = EXCLUDED."CreatedAtUtc",
                   "ExpiresAtUtc" = EXCLUDED."ExpiresAtUtc",
                   "ConsumedAtUtc" = NULL
            """, cancellationToken);

        await auditWriter.WriteAsync(
            reissue
                ? "operational_identity.activation_reissued"
                : "operational_identity.activation_issued",
            null,
            user.Id,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new OperationalIdentityActivationIssue(
            user.Id,
            user.Email!,
            token,
            expiresAt,
            reissue);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}