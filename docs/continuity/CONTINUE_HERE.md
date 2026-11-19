# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V78_2026-11-19.md` (approved).
- Latest validated head: `867496cce35baae69e35c2cd7cdbd2f05de32320`.
- V78 CI run `44569197822` passed completely: frontend audit/tests/build, portal browser tests, Release build, 26 domain checks, 10 PostgreSQL/RLS checks, 49 Identity/TOTP checks, HTTP authentication/session-security checks and real Kestrel + PostgreSQL full-stack browser test. Disposable credentials were cleared and the PostgreSQL container removed.
- V78 confirms `IOperationalAccountSecurityService` is registered internally but has no API endpoint call sites. Do not expose `DisableAsync` or `RevokeSessionsAsync` directly: the service does not perform tenant/role authorization itself.
- `docs/technical/ADMIN_ACTIONS_DESIGN_V1.md` remains a proposal. It now documents the internal service separately from absent administrative endpoints and lists unresolved owner decisions.
- Historical checkpoints V23–V77 are preserved. Do not rewrite historical checkpoints.
- Next: no A1/A2 administrative endpoint until explicit owner approval of actor/tenant/role scope, self/last-Owner rules, Platform Admin realm, step-up MFA/four-eyes and tenant-scoped audit requirements. Continue unrelated backend work without crossing this boundary.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Preserve audit-before-commit/cookie ordering, per-identity row locking and the independent TOTP replay guard.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.

---
