# CONTINUE HERE — Atendee Beauty

Read `CLAUDE.md` and the highest-numbered continuity status before editing.

- Repository: https://github.com/carlosluancds-ctrl/atendee-beauty
- Working branch: `security/csrf-webhook-antiforgery-v23`; never modify `main`.
- Latest continuity checkpoint: `docs/continuity/CONTINUATION_STATUS_V74_2026-11-19.md` (approved).
- Latest approved implementation: V74, validated commit `86565921a64332864fa97d386685f977cd5df69d`.
- V74 CI run `44558003655` passed completely: frontend audit/tests/build, 8 portal browser tests, Release build, 26 domain checks, 10 PostgreSQL/RLS checks, 49 Identity/TOTP checks, HTTP authentication/antiforgery/cookie/session-revocation/MFA/audit/tenant checks, and 1 real Kestrel + PostgreSQL full-stack browser test. Disposable credentials were cleared and the PostgreSQL container removed.
- V74 fixes the MFA setup cookie becoming invalid when `ResetAuthenticatorKeyAsync` rotates the Identity security stamp. The setup ticket is reissued with the new stamp while preserving the original expiry; missing/expired expiry fails closed.
- V74 tests fetch fresh antiforgery tokens after authentication-state transitions, assert setup-ticket refresh, and verify pending MFA sessions are rejected after session revocation/account disablement.
- Historical checkpoints V23–V73 are preserved. Do not rewrite historical checkpoints.
- Next: test account disable/session-revocation racing with in-flight password verification, MFA challenge completion and MFA enrollment. Prove a state change committed during those operations cannot produce a usable application/setup/challenge session afterward.
- Preserve authentication-before-antiforgery ordering, fresh request tokens after login/MFA/logout, first-Owner provisioning, tenant isolation, RLS, transaction runner and backend-first authorization.
- Preserve audit-before-cookie ordering and the independent TOTP replay guard.
- Do not deploy, configure SES, activate billing/trial, change production data, or modify `main`. Do not implement cross-tenant administrative disable/revoke or tenant status changes without explicit approval.
