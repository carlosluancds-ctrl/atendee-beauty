# Continuation status V89 - distributed authentication rate limiting

Date: 2026-11-19
Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
Branch: security/csrf-webhook-antiforgery-v23

## Validated implementation

- Feature implementation: 90c2a2f (`feat: distribute authentication rate limits via postgres`).
- Follow-up test isolation and endpoint/IP partition checks: 82dd5a5, 87b321e, f08a180, 09dcdca, 92ea4d4, 9d2ed24, 95e4956 and 90a5bc6.
- Latest validated head: 90a5bc6c79fa53a91471b3ec0204c8e9b17b3e34.
- Full PostgreSQL 18 workflow run: 44660361862, run 248, success on that exact SHA.
- Workflow log explicitly records PASS for the two-instance/40-concurrent-request atomic quota assertion, IPv4-mapped IPv6 normalization, bounded expiry cleanup, fail-closed 503 behavior and 429 behavior.

## Delivered

1. Migration `db/migrations/021_create_distributed_authentication_rate_limits.sql` creates the shared `identity.AuthenticationRateLimitBuckets` table, composite primary key, 32-byte digest constraint, positive request count, expiry constraint and expiry index. Public access is revoked; the API role receives only the required SELECT/INSERT/UPDATE/DELETE grants.
2. `PostgresDistributedAuthenticationRateLimiter` uses PostgreSQL `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` as the atomic shared counter. Independent API instances use the same database quota; no in-memory fallback weakens quotas.
3. Client addresses are normalized for IPv4-mapped IPv6 and converted to HMAC-SHA256 partition hashes with an address-family discriminator. Raw IP addresses are not stored. A missing address maps to a restrictive shared unknown-client partition.
4. Fixed UTC windows and per-policy quotas are attached to antiforgery (30/minute), login (5/minute), activation (5/minute), MFA challenge/enrollment (10/minute). Existing health throttling remains process-local and is not represented as distributed.
5. Authentication limiting runs after trusted forwarded-header resolution and routing, before authentication and antiforgery. Existing authentication-before-antiforgery ordering is preserved.
6. A 429 response includes `Retry-After`. If PostgreSQL is unavailable, requests fail closed with 503, `Retry-After: 5`, `Cache-Control: no-store` and `rate_limit_unavailable`; the protected endpoint is not invoked.
7. Expired buckets have five-minute post-window retention. Acquisition opportunistically removes up to 256 expired rows; a hosted worker repeats bounded cleanup once per minute.
8. The partition HMAC secret is required when operational Identity is enabled, must be base64 for at least 32 cryptographically random bytes and must be identical across API instances sharing the database. It must be supplied through deployment secrets/environment, never committed or logged. Changing it resets effective partitions; rotation requires a coordinated operational plan.

## CI evidence - run 44660361862 (run 248)

The workflow completed successfully on exact head `90a5bc6c79fa53a91471b3ec0204c8e9b17b3e34`. Every job step passed:

- Frontend dependency audit, tests/build and portal browser E2E: 8 passed.
- Locked restore and Release build: succeeded, zero warnings and zero errors.
- Domain checks: 79/79 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 43/43 passed.
- Identity/TOTP/tenant-context checks: 49/49 passed.
- HTTP authentication, session-security and full-stack browser integration checks: passed.
- V89 workflow log confirms two independent limiter instances share one atomic PostgreSQL fixed-window counter under 40 concurrent acquisitions with exactly seven permits and no lost increments/excess permits.
- Additional logged PASS assertions cover persisted request count and 32-byte keyed digest (not plaintext IP), independent IP partitions, fresh UTC window, IPv4-mapped normalization, no plaintext IP schema columns, bounded expired-bucket cleanup, simulated PostgreSQL outage returning fail-closed 503 without invoking the endpoint, and exhausted quota returning 429 with Retry-After.
- Disposable database credentials were cleared and PostgreSQL container removed.

CI URL: https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44660361862

## Remaining work - not claimed complete

1. Appointment rescheduling and customer cancellation windows remain product-policy decisions. Do not invent replacement/history semantics or customer-facing cancellation rules; obtain explicit owner approval before implementation.
2. The transactional outbox is persisted atomically but still has no dispatcher/consumer. Implement only with real handlers, idempotent delivery, retry/backoff, poison/dead-letter handling and observability. External providers remain blocked pending provider, credential, cost and product approval.
3. Data Protection key-ring persistence/protection and certificate rotation require a coordinated compatibility/rotation plan and deployment-specific path/secret management. Do not rotate keys or alter production configuration.
4. Platform Admin and cross-tenant A1/A2 actions remain blocked pending explicit approval of realm, actor/scope, step-up MFA/four-eyes, self/last-Owner rules and tenant-scoped audit.
5. Production readiness still requires independent security review, backup/restore validation, trusted proxy configuration and deployment-specific secret/key controls. CI success is not production authorization.

## Safeguards and handoff

- `main` untouched. No deployment, production data/configuration changes or external provider activation.
- Branch remains `security/csrf-webhook-antiforgery-v23`.
- Worktree must remain clean after the documentation checkpoint is pushed.
- Active worktree: `C:\Users\iaras\Downloads\atendee-beauty-v85-work`.
- Preserve the older invalid clone `C:\Users\iaras\Downloads\atendee-beauty-git` untouched.
- Read `CLAUDE.md`, `docs/continuity/CONTINUE_HERE.md`, this highest-numbered status and `docs/technical/DISTRIBUTED_AUTH_RATE_LIMITING_V1.md` before continuing.
