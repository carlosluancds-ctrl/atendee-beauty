using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Atendee.Api.Security;

/// <summary>
/// V92: bounded request sizes and header timeouts for the JSON-only API. The API
/// accepts no file uploads, so a small fixed body ceiling is safe and limits
/// memory/CPU abuse before authentication, antiforgery or JSON parsing run.
/// </summary>
public static class ApiRequestLimits
{
    public const long MaxRequestBodyBytes = 1024 * 1024;
    public const int MaxRequestHeadersTotalSizeBytes = 32 * 1024;
    public const int MaxRequestHeaderCount = 100;
    public const int MaxRequestLineSizeBytes = 8 * 1024;
    public static readonly TimeSpan RequestHeadersTimeout = TimeSpan.FromSeconds(15);

    public static void Apply(KestrelServerLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.MaxRequestBodySize = MaxRequestBodyBytes;
        limits.MaxRequestHeadersTotalSize = MaxRequestHeadersTotalSizeBytes;
        limits.MaxRequestHeaderCount = MaxRequestHeaderCount;
        limits.MaxRequestLineSize = MaxRequestLineSizeBytes;
        limits.RequestHeadersTimeout = RequestHeadersTimeout;
    }
}

/// <summary>
/// Rejects requests that declare a body larger than the ceiling with HTTP 413 before
/// routing, authentication, antiforgery or body parsing, and caps undeclared
/// (chunked) bodies through the server's max-request-body-size feature when present.
/// </summary>
public sealed class RequestBodySizeLimitMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var declaredLength = context.Request.ContentLength;
        if (declaredLength is > ApiRequestLimits.MaxRequestBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.Headers.Connection = "close";
            await context.Response.WriteAsJsonAsync(new { status = "payload_too_large" });
            return;
        }

        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = ApiRequestLimits.MaxRequestBodyBytes;
        }

        await next(context);
    }
}