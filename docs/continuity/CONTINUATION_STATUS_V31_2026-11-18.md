
# Atendee Beauty - Continuity Status V31 (2026-11-18)

## Repository
- Branch: security/csrf-webhook-antiforgery-v23 (base wip/operational-identity-v1). main untouched (still 44640018f6fa596755d93f9aa0e5affb713f8344). No deploy, no signup.
- Starting point verified before work: local HEAD = remote HEAD = 117523e62b885ad9c7289eea508999aec546e3a0, clean tree, CI run 44214327637 success.
- Code SHA of this increment: 94848b190a4b88b2379a5d25446712f9e65482b5.

## Increment: audit of MFA enrollment failures and password-verified-pending-MFA (Priority A, part 3)
Migration 006 (db/migrations/006_extend_security_audit_mfa_pending_and_enrollment_failure.sql, applied in CI after 005) extends the audit CHECK constraint with:
- operational_auth.password_verified_mfa_pending
- operational_mfa.enrollment_failed

Application changes (OperationalAuthenticationEndpoints.cs, PostgresSecurityAuditWriter.cs allowlist):
- Login, privileged role without MFA: the pending event is written BEFORE the MFA-setup cookie is issued. If the mandatory write fails the request fails and no cookie exists.
- Login, MFA enabled: the pending event is written after Identity reports RequiresTwoFactor, before the 202 response. No application cookie exists at that point.
- Enrollment verify with an invalid authenticator code: AccessFailedAsync and the enrollment_failed event share one identity-database transaction.
- Actor is NULL for both new events by design: the identity is not yet authenticated. Target is the server-resolved identity (never client-supplied).
- Nothing else is stored: no code, email, IP, password, secret or request body.

Flood / DoS decision:
- Pending events require a verified password and sit behind the auth-login limiter (5/window), so volume is bounded by successful credential checks.
- Enrollment failures sit behind the auth-mfa limiter (10/window) and the Identity lockout counter; once locked out, GetSetupUserAsync rejects before any event is written.
- Malformed enrollment input (blank or over-length code) is rejected before any user lookup and is NOT audited (no trusted target yet).

## Tests (tests/Atendee.Api.HttpChecks) - observed passing in CI
- Owner setup login yields exactly one pending event and no login_succeeded/login_failed.
- Owner MFA-enabled login yields a second pending event.
- Blank enrollment code returns 400 and does not grow the ledger.
- Invalid enrollment code returns 400, yields exactly one enrollment_failed, and does not emit enrolled or challenge_failed.
- Both new event types have zero rows with a non-null actor.
- Manager wrong password yields login_failed and NO pending event; the following correct login yields exactly one pending event.
- Test budget note: the MFA host already uses 5 of 5 auth-login permits; do not add logins there without moving them to a fresh host.

## CI
- GitHub Actions run 44215796005: completed / success on SHA 94848b190a4b88b2379a5d25446712f9e65482b5 (PostgreSQL 18.7). Domain 15/15, persistence 9/9, Identity 23/23, and the HTTP step printed its final RESULT line; all 8 new assertions were seen in the job log. CI evidence only; not production approval.
- Local: Release build 0 warnings / 0 errors, git diff --check clean. Integration checks cannot run locally (no PostgreSQL/Docker on the remote desktop).

## Limitations
- Privileged DB owner/superuser can still alter or disable audit data; the ledger is not tamper-proof against them.
- TOTP challenge failure still writes its audit event outside the failed-attempt transaction (pre-existing); only the new enrollment failure path is transactional.
- No audit query/export endpoint exists.

## Remaining work
- Priority B: documented and tested endpoint authorization matrix (Owner/Manager/Receptionist/Professional), including status codes and MFA/amr requirements.
- Priority C: admin design only (disable/revoke, membership management, audit review/export, Platform Admin) after B.
- Tenant-status access policy still requires the product owner's decision. Not implemented.
