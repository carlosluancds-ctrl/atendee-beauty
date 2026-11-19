# Atendee Beauty — Continuity Status V76 (approved, 2026-11-19)

## Result

V76 approved after the complete PostgreSQL 18 persistence integration workflow passed on validation head `d2d51327e639ae18755d9cf4cafd47f29892dc0e`, branch `security/csrf-webhook-antiforgery-v23`.

- Workflow: [PostgreSQL 18 persistence integration](https://github.com/carlosluancds-ctrl/atendee-beauty/actions/runs/44566734046)
- Run: `44566734046`, attempt 1, conclusion `success`.
- Job: `Build and verify tenant isolation`, conclusion `success`.
- V76 is a test-only security regression increment. No production authentication or account-security implementation was changed; existing transaction boundaries in `OperationalAccountSecurityService` were exercised with deterministic Identity-table and audit-table failures.

## Scope V76 — atomic rollback for account disablement and session revocation

### Failure-injection coverage

Test-only PostgreSQL triggers inject failures at both persistence boundaries while each service operation runs through its real DI scope and disposable PostgreSQL database.

- `DisableAsync`: an Identity `LockoutEnd` update failure aborts the operation; `LockoutEnd` remains null, the original `SecurityStamp` is preserved, and no `operational_account.disabled` event is written.
- `RevokeSessionsAsync`: an Identity `SecurityStamp` update failure aborts the operation; the original stamp is preserved and no `operational_account.sessions_revoked` event is written.
- `DisableAsync`: a mandatory `operational_account.disabled` audit insert failure rolls back both the lockout and security-stamp mutations; no audit event persists.
- `RevokeSessionsAsync`: a mandatory `operational_account.sessions_revoked` audit insert failure rolls back the security-stamp mutation; no audit event persists.

The tests verify the durable database state from fresh scopes after each failed transaction. Test triggers are removed after each scenario; the CI database is disposable and removed at job completion.

### Complete CI evidence — run 44566734046

- Frontend dependency audit: success, 0 vulnerabilities.
- Frontend tests and build: success.
- Portal browser E2E: 8 passed (5.8s).
- Release build: success.
- Domain checks: 26/26 passed.
- PostgreSQL 18.7 / EF Core / RLS persistence checks: 10/10 passed.
- Identity, TOTP and tenant-context checks: 49/49 passed.
- HTTP authentication, antiforgery, cookies, session revocation, logout, MFA, audit, role-matrix and tenant-status checks: success, including all four V76 failure-injection scenarios and the V75 concurrency regressions.
- Real Kestrel + disposable PostgreSQL full-stack browser E2E: 1 passed (4.8s).
- Disposable database credentials cleared and PostgreSQL container removed.

## Commit

- `d2d51327e639ae18755d9cf4cafd47f29892dc0e` — verify account-security actions roll back on Identity mutation and mandatory audit failures.

## Historical checkpoints preserved

V64–V75 remain unchanged and passing in the full CI workflow. V75's five authentication/session issuance race scenarios, V74's refreshed MFA setup ticket and expiry preservation, V73's serialized Identity updates, and the independent TOTP replay guard remain intact.

## Next step

Add a focused test for explicit `IdentityResult.Failed` responses from the UserManager store in `DisableAsync` and `RevokeSessionsAsync`, separately from the database-exception cases in V76. Prove the service's `EnsureSucceeded` guards abort the transaction before audit persistence. Do not expose or implement cross-tenant administrative endpoints.

## Guardrails

- `main` remains untouched.
- No deployment or production data changes.
- PostgreSQL CI database is disposable and removed at job completion.
- No public signup, SES configuration, billing/trial activation or production mutation.
- Do not implement cross-tenant administrative disable/revoke or tenant-status changes without explicit owner approval and the authorization design in `CLAUDE.md`.

---
