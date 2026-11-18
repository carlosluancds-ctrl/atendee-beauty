using Atendee.Application.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Atendee.Infrastructure.Identity;

public enum OperationalIdentityActivationResult
{
    Activated,
    InvalidOrExpired,
    PasswordPolicyRejected
}

/// <summary>
/// Redeems an operator-issued activation token. It never creates identities and
/// never issues an authentication session.
/// </summary>
public sealed class OperationalIdentityActivationService(
    UserManager<OperationalIdentityUser> userManager,
    AtendeeIdentityDbContext db,
    ISecurityAuditWriter auditWriter)
{
    public async Task<OperationalIdentityActivationResult> ActivateAsync(
        string? token,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (!ActivationTokenCodec.IsValidFormat(token))
            return OperationalIdentityActivationResult.InvalidOrExpired;

        if (string.IsNullOrWhiteSpace(password) || password.Length > 1024)
            return OperationalIdentityActivationResult.PasswordPolicyRejected;

        var now = DateTimeOffset.UtcNow;
        var tokenHash = ActivationTokenCodec.HashToken(token!);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        Guid userId;

        // Discover the target without taking a token lock. Every activation and
        // re-issue then locks in the same order: identity row, activation row.
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction.GetDbTransaction();
            lookup.CommandText = """
                SELECT "UserId"
                  FROM identity."OperationalIdentityActivationTokens"
                 WHERE "TokenHash" = @token_hash
                """;
            AddParameter(lookup, "token_hash", tokenHash);
            var value = await lookup.ExecuteScalarAsync(cancellationToken);
            if (value is not Guid foundUserId)
                return OperationalIdentityActivationResult.InvalidOrExpired;
            userId = foundUserId;
        }

        await using (var identityLock = connection.CreateCommand())
        {
            identityLock.Transaction = transaction.GetDbTransaction();
            identityLock.CommandText = """
                SELECT "Id"
                  FROM identity."AspNetUsers"
                 WHERE "Id" = @user_id
                 FOR UPDATE
                """;
            AddParameter(identityLock, "user_id", userId);
            if (await identityLock.ExecuteScalarAsync(cancellationToken) is not Guid)
                return OperationalIdentityActivationResult.InvalidOrExpired;
        }

        DateTimeOffset expiresAt;
        DateTimeOffset? consumedAt;
        await using (var tokenLock = connection.CreateCommand())
        {
            tokenLock.Transaction = transaction.GetDbTransaction();
            tokenLock.CommandText = """
                SELECT "ExpiresAtUtc", "ConsumedAtUtc"
                  FROM identity."OperationalIdentityActivationTokens"
                 WHERE "UserId" = @user_id
                   AND "TokenHash" = @token_hash
                 FOR UPDATE
                """;
            AddParameter(tokenLock, "user_id", userId);
            AddParameter(tokenLock, "token_hash", tokenHash);
            await using var reader = await tokenLock.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return OperationalIdentityActivationResult.InvalidOrExpired;
            expiresAt = reader.GetFieldValue<DateTimeOffset>(0);
            consumedAt = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
        }

        if (consumedAt is not null || expiresAt <= now)
            return OperationalIdentityActivationResult.InvalidOrExpired;

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null ||
            user.EmailConfirmed ||
            user.PasswordHash is not null ||
            user.TwoFactorEnabled ||
            (user.LockoutEnd is not null && user.LockoutEnd > now))
        {
            return OperationalIdentityActivationResult.InvalidOrExpired;
        }

        var passwordResult = await userManager.AddPasswordAsync(user, password);
        if (!passwordResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return OperationalIdentityActivationResult.PasswordPolicyRejected;
        }

        user.EmailConfirmed = true;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            throw new InvalidOperationException("Could not confirm the operational identity email.");

        await using (var consume = connection.CreateCommand())
        {
            consume.Transaction = transaction.GetDbTransaction();
            consume.CommandText = """
                UPDATE identity."OperationalIdentityActivationTokens"
                   SET "ConsumedAtUtc" = @consumed_at
                 WHERE "UserId" = @user_id
                   AND "TokenHash" = @token_hash
                   AND "ConsumedAtUtc" IS NULL
                   AND "ExpiresAtUtc" > @consumed_at
                """;
            AddParameter(consume, "consumed_at", now);
            AddParameter(consume, "user_id", userId);
            AddParameter(consume, "token_hash", tokenHash);
            if (await consume.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Activation token could not be consumed atomically.");
        }

        await auditWriter.WriteAsync(
            "operational_identity.activation_succeeded",
            user.Id,
            user.Id,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return OperationalIdentityActivationResult.Activated;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}