# CONTINUE HERE - Atendee Beauty

Read `CLAUDE.md` and this file before editing. Then read the highest-numbered continuation status.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest validated implementation head: `d495c65aacbd05c59c8934b3428a9cb1e5da86bb` (V91 baseline API security response headers, on top of the V90 database readiness endpoint at `399ead5e86aadde12eb7e06a03051aa839767e6e` and V89 at `90a5bc6c79fa53a91471b3ec0204c8e9b17b3e34`).
- Latest full CI: run 253 (`44665423258`), success on that exact head; every job step succeeded (frontend, portal E2E, Release build, domain, PostgreSQL/RLS, Identity/TOTP, HTTP and full-stack checks). V90 CI remains run 251 (`44663263477`).
- V91 adds Content-Security-Policy (default-src 'none', frame-ancestors 'none'), Cross-Origin-Resource-Policy same-origin, Cross-Origin-Opener-Policy same-origin and X-Permitted-Cross-Domain-Policies none to every API response, asserted on /health/live, /health/ready and a 404. See `docs/continuity/CONTINUATION_STATUS_V91_2026-11-20.md`.
- V90 adds anonymous `GET /health/ready`: `SELECT 1` against the shared identity data source and the tenant database under a 2 second timeout; 200 `{"status":"ready"}` or 503 `{"status":"unavailable"}`, always `Cache-Control: no-store`, no dependency details. It checks reachability only (not schema, migrations or secrets) and uses the process-local `bootstrap-health` limit. Orchestrator wiring is deployment-specific and was not done.
- Correction: the V89 CI run is 44660361862 = run 248 (an earlier version of this file said 238 by mistake).
- V89 adds migration 021, atomic PostgreSQL fixed-window counters shared by API instances, HMAC-SHA256 IP partition digests, bounded cleanup and fail-closed 503/429 behavior. The partition secret must be the same across instances and supplied securely; do not rotate without an approved plan.
- V88 appointment booking/lifecycle, immutable snapshots, idempotency, overlap prevention, transactional audit/events/outbox and availability occupancy remain validated on run 237 (`44646121213`).
- Read `docs/continuity/CONTINUATION_STATUS_V91_2026-11-20.md` for V91, `docs/continuity/CONTINUATION_STATUS_V90_2026-11-19.md` for V90 scope, limits and exact CI evidence, and `docs/continuity/CONTINUATION_STATUS_V89_2026-11-19.md` for V89; read `docs/technical/DISTRIBUTED_AUTH_RATE_LIMITING_V1.md` for operational configuration. The copyable handoff command is in `docs/continuity/COMANDO_CONTINUIDADE.txt`.

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
