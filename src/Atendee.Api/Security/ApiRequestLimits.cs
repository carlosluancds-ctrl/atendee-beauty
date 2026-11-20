using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Atendee.Api.Security;

/// <summary>
/// V92/V94: bounded request and response sizes for the JSON-only API. The API
/// accepts no file uploads and returns no large payloads, so a small fixed
/// ceiling is safe and limits memory/CPU abuse.
/// </summary>
public static class ApiRequestLimits
{
    public const long MaxRequestBodyBytes = 1024 * 1024;
    public const long MaxResponseBodyBytes = 1024 * 1024;
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

/// <summary>
/// V94: Caps response body size to prevent memory exhaustion from accidentally large
/// responses. Wraps the response body stream and fails with HTTP 500 if the ceiling
/// is exceeded. Runs after routing and authentication but before the response is sent.
/// </summary>
public sealed class ResponseBodySizeLimitMiddleware(RequestDelegate next)
{
    private sealed class LimitedResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _written;

        public LimitedResponseStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            CheckLimit(count);
            _inner.Write(buffer, offset, count);
        }

        public override void WriteByte(byte value)
        {
            CheckLimit(1);
            _inner.WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            CheckLimit(count);
            return _inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            CheckLimit(buffer.Length);
            return _inner.WriteAsync(buffer, cancellationToken);
        }

        private void CheckLimit(int count)
        {
            _written += count;
            if (_written > _limit)
            {
                throw new InvalidOperationException("Response body size limit exceeded");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var originalBody = context.Response.Body;
        try
        {
            context.Response.Body = new LimitedResponseStream(originalBody, ApiRequestLimits.MaxResponseBodyBytes);
            await next(context);
        }
        catch (InvalidOperationException ex) when (ex.Message == "Response body size limit exceeded")
        {
            // Response body exceeded the limit - we can't send a JSON error because the body is already corrupted
            // Log and close the connection
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.Headers.Connection = "close";
            // Don't write a body - just close
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}