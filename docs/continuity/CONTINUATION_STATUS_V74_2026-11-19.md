# Atendee Beauty — Continuity Status V74 (approved, 2026-11-19)

## Result

V74 approved after the complete PostgreSQL 18 persistence integration workflow passed on implementation SHA `86565921a64332864fa97d386685f977cd5df69d`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44558003655)
- Run: `44558003655`, attempt 1, conclusion `success`.
- Job: `Build and verify tenant isolation`, conclusion `success`.

## Scope V74 — preserve the MFA setup session after authenticator-key initialization

### Root cause

Starting MFA enrollment initializes the authenticator key through `ResetAuthenticatorKeyAsync`, which rotates the Identity security stamp. The existing short-lived `MfaSetup` ticket still carried the previous stamp, so the subsequent enrollment verification request was rejected as unauthorized before validating the submitted TOTP code.

### Implementation

- Authenticate the existing `MfaSetup` ticket and capture its original `ExpiresUtc` before changing the authenticator key.
- Keep the existing per-identity row lock and transaction around the setup-state revalidation and authenticator-key initialization.
- Commit the Identity transaction before emitting the replacement ticket.
- Reissue the `MfaSetup` cookie with the updated security stamp and the same identity/stage claims.
- Preserve the exact original expiration. If the original ticket has no expiration, has already expired, or the refreshed stamp is blank, clear the setup cookie and reject; do not extend the setup session.
- Keep the audit-before-cookie ordering from password verification and do not change the independent TOTP replay guard.

### Regression coverage

- Assert that enrollment start emits the refreshed `MfaSetup` cookie.
- Obtain fresh antiforgery request tokens after setup-ticket refresh, enrollment, and login/MFA session transitions.
- Verify blank and invalid authenticator codes reach enrollment validation and are rejected correctly; successful enrollment still completes and audits.
- Update the cross-flow enrollment concurrency scenario to use the refreshed session and antiforgery token.
- Strengthen pending challenge/setup revocation checks to verify the temporary cookies are actually issued before revocation/disablement, that revocation changes the security stamp without relying on lockout, and that disabled/revoked pending sessions cannot complete afterward.

### Complete CI evidence — run 44558003655

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed (6.0s).
- Release build: success.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed (5.0s).
- Disposable database credentials cleared and PostgreSQL container removed.

## Commit

- `86565921a64332864fa97d386685f977cd5df69d` — refresh MFA setup ticket after authenticator reset and update HTTP regressions.

## Historical checkpoints preserved

V64 antiforgery session transitions, V65 bidirectional service isolation, V66 professional identity/profile isolation, V67 cross-tenant service assignment rejection without partial mutation, V68 trusted-context spoofing resistance, V69 onboarding actor and TOTP audit atomicity, V70 failed-password counter/audit atomicity, V71 pending-MFA audit failure/cookie isolation, V72 login lockout serialization and V73 cross-flow MFA lockout serialization remain in the passing CI flow. Previous continuity files are unchanged.

## Next step

Continue the V73 security review: test account disable/session-revocation racing with in-flight password verification, MFA challenge completion and MFA enrollment. Prove that a state change committed during those operations cannot result in a usable application, setup or challenge session afterward. Preserve audit-before-cookie ordering, the independent TOTP replay guard and existing row-lock serialization. Do not add cross-tenant administrative disable/revoke functionality without explicit owner approval and the authorization design in `CLAUDE.md`.

## Guardrails

- `main` remains untouched.
- No deployment or production data changes.
- PostgreSQL CI database is disposable and removed at job completion.
- No public signup, SES configuration, billing/trial activation or production mutation.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval and the authorization design in `CLAUDE.md`.
