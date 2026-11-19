# Atendee Beauty — Continuity Status V73 (approved, 2026-11-19)

## Result

V73 approved after the complete PostgreSQL 18 persistence integration workflow passed on SHA `eb38175ef2b3b6a325535740b99f27f9fcbd28e6`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44461641990)
- Run: `44461641990`, attempt 1, conclusion `success`.
- Job: `Build and verify tenant isolation`, conclusion `success`.

## Scope V73 — serialize failed-attempt updates across password and MFA flows

### Shared per-identity locking

Extracted `LockAndReloadIdentityUserAsync` in `OperationalAuthenticationEndpoints`. Within an existing Identity transaction, it:
1. Acquires `SELECT ... FOR UPDATE` on the target `identity."AspNetUsers"` row.
2. Reloads the tracked `OperationalIdentityUser` after the lock is acquired.
3. Returns false if the identity row no longer exists.

The helper is now used before password login lockout checks, MFA challenge/recovery-code counter updates and resets, and MFA enrollment failure/success counter updates. MFA paths recheck the current lockout state after acquiring the lock. Enrollment also rechecks `TwoFactorEnabled` to avoid acting on a stale setup state.

The failed-attempt and reset Identity operations now check their returned `IdentityResult` before writing the associated audit event. A failed counter mutation returns HTTP 500 and rolls back rather than silently recording a success/failure event without the required state change.

The custom TOTP replay guard remains a separate atomic compare-and-set transaction. This change does not weaken or move that replay guard.

### Cross-flow HTTP/PostgreSQL regression coverage

Two synthetic identities exercise independent real HTTP sessions against PostgreSQL with a test-only delayed counter-update trigger:

1. MFA challenge identity, counter seeded at two:
   - Invalid TOTP challenge, invalid recovery-code challenge and wrong-password login run concurrently.
   - All three failures are counted; the third reaches the configured five-attempt lockout.
   - Exactly one `operational_auth.login_failed` and two `operational_mfa.challenge_failed` events are recorded.
   - A further challenge request against the locked account is rejected without another failure audit event.

2. MFA-enrollment-pending identity, counter seeded at three:
   - Invalid enrollment TOTP verification and wrong-password login run concurrently.
   - Both failures are counted; the second reaches lockout.
   - Exactly one `operational_auth.login_failed` and one `operational_mfa.enrollment_failed` event are recorded.
   - A further enrollment verification against the locked account is rejected without another failure audit event.

The PostgreSQL trigger/function is removed in `finally` after each concurrent scenario. All test identities and database state are confined to the disposable CI database.

### Complete CI evidence — run 44461641990

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed (6.6s).
- Release build: success.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success, including the new cross-flow concurrency assertions.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed (5.0s).
- Disposable database credentials cleared and PostgreSQL container removed.

## Commit

- `eb38175ef2b3b6a325535740b99f27f9fcbd28e6` — serialize MFA failed-attempt updates across auth flows and add concurrent password/TOTP/recovery/enrollment regressions.

## Historical checkpoints preserved

V64 antiforgery session transitions, V65 bidirectional service isolation, V66 professional identity/profile isolation, V67 cross-tenant service assignment rejection without partial mutation, V68 trusted-context spoofing resistance, V69 onboarding actor and TOTP audit atomicity, V70 failed-password counter/audit atomicity, V71 pending-MFA audit failure/cookie isolation and V72 login lockout serialization remain in the passing CI flow. Previous continuity files are unchanged.

## Next step

Review account disable/session-revocation racing with in-flight login and MFA completion. Prove that an identity disabled or revoked while password verification, MFA challenge or MFA enrollment is in progress cannot receive a usable new application/setup/challenge session after the state change. Preserve audit-before-cookie ordering, the independent TOTP replay guard, and the rule that no cross-tenant administrative disable/revoke feature is added without explicit owner approval.

## Guardrails

- `main` remains untouched.
- No deployment or production data changes.
- PostgreSQL CI database is disposable and removed at job completion.
- No public signup, SES configuration, billing/trial activation or production mutation.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval and the authorization design in `CLAUDE.md`.
