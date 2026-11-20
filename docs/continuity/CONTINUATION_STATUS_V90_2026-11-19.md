# Continuation status V90 - database readiness endpoint

Date: 2026-11-19
Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
Branch: security/csrf-webhook-antiforgery-v23

## Validated implementation

- Feature implementation: 399ead5e86aadde12eb7e06a03051aa839767e6e (`feat: add database readiness endpoint`), a single commit on top of the V89 documentation head b2b9249.
- Latest validated implementation head: 399ead5e86aadde12eb7e06a03051aa839767e6e.
- Full PostgreSQL 18 workflow run: 44663263477, run 251, success on that exact SHA.
- The workflow log records PASS for all five new V90 assertions (listed below) and no FAIL lines.
- Local validation before push: `git diff --check` clean (only line-ending normalization notices) and Release build with 0 warnings and 0 errors. Integration checks need a disposable PostgreSQL 18.7 instance and were validated by CI only.

## Delivered

1. `GET /health/ready` is an anonymous endpoint next to `GET /health/live`. It returns `200 {"status":"ready"}` when the databases are reachable and `503 {"status":"unavailable"}` otherwise, always with `Cache-Control: no-store`. The body never contains hosts, connection strings or exception text.
2. `IDatabaseReadinessProbe` (Infrastructure/Web) and `NpgsqlDatabaseReadinessProbe` run `SELECT 1` against the shared identity `NpgsqlDataSource` (the same data source the distributed rate limiter uses) and against the tenant database, under one 2 second linked cancellation timeout. A caller cancellation is rethrown; any dependency failure is logged as a warning with the exception type only and reported as not ready.
3. `AddAtendeeDatabaseReadiness(tenantConnectionString)` registers the probe as a singleton. It must run after the rate-limiting overload of `AddAtendeeIdentity`, which registers the shared `NpgsqlDataSource`.
4. When no databases are configured (Development only), no probe is registered and the endpoint reports ready because there is nothing to check.
5. The endpoint uses the existing `bootstrap-health` policy (60 requests per minute per client IP, process-local). It does not use the distributed authentication limiter, authentication or antiforgery, and exposes nothing tenant-specific.
6. Authentication-before-antiforgery ordering, tenant isolation, RLS, mandatory audit, the transaction runner and MFA/TOTP protections are untouched. No migration, no business rule, no provider, no credential and no production configuration changed.

## CI evidence - run 44663263477 (run 251)

The workflow completed successfully on exact head `399ead5e86aadde12eb7e06a03051aa839767e6e`. Every job step passed:

- Frontend dependency audit, tests/build and portal browser E2E: 8 passed.
- Release build: 0 warnings and 0 errors.
- Domain checks: 79/79.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 43/43.
- Identity/TOTP/tenant-context checks: 49/49.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks passed; full-stack browser E2E passed against the real Kestrel API and disposable PostgreSQL.
- New V90 PASS assertions: probe ready when both databases are reachable; probe not ready when the identity database is unreachable; probe not ready when the tenant database is unreachable; `GET /health/ready` 200 with minimal no-store body, anonymous and without antiforgery; `GET /health/ready` 503 with minimal no-store body and no dependency details.
- Disposable credentials cleared and PostgreSQL container removed.

## Limits and notes

- Readiness proves reachability only. It does not verify migrations, schema, grants/RLS, the limiter HMAC secret or Data Protection keys.
- The 503 HTTP assertion injects an unavailable probe through test services; the real failure paths are covered by the probe checks against unreachable databases.
- The `bootstrap-health` limit is per client IP and process-local. Behind a proxy, trusted forwarded headers must be configured or probes share one partition.
- Orchestrator wiring is deployment-specific and not done here: liveness should use `/health/live` and readiness `/health/ready`. No deploy or production change was made.
- Documentation correction: the V89 CI run is 44660361862 with run number 248 (the manifest and the handoff command were right). `CONTINUE_HERE.md` previously said "run 238" by mistake and is fixed in this checkpoint.

## Still blocked or requiring decisions

1. Dedicated appointment rescheduling and customer cancellation windows need explicit product-owner decisions on replacement/history semantics and customer-facing policy.
2. The transactional outbox has no dispatcher/consumer. Implement only with real idempotent handlers, retry/backoff, poison/dead-letter handling, observability and approved provider/credential/cost choices.
3. Data Protection key-ring persistence/protection and certificate rotation remain blocked until old-key compatibility and a coordinated rotation plan exist. The limiter HMAC key must stay identical across instances and must not be rotated without a coordinated plan.
4. Platform Admin and cross-tenant A1/A2 actions remain blocked pending explicit approval of realm, actor/scope, step-up MFA/four-eyes, self/last-Owner rules and tenant-scoped audit.
5. The official WhatsApp Cloud API integration is not implemented and depends on Meta Business account approval, provider choice, credentials and costs.
6. Production readiness still requires independent security review, backup/restore validation, trusted proxy configuration and deployment-specific secret/key controls. CI success is not production authorization.

## Continuation

- Read `CLAUDE.md`, `docs/continuity/CONTINUE_HERE.md`, this highest-numbered status, `docs/continuity/manifest.json` and `docs/technical/DISTRIBUTED_AUTH_RATE_LIMITING_V1.md` before continuing.
- Active worktree: `C:\Users\iaras\Downloads\atendee-beauty-v85-work`. Preserve `C:\Users\iaras\Downloads\atendee-beauty-git` untouched.
