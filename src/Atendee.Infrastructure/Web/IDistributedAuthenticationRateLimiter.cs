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