# CONTINUE HERE - Atendee Beauty

Read CLAUDE.md and this file before editing. Then read the highest-numbered continuation status.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: security/csrf-webhook-antiforgery-v23; never modify main.
- Latest validated implementation head: e7efdc40bc2d6c2e88dbb2e182add2379eeea370.
- Latest full CI: run 237 (44646121213), success on that exact head. Frontend audit/tests/build, portal browser E2E 8/8, Release build zero warnings/errors, domain 79/79, PostgreSQL 18.7 / EF Core / RLS 43/43, Identity/TOTP 49/49, HTTP security and appointment integration, real Kestrel + disposable PostgreSQL full-stack browser E2E 1/1. Credentials cleared and disposable PostgreSQL removed.
- V88 adds tenant appointments, immutable booking snapshots, idempotency, transactional audit/events/outbox, PostgreSQL overlap prevention, legal lifecycle transitions and tenant-scoped appointment reads.
- Authoritative availability now subtracts blocking appointment occupancy and buffers. Completed, cancelled and no-show appointments no longer block.
- V87 availability is validated on run 234 (44638633076). V86 and V85 historical validated checkpoints remain preserved.
- Read docs/continuity/CONTINUATION_STATUS_V88_2026-11-19.md for full V88 scope and CI evidence.

## Next backend increments

1. V89: implement shared/distributed authentication rate limiting with an atomic backend and multi-instance concurrency tests. Do not assume a production Redis/managed-service topology; fail closed safely if the shared limiter is unavailable.
2. Dedicated appointment rescheduling is not implemented. Before exposing it, define the replacement/history semantics and customer-facing cancellation policy with the product owner; do not invent policy.
3. Build a durable outbox dispatcher only together with real message handlers, retry/backoff, poison-message/dead-letter handling, observability and idempotent delivery. External providers remain blocked until credentials, cost and product choices are approved.
4. Data Protection certificate rotation remains blocked until old-key decryption compatibility and a coordinated rotation plan are available. Do not change production keys or paths.
5. Production readiness requires independent security review, backup/restore validation, trusted proxy configuration and deployment-specific secret/key management.
6. Platform Admin and A1/A2 cross-tenant administration remain blocked pending explicit approval of realm, actor/scope, step-up MFA/four-eyes, self/last-Owner and audit requirements.

## Guardrails

- No deploy, no production data/configuration changes, no main changes.
- Do not enable public signup or configure email/WhatsApp/AI/billing/trial/payment providers without the required product, credential and cost decisions.
- Preserve authentication-before-antiforgery ordering, fresh tokens after login/MFA/logout, tenant isolation, RLS, transaction runner, mandatory audit, per-identity row locking and TOTP replay protections.
- Active worktree: C:\Users\iaras\Downloads\atendee-beauty-v85-work.
- Preserve the older invalid clone C:\Users\iaras\Downloads\atendee-beauty-git untouched.