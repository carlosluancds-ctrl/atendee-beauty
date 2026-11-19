# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V75_2026-11-19.md` (approved).
- Latest validated head: `8744d1730882dff3ca7c3fe848c8f282934de8fc`.
- V75 CI run `44565239959` passed completely: frontend audit/tests/build, 8 portal browser tests, Release build, 26 domain checks, 10 PostgreSQL/RLS checks, 49 Identity/TOTP checks, HTTP authentication/session-security checks including five deterministic concurrent issuance races, and 1 real Kestrel + PostgreSQL full-stack browser test. Disposable credentials were cleared and the PostgreSQL container removed.
- V75 verifies that cookies emitted after a concurrent revocation/disablement are unusable: pending-MFA challenge, completed MFA challenge, MFA enrollment, direct non-MFA login, and initial MFA setup.
- V75 is test-only; production authentication implementation remains the approved V73/V74 implementation. Test synchronization uses `pg_locks` rather than relying on cross-role `pg_stat_activity` query visibility.
- Historical checkpoints V23–V74 are preserved. Do not rewrite historical checkpoints.
- Next: test audit-write and Identity update failures in `DisableAsync` and `RevokeSessionsAsync`; prove transactions roll back stamp/lockout mutations and no misleading audit row persists.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Preserve audit-before-cookie ordering, per-identity row locking and the independent TOTP replay guard.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.

---
