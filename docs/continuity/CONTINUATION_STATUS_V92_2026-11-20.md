# Continuation status V92 - bounded API request sizes and Kestrel request limits

Date: 2026-11-20
Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
Branch: security/csrf-webhook-antiforgery-v23

## Validated implementation

- Feature implementation: 98d8fc47009e67dfd691180261344ce260d41b9c (`feat: bound API request body size and Kestrel request limits`), a single commit on top of the V91 documentation head ff33a76f80cccb76023ff267f0b266ecaef5b004.
- Full PostgreSQL 18 workflow run: 44667879306, run 255, success on that exact SHA (every job step success, observed through the public GitHub Actions jobs API).
- Local validation before push: `git diff --check` clean (only line-ending normalization notices) and Release build of Atendee.Api and Atendee.Api.HttpChecks with 0 warnings and 0 errors.
- The workflow log itself cannot be read without authentication. The HTTP checks step is fail-fast (Ensure throws on any failed assertion), so its success implies the new assertions passed.

## Delivered

1. New `src/Atendee.Api/Security/ApiRequestLimits.cs`: Kestrel limits applied at startup - MaxRequestBodySize 1 MiB (the API is JSON-only and accepts no uploads), MaxRequestHeadersTotalSize 32 KiB, MaxRequestHeaderCount 100, MaxRequestLineSize 8 KiB and RequestHeadersTimeout 15 seconds.
2. New `RequestBodySizeLimitMiddleware` placed after HTTPS redirection and before the exception handler, routing, rate limiting, authentication and antiforgery: a request declaring Content-Length above 1 MiB gets HTTP 413 `{"status":"payload_too_large"}` with `Connection: close` and the baseline security headers; when the server exposes a writable max-request-body-size feature (Kestrel), undeclared/chunked bodies are capped to the same ceiling.
3. New HTTP checks: the configured Kestrel limits equal the constants; a body of 1 MiB + 1 byte is rejected with 413, minimal body, no-store and security headers; a body of exactly 1 MiB is not rejected by the size limit.
4. No migration, business rule, provider, credential, production configuration or ordering change. Authentication-before-antiforgery, tenant isolation, RLS, mandatory audit, transaction runner and MFA/TOTP protections are untouched.

## Limits and notes

- WebApplicationFactory uses TestServer, which does not enforce Kestrel limits at the socket level. Real Kestrel enforcement of chunked bodies, header size/count and timeouts is therefore covered only by asserting the configured values; the Content-Length 413 path is exercised end to end.
- If a future endpoint must accept larger bodies (for example file uploads), give it endpoint-specific limit metadata and a deliberate review; do not raise the global ceiling.
- A reverse proxy in front of the API should enforce equal or stricter limits; this is deployment-specific and was not configured.

## Still blocked or requiring decisions

Unchanged from V91: appointment rescheduling/cancellation policy; outbox dispatcher and handlers with provider/credential/cost approvals; Data Protection and limiter HMAC coordinated key rotation; Platform Admin and A1/A2 actions; WhatsApp Cloud API (Meta Business approval, provider, credentials, costs); independent security review, backup/restore validation, trusted proxy configuration, orchestrator probe wiring to /health/live and /health/ready, and deploy decisions. CI success is not production authorization.

## Continuation

Read CLAUDE.md, docs/continuity/CONTINUE_HERE.md, this status, docs/continuity/manifest.json and docs/technical/DISTRIBUTED_AUTH_RATE_LIMITING_V1.md. Active worktree: C:\Users\iaras\Downloads\atendee-beauty-v85-work. Preserve C:\Users\iaras\Downloads\atendee-beauty-git untouched.