using System.Net;

namespace Atendee.Infrastructure.Web;

public readonly record struct DistributedRateLimitDecision(
    bool IsAvailable,
    bool IsAllowed,
    TimeSpan RetryAfter)
{
    public static DistributedRateLimitDecision Unavailable =>
        new(false, false, TimeSpan.FromSeconds(5));
}

public interface IDistributedAuthenticationRateLimiter
{
    Task<DistributedRateLimitDecision> TryAcquireAsync(
        string policy,
        IPAddress? remoteIpAddress,
        int permitLimit,
        TimeSpan window,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
public sealed class AuthenticationRateLimitPartitionKey
{
    private readonly byte[] _value;

    public AuthenticationRateLimitPartitionKey(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length < 32)
            throw new ArgumentException("The authentication rate-limit partition key must contain at least 32 bytes.", nameof(value));

        _value = value.ToArray();
    }

    public byte[] CopyValue() => _value.ToArray();
}