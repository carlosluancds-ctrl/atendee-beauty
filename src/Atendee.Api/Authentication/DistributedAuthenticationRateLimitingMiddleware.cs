using Atendee.Infrastructure.Web;

namespace Atendee.Api.Authentication;

public sealed record DistributedAuthenticationRateLimitMetadata(
    string Policy,
    int PermitLimit,
    TimeSpan Window);

public sealed class DistributedAuthenticationRateLimitingMiddleware(
    RequestDelegate next,
    ILogger<DistributedAuthenticationRateLimitingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var metadata = context.GetEndpoint()?.Metadata
            .GetMetadata<DistributedAuthenticationRateLimitMetadata>();
        if (metadata is null)
        {
            await next(context);
            return;
        }

        var limiter = context.RequestServices
            .GetService<IDistributedAuthenticationRateLimiter>();
        if (limiter is null)
        {
            await RejectUnavailableAsync(context);
            return;
        }

        DistributedRateLimitDecision decision;
        try
        {
            decision = await limiter.TryAcquireAsync(
                metadata.Policy,
                context.Connection.RemoteIpAddress,
                metadata.PermitLimit,
                metadata.Window,
                DateTimeOffset.UtcNow,
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Distributed authentication rate limiting failed for policy {Policy}; rejecting request.",
                metadata.Policy);
            await RejectUnavailableAsync(context);
            return;
        }

        if (!decision.IsAvailable)
        {
            await RejectUnavailableAsync(context);
            return;
        }

        if (!decision.IsAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = Math.Max(
                1,
                (int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(
                new { error = "rate_limit_exceeded" },
                context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static Task RejectUnavailableAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "5";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-RateLimit-Backend"] = "unavailable";
        return context.Response.WriteAsJsonAsync(
            new { error = "rate_limit_unavailable" },
            context.RequestAborted);
    }
}