using System.Net;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Npgsql;

namespace Atendee.Infrastructure.Web;

public sealed class PostgresDistributedAuthenticationRateLimiter(
    NpgsqlDataSource dataSource,
    byte[] partitionKeySecret,
    ILogger<PostgresDistributedAuthenticationRateLimiter> logger)
    : IDistributedAuthenticationRateLimiter
{
    private static readonly TimeSpan RetentionAfterWindow = TimeSpan.FromMinutes(5);

    private readonly byte[] _partitionKeySecret = partitionKeySecret.ToArray();

    private const string AcquireSql = """
        WITH expired AS
        (
            DELETE FROM identity."AuthenticationRateLimitBuckets"
             WHERE ctid IN
             (
                 SELECT ctid
                   FROM identity."AuthenticationRateLimitBuckets"
                  WHERE "ExpiresAtUtc" <= @now
                  ORDER BY "ExpiresAtUtc"
                  LIMIT 256
             )
            RETURNING 1
        )
        INSERT INTO identity."AuthenticationRateLimitBuckets" AS bucket
            ("Policy", "PartitionHash", "WindowStartedAtUtc", "RequestCount", "ExpiresAtUtc")
        VALUES
            (@policy, @partitionHash, @windowStartedAtUtc, 1, @expiresAtUtc)
        ON CONFLICT ("Policy", "PartitionHash", "WindowStartedAtUtc")
        DO UPDATE
           SET "RequestCount" = bucket."RequestCount" + 1,
               "ExpiresAtUtc" = GREATEST(
                   bucket."ExpiresAtUtc",
                   EXCLUDED."ExpiresAtUtc")
        RETURNING "RequestCount";
        """;

    public async Task<DistributedRateLimitDecision> TryAcquireAsync(
        string policy,
        IPAddress? remoteIpAddress,
        int permitLimit,
        TimeSpan window,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        if (policy.Length > 64)
            throw new ArgumentOutOfRangeException(nameof(policy));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitLimit);
        if (window <= TimeSpan.Zero || window > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(window));

        var now = utcNow.ToUniversalTime();
        var windowTicks = window.Ticks;
        var windowStartTicks = now.UtcDateTime.Ticks / windowTicks * windowTicks;
        var windowStartedAt = new DateTimeOffset(windowStartTicks, TimeSpan.Zero);
        var windowEndsAt = windowStartedAt.Add(window);
        var expiresAt = windowEndsAt.Add(RetentionAfterWindow);
        var partitionHash = CreatePartitionHash(remoteIpAddress);

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(AcquireSql, connection)
            {
                CommandTimeout = 3
            };
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("policy", policy);
            command.Parameters.AddWithValue("partitionHash", partitionHash);
            command.Parameters.AddWithValue("windowStartedAtUtc", windowStartedAt);
            command.Parameters.AddWithValue("expiresAtUtc", expiresAt);

            var requestCount = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken));

            var retryAfter = windowEndsAt - now;
            return requestCount <= permitLimit
                ? new DistributedRateLimitDecision(true, true, TimeSpan.Zero)
                : new DistributedRateLimitDecision(
                    true,
                    false,
                    retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Distributed authentication rate limiting is unavailable for policy {Policy}; request will be rejected.",
                policy);
            return DistributedRateLimitDecision.Unavailable;
        }
    }

    private byte[] CreatePartitionHash(IPAddress? remoteIpAddress)
    {
        // Prefix the address family so the same byte sequence cannot collide
        // between IPv4 and IPv6. Null addresses share a deliberately restrictive
        // unknown-client partition rather than bypassing the limiter.
        var address = remoteIpAddress?.IsIPv4MappedToIPv6 == true
            ? remoteIpAddress.MapToIPv4()
            : remoteIpAddress;
        var addressBytes = address?.GetAddressBytes() ?? [];
        var material = new byte[addressBytes.Length + 1];
        material[0] = address is null
            ? (byte)0
            : address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? (byte)4
                : (byte)6;
        addressBytes.CopyTo(material, 1);
        return HMACSHA256.HashData(_partitionKeySecret, material);
    }
}