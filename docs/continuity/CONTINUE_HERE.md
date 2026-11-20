# CONTINUE HERE - Atendee Beauty

Read `CLAUDE.md` and this file before editing. Then read the highest-numbered continuation status.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest validated implementation head: `98d8fc47009e67dfd691180261344ce260d41b9c` (V92 bounded API request sizes, on top of the V91 baseline API security response headers at `d495c65aacbd05c59c8934b3428a9cb1e5da86bb` and the V90 database readiness endpoint at `399ead5e86aadde12eb7e06a03051aa839767e6e` and V89 at `90a5bc6c79fa53a91471b3ec0204c8e9b17b3e34`).
- Latest full CI: run 255 (`44667879306`), success on that exact head; V91 CI remains run 253 (`44665423258`); every job step succeeded (frontend, portal E2E, Release build, domain, PostgreSQL/RLS, Identity/TOTP, HTTP and full-stack checks). V90 CI remains run 251 (`44663263477`).
- V93 adds comprehensive HTTP checks in `tests/Atendee.Api.HttpChecks` verifying the 1 MiB request body limit (HTTP 413) on unknown routes with all baseline security headers, and that the exact ceiling size is accepted. Health endpoints are GET-only in production; TestServer routes unmatched POST to 404 before pre-routing middleware. CI run 259 (`44671789665`) in progress on head `e1fec9ade0b97652d94e3a9e7bb20cd56c7106ce`.
- V92 adds a 1 MiB request-body ceiling (HTTP 413 before routing/authentication/antiforgery for oversized Content-Length, Kestrel MaxRequestBodySize for chunked bodies), 32 KiB header total, 100 headers, 8 KiB request line and a 15 second header timeout. See `docs/continuity/CONTINUATION_STATUS_V92_2026-11-20.md`.
- V91 adds Content-Security-Policy (default-src 'none', frame-ancestors 'none'), Cross-Origin-Resource-Policy same-origin, Cross-Origin-Opener-Policy same-origin and X-Permitted-Cross-Domain-Policies none to every API response, asserted on /health/live, /health/ready and a 404. See `docs/continuity/CONTINUATION_STATUS_V91_2026-11-20.md`.
- V90 adds anonymous `GET /health/ready`: `SELECT 1` against the shared identity data source and the tenant database under a 2 second timeout; 200 `{"status":"ready"}` or 503 `{"status":"unavailable"}`, always `Cache-Control: no-store`, no dependency details. It checks reachability only (not schema, migrations or secrets) and uses the process-local `bootstrap-health` limit. Orchestrator wiring is deployment-specific and was not done.
- Correction: the V89 CI run is 44660361862 = run 248 (an earlier version of this file said 238 by mistake).
- V89 adds migration 021, atomic PostgreSQL fixed-window counters shared by API instances, HMAC-SHA256 IP partition digests, bounded cleanup and fail-closed 503/429 behavior. The partition secret must be the same across instances and supplied securely; do not rotate without an approved plan.
- V88 appointment booking/lifecycle, immutable snapshots, idempotency, overlap prevention, transactional audit/events/outbox and availability occupancy remain validated on run 237 (`44646121213`).
- Read `docs/continuity/CONTINUATION_STATUS_V93_2026-11-20.md` for V93, `docs/continuity/CONTINUATION_STATUS_V92_2026-11-20.md` for V92, `docs/continuity/CONTINUATION_STATUS_V91_2026-11-20.md` for V91, `docs/continuity/CONTINUATION_STATUS_V90_2026-11-19.md` for V90 scope, limits and exact CI evidence, and `docs/continuity/CONTINUATION_STATUS_V89_2026-11-19.md` for V89; read `docs/technical/DISTRIBUTED_AUTH_RATE_LIMITING_V1.md` for operational configuration. The copyable handoff command is in `docs/continuity/COMANDO_CONTINUIDADE.txt`.

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
