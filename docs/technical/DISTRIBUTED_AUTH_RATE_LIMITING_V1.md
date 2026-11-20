# Distributed authentication rate limiting (V89)

## Configuration

When operational Identity is enabled, the API requires:

- AuthenticationRateLimiting__PartitionKeySecret: base64 encoding of at least 32 cryptographically random bytes. Generate a value with openssl rand -base64 32.
- Every API instance sharing the same Identity database must use the exact same secret. Supply it through the deployment secret manager/environment; never commit it or log it.

The secret is used only as an HMAC-SHA256 key for IP partition identifiers. Changing it changes every partition digest and effectively starts fresh rate-limit buckets, so rotate it only with a coordinated operational plan.

## Storage and policy

Apply db/migrations/021_create_distributed_authentication_rate_limits.sql after migration 020 as atendee_migrator.

The table lives in the isolated identity schema. The API's atendee_auth_login database role receives only the table privileges needed to atomically insert/increment buckets and remove expired rows. Public access is revoked. Raw client IP addresses are never stored.

Each bucket is keyed by policy, HMAC-SHA256(IP address including address-family discriminator), and a UTC-aligned fixed-window start. IPv4-mapped IPv6 addresses are normalized to IPv4. A missing remote address maps to one shared restrictive unknown partition. The PostgreSQL INSERT ... ON CONFLICT DO UPDATE ... RETURNING statement serializes concurrent increments across API instances; rejected attempts also increment the counter.

| Policy | Limit | Window |
|---|---:|---:|
| auth-antiforgery | 30 | 1 minute |
| auth-login | 5 | 1 minute |
| auth-activation | 5 | 1 minute |
| auth-mfa (challenge and enrollment) | 10 | 1 minute |
| bootstrap-health | 60 | 1 minute, existing process-local ASP.NET limiter |

Authentication policies execute after trusted forwarded-header processing and endpoint routing, but before authentication and antiforgery middleware. The existing authentication-before-antiforgery order remains unchanged.

Buckets expire five minutes after their window closes. Each successful acquisition opportunistically deletes up to 256 expired rows. A hosted cleanup worker also runs once per minute and deletes up to 256 expired rows per tick, so cleanup continues during idle periods without an external scheduler. Both paths use the same indexed expiry and are safe across concurrent API instances.

## Failure behavior

The distributed store has a three-second connection/command timeout. If PostgreSQL is unavailable, the API returns 503 Service Unavailable, Retry-After: 5, and rate_limit_unavailable; the protected endpoint is not invoked. There is deliberately no process-local fallback for authentication policies because that would allow each instance to grant an independent quota. A consumed quota returns 429 Too Many Requests with Retry-After.

## Verification

tests/Atendee.Api.HttpChecks exercises two independent limiter instances against the shared PostgreSQL database with 40 concurrent acquisitions and a seven-permit limit; it also verifies the persisted count, hashed partitions, IPv4-mapped normalization, window rollover, expired-row cleanup, 429 middleware behavior, and fail-closed 503 behavior during a simulated database outage.