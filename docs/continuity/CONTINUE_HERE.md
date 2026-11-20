# CONTINUE HERE - Atendee Beauty

Read `CLAUDE.md` and this file before editing. Then read the highest-numbered continuation status.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest validated implementation head: `90a5bc6c79fa53a91471b3ec0204c8e9b17b3e34` (V89 distributed PostgreSQL authentication rate limiting).
- Latest full CI: run 238 (`44660361862`), success on that exact head. Frontend audit/tests/build, portal browser E2E 8/8, Release build zero warnings/errors, domain 79/79, PostgreSQL 18.7 / EF Core / RLS 43/43, Identity/TOTP 49/49 and HTTP security/integration checks all passed. CI log confirms V89 two-instance concurrency and fail-closed assertions. Disposable credentials cleared and PostgreSQL container removed.
- V89 adds migration 021, atomic PostgreSQL fixed-window counters shared by API instances, HMAC-SHA256 IP partition digests, bounded cleanup and fail-closed 503/429 behavior. The partition secret must be the same across instances and supplied securely; do not rotate without an approved plan.
- V88 appointment booking/lifecycle, immutable snapshots, idempotency, overlap prevention, transactional audit/events/outbox and availability occupancy remain validated on run 237 (`44646121213`).
- Read `docs/continuity/CONTINUATION_STATUS_V89_2026-11-19.md` for full V89 scope and exact CI evidence; read `docs/technical/DISTRIBUTED_AUTH_RATE_LIMITING_V1.md` for operational configuration. The copyable handoff command is in `docs/continuity/COMANDO_CONTINUIDADE.txt`.

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
