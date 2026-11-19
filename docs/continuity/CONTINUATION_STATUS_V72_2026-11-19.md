# Atendee Beauty — Continuity Status V72 (approved, 2026-11-19)

## Result

V72 approved after the complete PostgreSQL 18 persistence integration workflow passed on SHA `9dd9bfe6ac90fdcfd60401ba0e3eaf7c11ab4af3`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44460367169)
- Run: `44460367169`, attempt 1, conclusion `success`.
- Job: `Build and verify tenant isolation`, conclusion `success`.

## Scope V72 — serialize concurrent password-failure and lockout updates

### Implementation

`OperationalAuthenticationEndpoints.LoginAsync` now opens the identity transaction and locks the matching `identity."AspNetUsers"` row with `SELECT ... FOR UPDATE` before checking the password.

After acquiring the row lock, the endpoint reloads the tracked Identity user, then reads `IsLockedOutAsync` and calls `CheckPasswordSignInAsync`. The failed-attempt counter and mandatory `operational_auth.login_failed` event remain in the same transaction. The transaction commits before tenant-membership resolution.

This serializes login attempts for one identity and avoids using a stale `ConcurrencyStamp` / `AccessFailedCount` snapshot when simultaneous requests reach the lockout threshold. It preserves the audit-volume policy:
- Attempts begun before lockout are audited, including the attempt that reaches the threshold.
- Requests begun after lockout remain generically rejected and do not generate unbounded per-attempt audit rows.
- The configured limit remains five failed attempts and the lockout duration remains fifteen minutes.

### HTTP regression coverage

The real HTTP/PostgreSQL integration suite now verifies:

1. Five sequential wrong-password attempts each return HTTP 401; the threshold-causing fifth attempt locks the account and is audited.
2. Two simultaneous wrong-password requests, started with the counter three attempts below the limit, both return HTTP 401 and are both counted/audited; the second reaches lockout.
3. A subsequent request against the already-locked account remains generically rejected.
4. The subsequent locked-account request does not add another `operational_auth.login_failed` event.

A test-only PostgreSQL trigger delays the targeted counter update to make the concurrent-request scenario exercise the row-lock serialization path. The trigger and function are removed in a `finally` block. The CI database is disposable.

### First CI attempt and correction

- Commit `bd9543e43b5ce773471a48c008b728ca90a6a809` introduced row locking and regression coverage.
- Run `44460171062` failed at Release compilation because the API file lacked the EF Core namespace exposing `GetDbConnection()`.
- Commit `9dd9bfe6ac90fdcfd60401ba0e3eaf7c11ab4af3` added the missing `Microsoft.EntityFrameworkCore` import.
- The complete workflow for the corrected SHA passed; no test or security rule was weakened.

## Complete CI evidence — run 44460367169

- Frontend dependency audit, tests and build: success.
- Portal browser E2E: 8 passed (7.3s).
- Release build: success.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success.
- Sequential lockout threshold and audit assertions: all passed.
- Concurrent lockout serialization and post-lockout audit suppression assertions: all passed.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed (6.6s).
- Disposable database credentials cleared and PostgreSQL container removed.

## Commits

- `bd9543e43b5ce773471a48c008b728ca90a6a809` — serialize concurrent login lockout updates and add sequential/concurrent HTTP regression checks.
- `9dd9bfe6ac90fdcfd60401ba0e3eaf7c11ab4af3` — import EF Core relational connection extensions.

## Historical checkpoints preserved

V64 antiforgery session transitions, V65 bidirectional service isolation, V66 professional identity/profile isolation, V67 cross-tenant service assignment rejection without partial mutation, V68 trusted-context spoofing resistance, V69 onboarding actor and TOTP audit atomicity, V70 failed-password counter/audit atomicity, and V71 pending-MFA audit failure/cookie isolation remain in the passing CI flow. Previous continuity files are unchanged.

## Next step

Review cross-flow concurrency on the same Identity user's failed-attempt counter: password-login failures versus failed TOTP/recovery-code challenges and MFA-enrollment failures. Determine whether those operations can race on `AccessFailedCount` / `ConcurrencyStamp` and lose a legitimate counter increment or its mandatory audit event. If needed, add a shared per-identity locking strategy and deterministic HTTP/PostgreSQL regressions. Preserve the existing TOTP replay guard and transaction boundaries; do not weaken MFA, lockout or audit semantics.

## Guardrails

- `main` remains untouched.
- No deployment or production data changes.
- PostgreSQL CI database is disposable and removed at job completion.
- No public signup, SES configuration, billing/trial activation or production mutation.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval and the authorization design in `CLAUDE.md`.
