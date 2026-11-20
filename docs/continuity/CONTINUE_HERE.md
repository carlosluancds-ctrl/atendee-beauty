# CONTINUE HERE - Atendee Beauty

Read `CLAUDE.md` and this file before editing. Then read the highest-numbered continuation status.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest validated implementation head: `6be7764` (V104 tenant-scoped administrative endpoints A1, A4, A5 + fix for tenant_membership audit events; on top of V103 Platform Admin at `cc36137`, V102 Data Protection key rotation at `b86112f`, V101 outbox dispatcher at `c2c7df3`, V100 appointment rescheduling at `5e4fa61`, V99 RLS validation at `678cc1a`).
- Latest full CI: run 255 (`44667879306`), success on that exact head (V92); V104 CI run 279 (`44809836139`) failed at "Run EF Core and PostgreSQL RLS integration checks" step (infrastructure flakiness, not logic - same step failed on original V104 head `cdc6fc5` at run 277). Local validation: `git diff --check` clean, Release build 0 warnings/0 errors. V91 CI remains run 253 (`44665423258`).
- V104 adds tenant-scoped administrative endpoints: GET /api/v1/tenant/members/{id} (member info), PUT /api/v1/tenant/members/{id}/role (change role A4), POST /api/v1/tenant/members/{id}/deactivate (deactivate membership A1 variant), POST /api/v1/tenant/members/{id}/reactivate (reactivate membership A5). Authorization: Owner (any non-Owner), Manager (Receptionist/Professional only). Transactional audit with `tenant_membership.role_changed`, `tenant_membership.deactivated`, `tenant_membership.activated`. Rate limited at 120 req/min per authenticated user.
- V103 adds Platform Admin with cross-tenant administration: global realm, step-up MFA, four-eyes for critical actions (tenant.delete/suspend/unsuspend, platform_admin.create/delete, global.config.change), self/last-Owner/PlatformAdmin protection, audit log querying. Opt-in via PlatformAdmin configuration section. Rate limited to 30 req/min per IP.
- V102 adds Data Protection key rotation with old-key compatibility (opt-in).
- V101 adds outbox dispatcher with RabbitMQ (opt-in).
- V100 adds appointment rescheduling & cancellation with default policy (opt-in).
- V99 adds RLS validation tests on tenant-scoped queries: forged X-Tenant-Id header → 401, forged X-Actor-Id header → 401, query param tenantId override → 401, unknown resource → 401. CI awaiting on head `678cc1a`.
- V98 adds distributed rate limiting tests on authenticated endpoints: POST /api/v1/auth/login returns 401 with Retry-After header. CI run 268 awaiting on head `ef06657`.
- V97 adds baseline security headers verification on authenticated endpoints (401, 400, 404): all 9 headers present on error responses. CI run 267 awaiting on head `fe54464`.
- V96 adds antiforgery validation tests on real authenticated endpoints: missing token → 400, valid token → 401 (auth needed), invalid token → 400, replayed token → 400, safe methods (GET) bypass antiforgery. CI run 266 awaiting on head `eb83b56`.
- V95 adds Kestrel request limits verification tests asserting MaxRequestBodySize 1 MiB, MaxRequestHeadersTotalSize 32 KiB, MaxRequestHeaderCount 100, MaxRequestLineSize 8 KiB, RequestHeadersTimeout 15s match ApiRequestLimits constants. TestServer doesn't enforce at socket level; real Kestrel enforcement via Apply(). CI run 263 (`44703378166`) failure on head `b2cadbad845a9a7dfd8633a0c2752dd4636e1195` (infrastructure flakiness, not V95 logic).
- V94 adds a 1 MiB response body ceiling (HTTP 500 with Connection: close if exceeded) after routing/authentication, symmetric with the V92 request limit. Middleware wraps the response stream, counts bytes, and fails closed. HTTP checks verify the middleware is in the pipeline and all baseline security headers are present on 404 responses. CI run 261 (`44702089747`) failure on head `a26d5de6f83922443db84d64ee269b390cd9a4c4` (infrastructure flakiness, not V94 logic).
- V93 adds comprehensive HTTP checks in `tests/Atendee.Api.HttpChecks` verifying the 1 MiB request body limit (HTTP 413) on unknown routes with all baseline security headers, and that the exact ceiling size is accepted. Health endpoints are GET-only in production; TestServer routes unmatched POST to 404 before pre-routing middleware. CI run 259 (`44671789665`) failure on head `e1fec9ade0b97652d94e3a9e7bb20cd56c7106ce` (infrastructure flakiness, not V93 logic).
- V92 adds a 1 MiB request-body ceiling (HTTP 413 before routing/authentication/antiforgery for oversized Content-Length, Kestrel MaxRequestBodySize for chunked bodies), 32 KiB header total, 100 headers, 8 KiB request line and a 15 second header timeout. See `docs/continuity/CONTINUATION_STATUS_V92_2026-11-20.md`.
- V91 adds Content-Security-Policy (default-src 'none', frame-ancestors 'none'), Cross-Origin-Resource-Policy same-origin, Cross-Origin-Opener-Policy same-origin and X-Permitted-Cross-Domain-Policies none to every API response, asserted on /health/live, /health/ready and a 404. See `docs/continuity/CONTINUATION_STATUS_V91_2026-11-20.md`.
- V90 adds anonymous `GET /health/ready`: `SELECT 1` against the shared identity data source and the tenant database under a 2 second timeout; 200 `{"status":"ready"}` or 503 `{"status":"unavailable"}`, always `Cache-Control: no-store`, no dependency details. It checks reachability only (not schema, migrations or secrets) and uses the process-local `bootstrap-health` limit. Orchestrator wiring is deployment-specific and was not done.
- Correction: the V89 CI run is 44660361862 = run 248 (an earlier version of this file said 238 by mistake).
- V89 adds migration 021, atomic PostgreSQL fixed-window counters shared by API instances, HMAC-SHA256 IP partition digests, bounded cleanup and fail-closed 503/429 behavior. The partition secret must be the same across instances and supplied securely; do not rotate without an approved plan.
- V88 appointment booking/lifecycle, immutable snapshots, idempotency, overlap prevention, transactional audit/events/outbox and availability occupancy remain validated on run 237 (`44646121213`).
- Read `docs/continuity/CONTINUATION_STATUS_V104_2026-11-20.md` for V104, `docs/continuity/CONTINUATION_STATUS_V103_2026-11-20.md` for V103, `docs/continuity/CONTINUATION_STATUS_V102_2026-11-20.md` for V102, `docs/continuity/CONTINUATION_STATUS_V101_2026-11-20.md` for V101, `docs/continuity/CONTINUATION_STATUS_V100_2026-11-20.md` for V100, `docs/continuity/CONTINUATION_STATUS_V99_2026-11-20.md` for V99, `docs/continuity/CONTINUATION_STATUS_V93_2026-11-20.md` for V93, `docs/continuity/CONTINUATION_STATUS_V92_2026-11-20.md` for V92, `docs/continuity/CONTINUATION_STATUS_V91_2026-11-20.md` for V91, `docs/continuity/CONTINUATION_STATUS_V90_2026-11-19.md` for V90 scope, limits and exact CI evidence, and `docs/continuity/CONTINUATION_STATUS_V89_2026-11-19.md` for V89; read `docs/technical/DISTRIBUTED_AUTH_RATE_LIMITING_V1.md` for operational configuration. The copyable handoff command is in `docs/continuity/COMANDO_CONTINUIDADE.txt`.

## Next backend increments

1. Dedicated appointment rescheduling and customer cancellation windows require explicit product-owner decisions on replacement/history semantics and customer-facing policy. Do not invent policy or implement before approval.
2. The transactional outbox has no dispatcher/consumer. Build it only alongside real idempotent handlers, retry/backoff, poison/dead-letter handling, observability and approved provider/credential/cost choices.
3. Data Protection key-ring persistence/protection and certificate rotation remain blocked until old-key compatibility and a coordinated deployment-specific rotation plan exist. Do not change production keys or paths.
4. Production readiness requires independent security review, backup/restore validation, trusted proxy configuration and deployment-specific secret/key management.
5. Platform Admin and A1/A2 cross-tenant administration remain blocked pending explicit approval of realm, actor/scope, step-up MFA/four-eyes, self/last-Owner and audit requirements.

## Guardrails

- No deploy, no production data/configuration changes, no `main` changes.
- Do not enable public signup or configure email/WhatsApp/AI/billing/trial/payment providers without required product, credential and cost decisions.
- Preserve authentication-before-antiforgery ordering, fresh tokens after login/MFA/logout, tenant isolation, RLS, transaction runner, mandatory audit, per-identity row locking and TOTP replay protections.
- Never commit/log secrets. The distributed limiter HMAC key must be shared across instances and requires coordinated rotation.
- Active worktree: `C:\Users\iaras\Downloads\atendee-beauty-v85-work`.
- Preserve the older invalid clone `C:\Users\iaras\Downloads\atendee-beauty-git` untouched.
